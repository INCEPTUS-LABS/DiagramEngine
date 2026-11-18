using System.Collections.Immutable;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Bpmn.Commands;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Organizational.Profiles;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplicationToolbarActualNodeBodyAnchorKeepsLabelHitDistinct(bool label)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 0);
        var session = Session(fixture.Host);
        var scene = session.CaptureState().CurrentScene!;
        var hit = scene.Items.Single(item => item.Origin.VisualStateId?.Value == "v-source" &&
            item.Layer == (label ? Canvas2DSceneLayer.Label : Canvas2DSceneLayer.Content) && item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        await PointerObserver(fixture.Host).ContextMenuDocumentPointAsync(scene,
            label ? Center(hit.Bounds) : hit.Bounds.TopLeft + new VectorD(12, 12));
        await ApplicationMenuAsync(fixture.Host);
        fixture.Host.CloseContextMenu();
        var toolbar = await ApplicationToolbarAsync(fixture.Host);
        scene = session.CaptureState().CurrentScene!;
        var body = scene.Items.Single(item => item.Origin.VisualStateId?.Value == "v-source" && item.Layer == Canvas2DSceneLayer.Content && item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        Assert.Equal(hit.Id, toolbar.Context.Part!.SceneObjectId);
        Assert.Equal(new SemanticElementId("source"), toolbar.Context.Target.SemanticId);
        Assert.Equal(ApplicationScreenBounds(scene, body.Bounds), toolbar.Placement.Anchor);
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ApplicationToolbarActualRelationshipAnchorUsesFinalRouteForPathAndLabel(bool message, bool label)
    {
        BpmnModelerCompositionFactory factory;
        VisualStateId visual;
        SemanticElementId owner;
        if (message)
        {
            await using var prepared = A22ConnectionHost(new ToolboxSelectionState());
            await prepared.InitializeAsync("application-toolbar-message-source", "container");
            var source = AttachedDocument(Session(prepared));
            owner = new("toolbar:message");
            visual = new("toolbar:message:visual");
            Assert.True((await Session(prepared).ExecuteAsync(new CreateBpmnMessageFlowCommand(source.DocumentId, source.Revision,
                owner, visual, A22ViewTaskId, A22ToolPeerTask, A22SourceAnchor(A22ViewTaskVisualId),
                new(A22ToolPeerVisual.Value + ":target")))).IsCommitted);
            Assert.True((await Session(prepared).ExecuteAsync(new UpdateBpmnMessageFlowNameCommand(source.DocumentId, source.Revision,
                owner, "Application message"))).IsCommitted);
            await Session(prepared).WaitForIdleAsync();
            factory = new(initialDocument: source.CaptureSnapshot(), accessMode: EditingSessionAccessMode.ReadOnly);
        }
        else
        {
            factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync()).WithAccessMode(EditingSessionAccessMode.ReadOnly);
            owner = new("demo:bpmn:sequence-flow:event-based-gateway-to-message");
            // Resolve the visual from the accepted document below, never from its display name.
            visual = new("pending");
        }
        await using var fixture = new ApplicationActionFixture(factory);
        await fixture.InitializeAsync();
        if (!message) visual = fixture.Facade.GetDocument().Snapshot!.VisualModel.VisualStates.Single(item => item.SemanticElementId == owner).Id;
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 0);
        var actualHit = await OpenApplicationPartAsync(fixture, visual, label);
        fixture.Host.CloseContextMenu();
        var session = Session(fixture.Host);
        var route = ApplicationCanonicalRoute(session.CaptureState().CurrentScene!, visual);
        var midpoint = ApplicationToolbarLayout.ArcMidpoint(route.Geometry.Points.Select(route.Transform.TransformPoint).ToImmutableArray());
        Assert.True((await session.UpdateViewportAsync(new ViewportSnapshot(0.5,
            new(Math.Min(0, 350 - midpoint.X * 0.5), Math.Min(0, 300 - midpoint.Y * 0.5))))).Succeeded);
        await session.WaitForIdleAsync();
        var toolbar = await ApplicationToolbarAsync(fixture.Host);
        var scene = session.CaptureState().CurrentScene!;
        route = ApplicationCanonicalRoute(scene, visual);
        midpoint = ApplicationToolbarLayout.ArcMidpoint(route.Geometry.Points.Select(route.Transform.TransformPoint).ToImmutableArray());
        var css = scene.ViewportTransform.TransformPoint(midpoint);
        Assert.Equal(new RectD(css.X - 4, css.Y - 4, 8, 8), toolbar.Placement.Anchor);
        Assert.Equal(owner, toolbar.Context.Target.SemanticId);
        Assert.Equal(BpmnSemanticTargetKind.Relationship, toolbar.Context.Target.Kind);
        Assert.Equal(actualHit.Part!.SceneObjectId, toolbar.Context.Part!.SceneObjectId);
        Assert.Equal(label ? BpmnPresentationPartKind.ConnectorLabel : BpmnPresentationPartKind.ConnectorPath, toolbar.Context.Part.Kind);
    }

    [Theory]
    [InlineData("sales")]
    [InlineData("warehouse")]
    [InlineData("supplier")]
    [InlineData("company")]
    public async Task ApplicationToolbarActualOrganizationalOwnerUsesItsHeaderOrParticipantBody(string semanticId)
    {
        await using var prepared = new FacadeTestFixture(new(initialDocument: A22PoolSnapshot()));
        await prepared.InitializeAsync();
        await using var fixture = new ApplicationActionFixture(new(initialDocument: prepared.Facade.GetDocument().Snapshot!, accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 0);
        var session = Session(fixture.Host);
        Assert.True((await session.UpdateEditorStateAsync(new(semanticSceneSelection: new(semanticId),
            viewport: new ViewportSnapshot(0.5, default)))).Succeeded);
        await session.WaitForIdleAsync();
        var toolbar = await ApplicationToolbarAsync(fixture.Host);
        var scene = session.CaptureState().CurrentScene!;
        var parts = scene.Items.Where(item => toolbar.Context.Target.Parts.Any(part => part.SceneObjectId == item.Id)).ToArray();
        var header = parts.SingleOrDefault(item => item.Origin.StableSourceKey == semanticId + ":pool-header");
        var anchor = header ?? parts.Single(item => item.Layer == Canvas2DSceneLayer.Content);
        Assert.Equal(ApplicationScreenBounds(scene, anchor.Bounds), toolbar.Placement.Anchor);
        Assert.Equal(new SemanticElementId(semanticId), toolbar.Context.Target.SemanticId);
        Assert.Null(toolbar.Context.Target.VisualStateId);
        Assert.Null(toolbar.Context.Part); // No pointer hit was invented for programmatic selection.
        if (header is not null)
        {
            Assert.True((await session.UpdateModelProfileViewStateAsync(session.CaptureState().ModelProfileViewState
                .WithPreferredVisibility(OrganizationalModelProfile.Id, false))).Succeeded);
            await session.WaitForIdleAsync();
            Assert.Null(fixture.Host.ApplicationToolbar);
        }
    }

    [Fact]
    public async Task ApplicationToolbarSuspendsAtUnmatchedPanFrameAndDprDoesNotRescaleCssControls()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new(1400, 900, 1));
        await using var host = CreateHost(execution, surface,
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await host.InitializeAsync("application-toolbar-dpr", "container");
        host.SetApplicationActions(ApplicationActions, true, false, true, 0);
        var session = Session(host);
        Assert.True((await session.UpdateEditorStateAsync(new(selection: [new VisualStateId("v-source")],
            viewport: session.CaptureState().EditorState.Viewport))).Succeeded);
        await session.WaitForIdleAsync();
        var initial = await ApplicationToolbarAsync(host);
        var selection = session.CaptureState().EditorState.Selection;
        await surface.RaiseAsync(new(1400, 900, 2));
        await session.WaitForIdleAsync();
        var dpr = await ApplicationToolbarAsync(host);
        Assert.Equal(initial.Placement.Anchor, dpr.Placement.Anchor);
        Assert.Equal(initial.Placement.Width, dpr.Placement.Width);
        execution.BlockRender = true;
        var pan = session.UpdateViewportAsync(new ViewportSnapshot(1.1, new(-5, -5))).AsTask();
        try
        {
            await execution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(host.ApplicationToolbar);
            Assert.True(selection.AsSpan().SequenceEqual(session.CaptureState().EditorState.Selection.AsSpan()));
        }
        finally { execution.ReleaseRender(); }
        Assert.True((await pan).Succeeded);
        await session.WaitForIdleAsync();
        var settled = await ApplicationToolbarAsync(host);
        Assert.NotEqual(initial.Placement.Anchor, settled.Placement.Anchor);
        Assert.Equal(initial.Placement.Width, settled.Placement.Width);
        Assert.True((await session.UpdateViewportAsync(new ViewportSnapshot(1, new(-10000, -10000)))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.Null(host.ApplicationToolbar);
        Assert.True(selection.AsSpan().SequenceEqual(session.CaptureState().EditorState.Selection.AsSpan()));
    }

    private static async Task<DocumentCanvasApplicationToolbarState> ApplicationToolbarAsync(DocumentCanvasHost host)
    {
        await WaitForApplicationConditionAsync(() => host.ApplicationToolbar is { IsDiscoveryPending: false, Actions.IsEmpty: false });
        return host.ApplicationToolbar!;
    }

    private static RectD ApplicationScreenBounds(Canvas2DScene scene, RectD bounds)
    {
        var top = scene.ViewportTransform.TransformPoint(bounds.TopLeft);
        var bottom = scene.ViewportTransform.TransformPoint(new(bounds.Right, bounds.Bottom));
        return new(top.X, top.Y, bottom.X - top.X, bottom.Y - top.Y);
    }

    private static Canvas2DSceneItem ApplicationCanonicalRoute(Canvas2DScene scene, VisualStateId visual) =>
        scene.Items.Single(item => item.Origin.VisualStateId == visual && item.Origin.ProjectedObjectId is { } projected &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(projected, "connector"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplicationToolbarUsesFinalTransformedPolylineIncludingDegenerateRoute(bool zeroLength)
    {
        var factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync()).WithAccessMode(EditingSessionAccessMode.ReadOnly);
        await using var fixture = new ApplicationActionFixture(factory);
        await fixture.InitializeAsync();
        var session = Session(fixture.Host);
        var owner = new SemanticElementId("demo:bpmn:sequence-flow:event-based-gateway-to-message");
        var visual = fixture.Facade.GetDocument().Snapshot!.VisualModel.VisualStates.Single(item => item.SemanticElementId == owner).Id;
        var originalRoute = ApplicationCanonicalRoute(session.CaptureState().CurrentScene!, visual);
        await SelectApplicationTargetAsync(fixture.Facade, originalRoute.Origin.VisualStateId!);
        var original = session.CaptureState().CurrentScene!;
        originalRoute = ApplicationCanonicalRoute(original, originalRoute.Origin.VisualStateId!);
        ImmutableArray<PointD> points = zeroLength ? [new(10, 20), new(10, 20)] :
            [new(0, 0), new(40, 0), new(45, -4), new(50, 0), new(100, 0), new(100, 80)];
        var transform = Matrix2D.CreateScale(1.5, 0.75).Then(Matrix2D.CreateTranslation(300, 200));
        var route = new Canvas2DSceneItem(originalRoute.ProcessPlacement, originalRoute.Id, originalRoute.Layer,
            originalRoute.ZIndex, Canvas2DSceneGeometry.Path(points), originalRoute.Origin, transform: transform,
            style: originalRoute.Style, hitTestPolicy: originalRoute.HitTestPolicy, spatialRegion: originalRoute.SpatialRegion);
        var presented = new Canvas2DScene(original.DocumentId, original.SourceRevision, original.LayoutAlgorithmId,
            original.RoutingAlgorithmId, original.Configuration, original.Contributors, original.Viewport, original.ViewportTransform,
            original.ActiveToolId, original.FocusTargetId, original.ToolState, original.ContributorMetadata,
            original.Items.Select(item => item.Id == route.Id ? route : item),
            presentationContext: original.PresentationContext, presentationPlan: original.PresentationPlan);
        // Canonical lower-layer fixture: install a final Scene, as existing presentation-instance tests do.
        SetSessionField(session, "_currentScene", presented);
        try
        {
            fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 0);
            var toolbar = await ApplicationToolbarAsync(fixture.Host);
            var midpoint = ApplicationToolbarLayout.ArcMidpoint(points.Select(transform.TransformPoint).ToImmutableArray());
            var expected = presented.ViewportTransform.TransformPoint(midpoint);
            Assert.Equal(new RectD(expected.X - 4, expected.Y - 4, 8, 8), toolbar.Placement.Anchor);
            Assert.Equal(originalRoute.Origin.SemanticElementId, toolbar.Context.Target.SemanticId);
            Assert.Equal(BpmnSemanticTargetKind.Relationship, toolbar.Context.Target.Kind);
        }
        finally { SetSessionField(session, "_currentScene", original); }
    }
}
