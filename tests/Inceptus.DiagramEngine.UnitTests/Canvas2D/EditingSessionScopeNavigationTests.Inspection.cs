using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Interaction;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.History;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.UnitTests.Canvas2D;

public sealed partial class EditingSessionScopeNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectionNavigationReusesScopeCachesAndOnlyEditorsRecordHistory(bool readOnly)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var peer = new DocumentScopeId("test:inspection:peer");
        var document = CreateScopedDocument(inputs, new(peer), Scope(ScopeAId, Root(inputs)), Scope(ScopeBId, ScopeAId));
        var original = document.CaptureSnapshot();
        var transform = new ViewportSnapshot(0.8, new(-20, -30));
        var initial = new EditorStateSnapshot(selection: [inputs.Graph.Nodes[0].Source.VisualStateId!],
            viewport: new(transform.Zoom, transform.Pan,
                Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(transform, new(800, 600, 1))));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull((_, editor, _) => ValueTask.FromResult(ControlledEditingSessionPipeline.Success(inputs, editor)));
        for (var index = 0; index < 3; index++)
            pipeline.EnqueueScopedFull((snapshot, scope, editor, _) => ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scope, editor)));
        for (var index = 0; index < 10; index++)
            pipeline.EnqueueScene((artifacts, visuals, editor, _) => ValueTask.FromResult(ControlledEditingSessionPipeline.Success(artifacts, visuals, editor)));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var config = EditingSessionTestHarness.Configuration(initial);
        if (readOnly) config = config.WithAccessMode(EditingSessionAccessMode.ReadOnly);
        var attached = await EditingSession.AttachAsync(document, renderer, config, pipeline);
        await using var session = attached.Session!;
        var rootViewport = session.CaptureState().EditorState.Viewport;
        var transitions = 0;
        foreach (var target in new[] { ScopeAId, ScopeBId, peer, Root(inputs), ScopeAId, ScopeBId })
        {
            var navigation = await session.NavigateToScopeAsync(target);
            Assert.True(navigation.Succeeded);
            transitions++;
            var state = session.CaptureState();
            Assert.Equal(target, state.ActiveScopeId);
            Assert.Empty(state.EditorState.Selection);
            Assert.Null(state.EditorState.ActiveGesture);
            Assert.Null(state.EditorState.HoveredObjectId);
            Assert.Equal(readOnly ? 0 : transitions, state.HistoryStatus.EntryCount);
            Assert.Same(original, document.CaptureSnapshot());
            if (target == Root(inputs)) AssertViewport(rootViewport, state.EditorState.Viewport);
            var same = await session.NavigateToScopeAsync(target);
            Assert.True(same.Succeeded);
            Assert.Same(state.CurrentScene, same.State.CurrentScene);
            Assert.Equal(state.Generation, same.State.Generation);
        }
        Assert.Equal(4, pipeline.FullRunCount);
        Assert.Equal(3, pipeline.SceneRebuildCount);
        var beforeRestore = session.CaptureState();
        var undo = await session.UndoAsync();
        var redo = await session.RedoAsync();
        if (readOnly)
        {
            Assert.Contains(undo.Diagnostics, item => item.Code == EditingSessionDiagnosticCodes.ReadOnly);
            Assert.Contains(redo.Diagnostics, item => item.Code == EditingSessionDiagnosticCodes.ReadOnly);
            Assert.Equal(beforeRestore.HistoryStatus, session.CaptureState().HistoryStatus);
        }
        else
        {
            Assert.True(undo.IsApplied);
            Assert.True(redo.IsApplied);
            Assert.Equal(new HistoryStatus(transitions, true, false), session.CaptureState().HistoryStatus);
        }
        Assert.Equal(ScopeBId, session.CaptureState().ActiveScopeId);
        Assert.Same(original, document.CaptureSnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidAndPreCancelledInspectionNavigationDoNotInvalidatePresentation(bool readOnly)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = CreateScopedDocument(inputs, Scope(ScopeAId, Root(inputs)));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var config = EditingSessionTestHarness.Configuration();
        if (readOnly) config = config.WithAccessMode(EditingSessionAccessMode.ReadOnly);
        var attached = await EditingSession.AttachAsync(document, renderer, config, pipeline);
        await using var session = attached.Session!;
        var before = session.CaptureState();
        var invalid = await session.NavigateToScopeAsync(new("test:missing"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await session.NavigateToScopeAsync(ScopeAId, cancellation.Token);
        Assert.Equal(EditingSessionOperationStatus.Rejected, invalid.Status);
        Assert.Equal(EditingSessionOperationStatus.Cancelled, cancelled.Status);
        Assert.Same(before.CurrentScene, session.CaptureState().CurrentScene);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(before.Generation, session.CaptureState().Generation);
        Assert.Equal(1, pipeline.FullRunCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task InterruptedInspectionNavigationCannotPublishOrRecordHistory(bool readOnly, bool close)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = CreateScopedDocument(inputs, Scope(ScopeAId, Root(inputs)));
        var snapshot = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueScopedFull(async (current, scope, editor, token) =>
        {
            entered.SetResult();
            await resume.Task.WaitAsync(token);
            return SuccessfulEmptyScope(current, scope, editor);
        });
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var config = EditingSessionTestHarness.Configuration();
        if (readOnly) config = config.WithAccessMode(EditingSessionAccessMode.ReadOnly);
        var attached = await EditingSession.AttachAsync(document, renderer, config, pipeline);
        await using var session = attached.Session!;
        await using var controller = new Canvas2DInteractionController(session);
        using var cancellation = new CancellationTokenSource();
        var navigation = session.NavigateToScopeAsync(ScopeAId, cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(session.CaptureState().IsGraphicalInteractionEnabled);
        Assert.False((await controller.PointerActivatedAsync(new(30, 35))).Succeeded);
        if (close) await session.CloseAsync();
        else cancellation.Cancel();
        var result = await navigation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.Succeeded);
        Assert.Null(session.CaptureState().CurrentScene);
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Same(snapshot, document.CaptureSnapshot());
        if (!close)
        {
            Assert.Equal(EditingSessionOperationStatus.Cancelled, result.Status);
            Assert.Equal(EditingSessionStatus.RuntimeFaulted, session.CaptureState().Status);
            Assert.False((await session.NavigateToScopeAsync(Root(inputs))).Succeeded);
            Assert.False((await controller.PointerMovedAsync(new PointD(30, 35))).Succeeded);
        }
    }
}
