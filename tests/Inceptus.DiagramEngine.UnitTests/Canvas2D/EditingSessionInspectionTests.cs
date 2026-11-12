using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Interaction;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.ConnectionCreation;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.UnitTests.Canvas2D;

public sealed class EditingSessionInspectionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InitialActiveToolIsClearedOnlyForReadOnlyAttachment(bool readOnly, bool pendingEdit)
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var first = inputs.Graph.Nodes[0].Source.VisualStateId!;
        var second = inputs.Graph.Nodes[1].Source.VisualStateId!;
        using var seedScene = EditingSessionTestHarness.CreateScene(inputs, EditorStateSnapshot.Empty);
        var hovered = seedScene.Items.First(item => item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == first).Id;
        var initial = new EditorStateSnapshot(selection: [first], hoveredObjectId: hovered,
            activeToolId: "test:placement", focusTargetId: "test:canvas",
            viewport: new(0.8, new(-10, -20)));
        if (pendingEdit)
        {
            var connection = ConnectionToolState.WithSelection(initial, new("test:connection"));
            initial = new EditorStateSnapshot(initial.Selection, initial.HoveredObjectId,
                connection.ActiveToolId, initial.FocusTargetId, initial.Viewport,
                activeGesture: new("pending", "test:edit", default, new(10, 10)),
                temporaryFeedback: [new("pending-feedback", "test:preview", new RectD(10, 10, 20, 20))],
                toolState: connection.ToolState);
        }
        var configuration = EditingSessionTestHarness.Configuration(initial);
        if (readOnly) configuration = configuration.WithAccessMode(EditingSessionAccessMode.ReadOnly);
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var original = document.CaptureSnapshot();
        var builder = new Canvas2DSceneBuilder().WithEditingEnabled(!readOnly);
        var pipeline = new ControlledEditingSessionPipeline();
        EditorStateSnapshot? firstPipelineState = null;
        EditingSessionPipelineResult Build(EditingSessionPipelineArtifacts artifacts, EditorStateSnapshot editor) =>
            EditingSessionPipelineResult.Success(artifacts, builder.Build(artifacts.ProjectedGraph,
                artifacts.LayoutResult, artifacts.RoutingResult, original.VisualModel, editor).Scene!);
        pipeline.EnqueueFull((_, editor, _) =>
        {
            firstPipelineState = editor;
            return ValueTask.FromResult(Build(EditingSessionTestHarness.CreateArtifacts(inputs), editor));
        });
        for (var index = 0; index < 10; index++)
            pipeline.EnqueueScene((artifacts, _, editor, _) => ValueTask.FromResult(Build(artifacts, editor)));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attached = await EditingSession.AttachAsync(document, renderer, configuration, pipeline);
        Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
        await using var session = attached.Session!;
        await using var controller = new Canvas2DInteractionController(session);
        var before = session.CaptureState();
        Assert.NotNull(firstPipelineState);
        foreach (var state in new[] { firstPipelineState, before.EditorState })
        {
            Assert.Equal(readOnly ? null : initial.ActiveToolId, state.ActiveToolId);
            Assert.True(initial.Selection.AsSpan().SequenceEqual(state.Selection.AsSpan()));
            Assert.Equal(initial.HoveredObjectId, state.HoveredObjectId);
            Assert.Equal(initial.FocusTargetId, state.FocusTargetId);
            Assert.Equal(initial.Viewport, state.Viewport);
            Assert.Null(state.ActiveGesture);
            Assert.Empty(state.TemporaryFeedback);
            Assert.Empty(state.ToolState);
            Assert.Null(ConnectionToolState.GetSelection(state));
        }
        Assert.Same(initial, configuration.InitialEditorState);
        Assert.Equal(pendingEdit ? ConnectionToolState.ToolId : "test:placement", initial.ActiveToolId);
        if (pendingEdit)
        {
            Assert.NotNull(initial.ActiveGesture);
            Assert.Single(initial.TemporaryFeedback);
            Assert.Equal(new AnchorConnectionCreationId("test:connection"), ConnectionToolState.GetSelection(initial));
        }
        Assert.Equal(0, before.HistoryStatus.EntryCount);

        // The second node's document point (230, 35), transformed by the initial viewport.
        var point = new PointD(174, 8);
        var hover = await controller.PointerMovedAsync(point);
        Assert.True(hover.Succeeded);
        Assert.NotNull(hover.SessionState.EditorState.HoveredObjectId);
        Assert.NotEqual(hovered, hover.SessionState.EditorState.HoveredObjectId);
        Assert.True((await controller.PointerPressedAsync(new(1, point, buttons: 1))).Succeeded);
        var selected = await controller.PointerReleasedAsync(new(1, point));
        Assert.True(selected.Succeeded);
        Assert.Equal(second, Assert.Single(selected.SessionState.EditorState.Selection));
        Assert.True((await session.UpdateViewportAsync(new(0.7, new(-20, -30)))).Succeeded);
        Assert.True((await session.PanViewportAsync(new(-5, -10))).Succeeded);
        var after = session.CaptureState();
        Assert.Equal(0.7, after.EditorState.Viewport.Zoom);
        Assert.Equal(new VectorD(-25, -40), after.EditorState.Viewport.Pan);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(original.Revision, after.DocumentRevision);
        Assert.Same(original, document.CaptureSnapshot());
        if (readOnly) AssertNoEditHandles(after.CurrentScene!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectionRetainsSelectionHoverViewportAndCancellation(bool readOnly)
    {
        var (session, document, inputs) = await AttachAsync(readOnly, inspectionScene: readOnly);
        await using var owned = session;
        await using var controller = new Canvas2DInteractionController(session);
        var original = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var first = inputs.Graph.Nodes[0].Source.VisualStateId!;
        var second = inputs.Graph.Nodes[1].Source.VisualStateId!;

        var hover = await controller.PointerMovedAsync(new PointD(30, 35));
        Assert.True(hover.Succeeded);
        Assert.NotNull(hover.SessionState.EditorState.HoveredObjectId);
        if (readOnly) Assert.Equal("pointer", hover.CssCursor);
        await controller.PointerPressedAsync(new(1, new(30, 35), buttons: 1));
        var selected = await controller.PointerReleasedAsync(new(1, new(30, 35)));
        Assert.Equal(first, Assert.Single(selected.SessionState.EditorState.Selection));
        await controller.PointerPressedAsync(new(2, new(230, 35), buttons: 1, controlKey: true));
        var multiple = await controller.PointerReleasedAsync(new(2, new(230, 35), controlKey: true));
        Assert.Equal(new[] { first, second }, multiple.SessionState.SelectionOrder);
        Assert.Contains(multiple.SessionState.CurrentScene!.Items,
            item => item.Origin.StableSourceKey?.StartsWith("selection:", StringComparison.Ordinal) == true);
        if (readOnly) AssertNoEditHandles(multiple.SessionState.CurrentScene);

        var context = await controller.PointerContextMenuAsync(new(30, 35));
        Assert.True(context.Succeeded);
        if (readOnly)
        {
            Assert.Null(context.ConnectorRouteContextAction);
            Assert.Null(context.ConnectorAnchorContextAction);
            Assert.Null(context.NodeLabelContextAction);
        }
        await controller.PointerPressedAsync(new(3, new(30, 35), buttons: 1));
        await controller.PointerCancelledAsync(3);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.True((await controller.PointerLeftAsync()).Succeeded);
        Assert.Null(session.CaptureState().EditorState.HoveredObjectId);
        Assert.True((await session.UpdateViewportAsync(new(0.8, new(-30, -20)))).Succeeded);
        Assert.True((await session.PanViewportAsync(new(-10, -15))).Succeeded);
        Assert.Equal(0.8, session.CaptureState().EditorState.Viewport.Zoom);
        Assert.Equal(new VectorD(-40, -35), session.CaptureState().EditorState.Viewport.Pan);
        Assert.Same(original, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task EditingAcquisitionIsDeniedEvenWhenAnEditingHandleIsSupplied(bool readOnly, int target)
    {
        // Deliberately supply ordinary editable Scene geometry to prove that hiding handles
        // is not the enforcement boundary. The default control must acquire the same gesture.
        var (session, document, _) = await AttachAsync(readOnly, inspectionScene: false, selectConnector: target == 2);
        await using var owned = session;
        await using var controller = new Canvas2DInteractionController(session);
        var original = document.CaptureSnapshot();
        var before = session.CaptureState();
        if (target == 2)
            Assert.Contains(before.CurrentScene!.Items, item =>
                item.Origin.StableSourceKey?.StartsWith("route-bend-handle:", StringComparison.Ordinal) == true);
        var point = target switch { 0 => new PointD(30, 35), 1 => new PointD(110, 70), _ => new PointD(160, 45) };
        await controller.PointerPressedAsync(new(1, point, buttons: 1));
        var moved = await controller.PointerMovedAsync(new Canvas2DPointerInput(1, new(point.X + 25, point.Y + 15), buttons: 1));
        if (readOnly)
        {
            Assert.Null(session.CaptureState().EditorState.ActiveGesture);
            Assert.True(moved.CssCursor is "pointer" or "default");
            var released = await controller.PointerReleasedAsync(new(1, new(point.X + 25, point.Y + 15)));
            Assert.Null(released.PersistentOperation);
        }
        else Assert.NotNull(session.CaptureState().EditorState.ActiveGesture);
        await controller.PointerCancelledAsync(1);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.Same(original, document.CaptureSnapshot());
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task SessionRejectsForcedGestureAndConnectionAcquisitionButAllowsCleanup()
    {
        var (session, document, _) = await AttachAsync(true, inspectionScene: true);
        await using var owned = session;
        var before = session.CaptureState();
        var original = document.CaptureSnapshot();
        var gesture = new EditorStateSnapshot(activeGesture: new("forced", "test:edit", default, new(10, 10)));
        var direct = await session.UpdateEditorStateAsync(gesture);
        var interaction = await session.UpdateEditorStateFromInteractionAsync(before.CurrentScene!, before.Generation,
            before.EditorState, gesture);
        var connection = await session.SelectConnectionToolAsync(new("test:connection"), new([]));
        foreach (var result in new[] { direct, interaction, connection })
        {
            Assert.Equal(EditingSessionOperationStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == EditingSessionDiagnosticCodes.ReadOnly);
        }
        Assert.True((await session.SelectConnectionToolAsync(null, new([]))).Succeeded);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.Same(original, document.CaptureSnapshot());
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task StalePendingPointerAndClosedSessionCannotActivateInspection()
    {
        var (session, _, _) = await AttachAsync(true, inspectionScene: true);
        await using var owned = session;
        await using var controller = new Canvas2DInteractionController(session);
        await controller.PointerPressedAsync(new(1, new(30, 35), buttons: 1));
        Assert.True((await session.UpdateViewportAsync(new(0.7, default))).Succeeded);
        var stale = await controller.PointerReleasedAsync(new(1, new(30, 35)));
        Assert.Equal(Canvas2DInteractionStatus.Stale, stale.Status);
        Assert.Empty(session.CaptureState().EditorState.Selection);
        await session.CloseAsync();
        Assert.False((await controller.PointerActivatedAsync(new(30, 35))).Succeeded);
    }

    [Fact]
    public void InspectionBuilderRetainsSelectionAndDoesNotChangeSharedEditorBuilder()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var builder = new Canvas2DSceneBuilder();
        var inspection = builder.WithEditingEnabled(false);
        foreach (var visual in inputs.VisualModel.VisualStates)
        {
            var state = new EditorStateSnapshot(selection: [visual.Id]);
            var editing = builder.Build(inputs.Graph, inputs.Layout, inputs.Routing, inputs.VisualModel, state).Scene!;
            var viewing = inspection.Build(inputs.Graph, inputs.Layout, inputs.Routing, inputs.VisualModel, state).Scene!;
            Assert.Contains(editing.Items, IsEditHandle);
            AssertNoEditHandles(viewing);
            Assert.Contains(viewing.Items, item => item.Origin.StableSourceKey?.StartsWith("selection:", StringComparison.Ordinal) == true);
            Assert.Equal(editing.Items.Where(item => (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0),
                viewing.Items.Where(item => (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0));
        }
    }

    private static bool IsEditHandle(Canvas2DSceneItem item) =>
        (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 &&
        item.HitTestPolicy.Mode != Canvas2DHitTestMode.None;

    private static void AssertNoEditHandles(Canvas2DScene scene) => Assert.DoesNotContain(scene.Items, IsEditHandle);

    private static async Task<(EditingSession Session, Document Document, Canvas2DSceneTestData Inputs)> AttachAsync(
        bool readOnly, bool inspectionScene, bool selectConnector = false)
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var initial = new EditorStateSnapshot(selection: selectConnector ? [inputs.Graph.Edges[0].Source.VisualStateId!] : []);
        // Gesture fixtures need the node resize handles available before pressing them.
        if (!inspectionScene && !selectConnector) initial = new(selection: [inputs.Graph.Nodes[0].Source.VisualStateId!]);
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var builder = new Canvas2DSceneBuilder().WithEditingEnabled(!inspectionScene);
        var pipeline = new ControlledEditingSessionPipeline();
        EditingSessionPipelineResult Build(EditingSessionPipelineArtifacts artifacts, EditorStateSnapshot editor) =>
            EditingSessionPipelineResult.Success(artifacts, builder.Build(artifacts.ProjectedGraph,
                artifacts.LayoutResult, artifacts.RoutingResult, document.CaptureSnapshot().VisualModel, editor).Scene!);
        pipeline.EnqueueFull((_, editor, _) => ValueTask.FromResult(Build(EditingSessionTestHarness.CreateArtifacts(inputs), editor)));
        for (var index = 0; index < 30; index++)
            pipeline.EnqueueScene((artifacts, _, editor, _) => ValueTask.FromResult(Build(artifacts, editor)));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var configuration = EditingSessionTestHarness.Configuration(initial);
        if (readOnly) configuration = configuration.WithAccessMode(EditingSessionAccessMode.ReadOnly);
        var attached = await EditingSession.AttachAsync(document, renderer, configuration, pipeline);
        Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
        return (attached.Session!, document, inputs);
    }
}
