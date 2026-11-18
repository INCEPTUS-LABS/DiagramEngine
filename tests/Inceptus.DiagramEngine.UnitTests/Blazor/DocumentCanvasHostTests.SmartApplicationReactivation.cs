using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("disabled", BpmnModelerOperationStatus.Unavailable, "BPMN_APPLICATION_TOOLBAR_DISABLED")]
    [InlineData("no-provider", BpmnModelerOperationStatus.Unavailable, "BPMN_APPLICATION_ACTIONS_UNAVAILABLE")]
    [InlineData("no-authority", BpmnModelerOperationStatus.Unavailable, "BPMN_APPLICATION_ACTIONS_UNAVAILABLE")]
    [InlineData("no-selection", BpmnModelerOperationStatus.Unavailable, "BPMN_APPLICATION_TARGET_UNAVAILABLE")]
    [InlineData("native-stale", BpmnModelerOperationStatus.Rejected, "BPMN_APPLICATION_CONTEXT_STALE")]
    [InlineData("cancelled", BpmnModelerOperationStatus.Cancelled, null)]
    public async Task SmartActionsReactivationRejectsInapplicableContextWithoutNativeSideEffects(
        string scenario, BpmnModelerOperationStatus status, string? code)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        if (scenario != "no-selection") await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var expected = fixture.Facade.CapturePresentation().Stamp!;
        fixture.Host.SetApplicationActions(scenario == "no-provider" ? null : ApplicationActions,
            scenario != "no-authority", false, scenario != "disabled", 1);
        if (scenario == "native-stale")
        {
            var session = Session(fixture.Host);
            Assert.True((await session.UpdateEditorStateAsync(new(viewport: session.CaptureState().EditorState.Viewport))).Succeeded);
            await session.WaitForIdleAsync();
        }
        var document = fixture.Facade.GetDocument().Snapshot;
        var selection = fixture.Facade.CapturePresentation();
        var history = Session(fixture.Host).CaptureState().HistoryStatus;
        using var cancellation = new CancellationTokenSource();
        if (scenario == "cancelled") cancellation.Cancel();
        var result = await fixture.Host.RequestApplicationContextualActionsAsync(expected, 1, cancellation.Token);
        Assert.Equal(status, result.Status);
        if (code is null) Assert.Empty(result.Diagnostics);
        else Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
        Assert.Equal(selection.Stamp, result.Presentation.Stamp);
        Assert.Same(document, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(history, Session(fixture.Host).CaptureState().HistoryStatus);
        Assert.Empty(fixture.Invocations);
        if (scenario == "disabled")
        {
            Assert.True((await fixture.Host.OpenApplicationContextMenuFromSelectionAsync()).Succeeded);
            Assert.Single((await ApplicationMenuAsync(fixture.Host)).ApplicationActions);
        }
    }

    [Fact]
    public async Task SmartActionsReactivationRejectsMultipleOwnersWithoutChoosingPrimary()
    {
        var factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync()).WithAccessMode(EditingSessionAccessMode.ReadOnly);
        await using var fixture = new ApplicationActionFixture(factory);
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 0);
        var session = Session(fixture.Host);
        Assert.True((await session.UpdateEditorStateAsync(new(selection: [BpmnDemoPipeline.TaskVisualId, BpmnDemoPipeline.ApprovedTaskVisualId],
            viewport: session.CaptureState().EditorState.Viewport))).Succeeded);
        await session.WaitForIdleAsync();
        var stamp = fixture.Facade.CapturePresentation().Stamp!;
        var result = await fixture.Host.RequestApplicationContextualActionsAsync(stamp, 0);
        Assert.Equal(BpmnModelerOperationStatus.Rejected, result.Status);
        Assert.Equal("BPMN_APPLICATION_MULTIPLE_TARGETS", Assert.Single(result.Diagnostics).Code);
        Assert.Equal(2, result.Presentation.Selection.Length);
        Assert.Equal(stamp, result.Presentation.Stamp);
        Assert.Null(fixture.Host.ApplicationToolbar);
        Assert.Empty(fixture.Invocations);
    }

    [Fact]
    public async Task SmartActionsReactivationRejectsAmbiguousPresentationWithoutInventingHit()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var session = Session(fixture.Host);
        await session.UpdateEditorStateAsync(new(selection: [new VisualStateId("v-source")], viewport: session.CaptureState().EditorState.Viewport));
        await session.WaitForIdleAsync();
        var original = session.CaptureState().CurrentScene!;
        var body = original.Items.First(item => item.Layer == Canvas2DSceneLayer.Content && item.Origin.VisualStateId?.Value == "v-source");
        var other = new Canvas2DSceneItem(new Inceptus.DiagramEngine.Contracts.Presentation.PresentationScopePlacement(
            session.CaptureState().ActiveScopeId, default, new("smart-other-boundary")), new("smart:other-instance"),
            body.Layer, body.ZIndex, body.Geometry, body.Origin, hitTestPolicy: body.HitTestPolicy);
        using var ambiguous = new Canvas2DScene(original.DocumentId, original.SourceRevision, original.LayoutAlgorithmId,
            original.RoutingAlgorithmId, original.Configuration, original.Contributors, original.Viewport, original.ViewportTransform,
            original.ActiveToolId, original.FocusTargetId, original.ToolState, original.ContributorMetadata, original.Items.Append(other),
            presentationContext: original.PresentationContext, presentationPlan: original.PresentationPlan);
        SetSessionField(session, "_currentScene", ambiguous);
        try
        {
            fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 0);
            var presentation = fixture.Facade.CapturePresentation();
            Assert.Null(presentation.PrimaryPart);
            var result = await fixture.Host.RequestApplicationContextualActionsAsync(presentation.Stamp!, 0);
            Assert.Equal(BpmnModelerOperationStatus.Rejected, result.Status);
            Assert.Equal("BPMN_APPLICATION_TARGET_AMBIGUOUS", Assert.Single(result.Diagnostics).Code);
            Assert.Null(fixture.Host.ApplicationToolbar);
            Assert.Empty(fixture.Invocations);
        }
        finally { SetSessionField(session, "_currentScene", original); }
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartActionsDismissalSurvivesSameOwnerPartPanAndOffscreenReturn(EditingSessionAccessMode mode)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 1);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var before = await SmartToolbarAsync(fixture.Host);
        fixture.Host.DismissApplicationContextualActions();
        var session = Session(fixture.Host);
        var scene = session.CaptureState().CurrentScene!;
        var label = scene.Items.Single(item => item.Origin.VisualStateId?.Value == "v-source" && item.Layer == Canvas2DSceneLayer.Label &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        await PointerObserver(fixture.Host).ClickDocumentPointAsync(scene, Center(label.Bounds));
        Assert.Null(fixture.Host.ApplicationToolbar);
        Assert.Equal(before.Context.Target.SemanticId, Assert.Single(fixture.Facade.CapturePresentation().Selection).SemanticId);
        Assert.Equal(label.Id, fixture.Facade.CapturePresentation().PrimaryPart!.SceneObjectId);
        var editor = session.CaptureState().EditorState;
        Assert.True((await session.UpdateEditorStateAsync(new(selection: editor.Selection,
            viewport: new ViewportSnapshot(editor.Viewport.Zoom, new(-10000, -10000))))).Succeeded);
        await session.WaitForIdleAsync();
        var unavailable = await fixture.Host.RequestApplicationContextualActionsAsync(fixture.Facade.CapturePresentation().Stamp!, 1);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable, unavailable.Status);
        Assert.Equal("BPMN_APPLICATION_TOOLBAR_SUSPENDED", Assert.Single(unavailable.Diagnostics).Code);
        Assert.True((await session.UpdateEditorStateAsync(new(selection: editor.Selection, viewport: editor.Viewport))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.Null(fixture.Host.ApplicationToolbar);
        Assert.True((await fixture.Host.RequestApplicationContextualActionsAsync(fixture.Facade.CapturePresentation().Stamp!, 1)).Succeeded);
        Assert.Equal(before.Context.Target.SemanticId, (await SmartToolbarAsync(fixture.Host)).Context.Target.SemanticId);
    }
}
