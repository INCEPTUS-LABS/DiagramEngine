using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Interaction;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Semantics;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.IntegrationTests;

public sealed class EditingSessionInspectionIntegrationTests
{
    private static readonly DocumentScopeId Peer = new("test:inspection:peer");
    private static readonly DocumentScopeId Nested = new("test:inspection:nested");
    private static readonly SemanticElementId NestedOwner = new("test:inspection:nested-owner");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealBpmnRootPeerAndNestedInspectionPreserveNativeDocument(bool readOnly)
    {
        var (composition, renderer) = await CreateAsync();
        await using var renderOwner = renderer;
        var document = composition.Document;
        var config = composition.Configuration;
        if (readOnly) config = config.WithAccessMode(EditingSessionAccessMode.ReadOnly);
        var attachment = await EditingSession.AttachAsync(document, renderer, config);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        await using var session = attachment.Session!;
        var bytes = NativeDocumentSerializer.Export(document);
        var snapshot = document.CaptureSnapshot();
        var root = snapshot.SemanticModel.RootScopeId;
        var scopeNavigation = Assert.Single(BpmnPluginRegistration.N100.ScopeNavigationRegistrations);
        Assert.True(scopeNavigation.Contribution.TryResolveTargetScope(snapshot,
            snapshot.SemanticModel.Elements.Single(item => item.Id == NestedOwner), out var resolved));
        Assert.Equal(Nested, resolved);
        var expectedEntries = 0;
        foreach (var scope in new[] { BpmnDemoPipeline.ProcessOrderScopeId, Nested, Peer, root, Nested })
        {
            Assert.True((await session.NavigateToScopeAsync(scope)).Succeeded);
            expectedEntries++;
            var before = session.CaptureState();
            Assert.Equal(scope, before.ActiveScopeId);
            Assert.Equal(readOnly ? 0 : expectedEntries, before.HistoryStatus.EntryCount);
            Assert.Empty(before.EditorState.Selection);
            Assert.True((await session.UpdateViewportAsync(new(0.75, new(-25, -30)))).Succeeded);
            Assert.True((await session.PanViewportAsync(new(-10, -10))).Succeeded);
            Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
            Assert.True((await session.NavigateToScopeAsync(scope)).Succeeded);
            Assert.Same(snapshot, document.CaptureSnapshot());
            Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(document).AsSpan()));
        }
        var undo = await session.UndoAsync();
        if (readOnly) Assert.Contains(undo.Diagnostics, item => item.Code == EditingSessionDiagnosticCodes.ReadOnly);
        else
        {
            Assert.True(undo.IsApplied);
            Assert.Equal(root, session.CaptureState().ActiveScopeId);
            Assert.True((await session.RedoAsync()).IsApplied);
            Assert.Equal(Nested, session.CaptureState().ActiveScopeId);
        }
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(document).AsSpan()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealBpmnTaskDraggingCommitsOnlyForTheDefaultEditor(bool readOnly)
    {
        var (composition, renderer) = await CreateAsync();
        await using var renderOwner = renderer;
        var document = composition.Document;
        var config = composition.Configuration;
        if (readOnly) config = config.WithAccessMode(EditingSessionAccessMode.ReadOnly);
        var attachment = await EditingSession.AttachAsync(document, renderer, config);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        await using var session = attachment.Session!;
        await using var controller = Controller(session, composition);
        var original = document.CaptureSnapshot();
        var bytes = NativeDocumentSerializer.Export(document);
        var initial = session.CaptureState();
        var tool = await session.SelectConnectionToolAsync(
            composition.AnchorConnectionCreationCatalog.Registrations[0].CreationId,
            composition.AnchorConnectionCreationCatalog);
        if (readOnly) Assert.Contains(tool.Diagnostics, item => item.Code == EditingSessionDiagnosticCodes.ReadOnly);
        else Assert.True(tool.Succeeded);
        Assert.True((await session.SelectConnectionToolAsync(null, composition.AnchorConnectionCreationCatalog)).Succeeded);
        var body = initial.CurrentScene!.Items.Single(item => Canvas2DNodeBodyMetadata.IsNodeBody(item) &&
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        var center = new PointD(body.Bounds.X + body.Bounds.Width / 2, body.Bounds.Y + body.Bounds.Height / 2);
        var point = initial.CurrentScene.ViewportTransform.TransformPoint(center);
        Assert.True((await controller.PointerActivatedAsync(point)).Succeeded);
        Assert.True((await controller.PointerMovedAsync(point)).Succeeded);
        var selected = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.TaskVisualId, Assert.Single(selected.EditorState.Selection));
        if (readOnly) AssertNoEditHandles(selected.CurrentScene!);
        await controller.PointerPressedAsync(new(1, point, buttons: 1));
        await controller.PointerMovedAsync(new Canvas2DPointerInput(1, new(point.X + 30, point.Y + 25), buttons: 1));
        Assert.Equal(!readOnly, session.CaptureState().EditorState.ActiveGesture is not null);
        var released = await controller.PointerReleasedAsync(new(1, new(point.X + 30, point.Y + 25)));
        await session.WaitForIdleAsync();
        if (readOnly)
        {
            Assert.Null(released.PersistentOperation);
            Assert.Same(original, document.CaptureSnapshot());
            Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(document).AsSpan()));
            Assert.Equal(initial.HistoryStatus, session.CaptureState().HistoryStatus);
        }
        else
        {
            Assert.True(released.PersistentOperation?.IsCommitted);
            Assert.Equal(initial.DocumentRevision.Value + 1, document.Revision.Value);
            Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        }
    }

    [Fact]
    public async Task InspectionCannotAcquireNativeHandlesLabelsOrConnectionsFromRealEditingScenes()
    {
        var (composition, renderer) = await CreateAsync();
        await using var renderOwner = renderer;
        var attachment = await EditingSession.AttachAsync(composition.Document, renderer, composition.Configuration);
        var targets = new List<PointD>();
        var editKeys = new HashSet<string>(StringComparer.Ordinal);
        await using (var editor = attachment.Session!)
        {
            var initial = editor.CaptureState();
            // Collect genuine visible edit targets from the ordinary Scene, including
            // Task labels, anchors, resize zones and connector endpoints.
            foreach (var visual in initial.ProjectedGraph!.Nodes.Select(node => node.Source.VisualStateId!)
                         .Concat(initial.ProjectedGraph.Edges.Select(edge => edge.Source.VisualStateId!)))
            {
                Assert.True((await editor.UpdateEditorStateAsync(new(selection: [visual]))).Succeeded);
                var scene = editor.CaptureState().CurrentScene!;
                foreach (var item in scene.Items.Where(item => item.IsVisible &&
                             item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
                             (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0))
                    if (item.Origin.StableSourceKey is { } key) editKeys.Add(key);
                targets.AddRange(scene.Items.Where(item => item.IsVisible && item.Origin.VisualStateId == visual &&
                        item.HitTestPolicy.Mode != Canvas2DHitTestMode.None)
                    .Select(item => scene.ViewportTransform.TransformPoint(
                        new(item.Bounds.X + item.Bounds.Width / 2, item.Bounds.Y + item.Bounds.Height / 2))));
            }
        }
        Assert.True(targets.Count > 20);
        Assert.Contains(editKeys, key => key.StartsWith("resize-handle:", StringComparison.Ordinal));
        Assert.Contains(editKeys, key => key.StartsWith("connector-anchor-handle:", StringComparison.Ordinal));
        Assert.Contains(editKeys, key => key.StartsWith("connector-endpoint-handle:", StringComparison.Ordinal));
        // A fresh attachment exercises the same immutable document through the internal
        // verification boundary; no public modeler mode or test-only host is introduced.
        await using var inspectionRenderer = await CreateRendererAsync();
        var second = await EditingSession.AttachAsync(composition.Document, inspectionRenderer,
            composition.Configuration.WithAccessMode(EditingSessionAccessMode.ReadOnly));
        Assert.Equal(EditingSessionAttachStatus.Ready, second.Status);
        await using var session = second.Session!;
        await using var controller = Controller(session, composition);
        var before = composition.Document.CaptureSnapshot();
        var bytes = NativeDocumentSerializer.Export(composition.Document);
        foreach (var point in targets.Distinct())
        {
            await controller.PointerActivatedAsync(point);
            var hover = await controller.PointerMovedAsync(point);
            Assert.True(hover.CssCursor is "pointer" or "default");
            AssertNoEditHandles(session.CaptureState().CurrentScene!);
            await controller.PointerPressedAsync(new(1, point, buttons: 1));
            await controller.PointerMovedAsync(new Canvas2DPointerInput(1, new(point.X + 20, point.Y + 15), buttons: 1));
            Assert.Null(session.CaptureState().EditorState.ActiveGesture);
            var release = await controller.PointerReleasedAsync(new(1, new(point.X + 20, point.Y + 15)));
            Assert.Null(release.PersistentOperation);
            await controller.CancelActiveGestureAsync();
            Assert.Same(before, composition.Document.CaptureSnapshot());
            Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
        }
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(composition.Document).AsSpan()));
    }

    [Fact]
    public void AmbiguousSubProcessOwnerDoesNotChooseAnArbitraryNavigationTarget()
    {
        var id = new DocumentId("test:ambiguous-navigation");
        var root = new DocumentScopeId(id.Value);
        var owner = BpmnSemanticFactory.CreateSubProcess(NestedOwner, "NEST", "Nested");
        var snapshot = new DocumentSnapshot(new SemanticModelSnapshot(id, new(0), [owner],
            nestedScopes: [new(Nested, root, NestedOwner), new(Peer, root, NestedOwner)]),
            new VisualModelSnapshot(id, new(0)), new(id, new(0)));
        var contribution = Assert.Single(BpmnPluginRegistration.N100.ScopeNavigationRegistrations).Contribution;
        Assert.False(contribution.TryResolveTargetScope(snapshot, owner, out var target));
        Assert.Null(target);
    }

    private static Canvas2DInteractionController Controller(EditingSession session, DocumentCanvasComposition composition) =>
        new(session, connectionCreationCatalog: composition.AnchorConnectionCreationCatalog,
            creationIdentityProvider: composition.DocumentCreationIdentityProvider,
            endpointReconnectionCatalog: composition.EndpointReconnectionCatalog,
            spatialEditPlanners: composition.SpatialEditPlanners);

    private static void AssertNoEditHandles(Canvas2DScene scene) =>
        Assert.DoesNotContain(scene.Items, item =>
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static async Task<(DocumentCanvasComposition Composition, Canvas2DRenderer Renderer)> CreateAsync()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var original = composition.Document.CaptureSnapshot();
        var semantic = original.SemanticModel;
        var candidate = new DocumentSnapshot(new SemanticModelSnapshot(original.DocumentId, original.Revision,
                semantic.Elements.Append(BpmnSemanticFactory.CreateSubProcess(NestedOwner, "NEST", "Nested")),
                semantic.Relationships, semantic.NestedScopes.Concat([new(Peer), new(Nested, BpmnDemoPipeline.ProcessOrderScopeId, NestedOwner)]),
                semantic.ScopeMemberships.Append(new(NestedOwner, BpmnDemoPipeline.ProcessOrderScopeId)),
                semantic.ModelProfiles, semantic.ProfileAssignments),
            new VisualModelSnapshot(original.DocumentId, original.Revision,
                original.VisualModel.VisualStates.Append(new(new("test:inspection:nested-visual"), NestedOwner,
                    new(400, 400), new(120, 80), VisualPlacementMode.Pinned)), original.VisualModel.ProfileElementPresentations),
            original.Metadata, original.Publication);
        var reconstructed = DocumentReconstructor.Reconstruct(candidate, composition.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(reconstructed.Succeeded);
        var renderer = await CreateRendererAsync();
        var document = await BpmnModelerTestComposition.PrepareFreshDocumentAsync(
            reconstructed.Document!, composition.Configuration, renderer);
        return (BpmnModelerTestComposition.WithDocument(composition, document), renderer);
    }

    private static async Task<Canvas2DRenderer> CreateRendererAsync()
    {
        var renderer = new Canvas2DRenderer(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
            new Canvas2DRendererConfiguration(fontResources:
                [new("org.dejavu.DejaVuSans", "2.37", "DejaVu Sans", "fonts/DejaVuSans-2.37.ttf")],
                defaultFontFamily: "DejaVu Sans"));
        Assert.True((await renderer.InitializeAsync("inspection", new(1400, 900, 1))).Succeeded);
        return renderer;
    }
}
