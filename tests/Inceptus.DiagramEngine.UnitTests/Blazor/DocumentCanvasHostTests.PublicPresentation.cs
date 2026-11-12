using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Canvas2D.Rendering.Interop;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task Stage3PublicCompositionPreservesModeSnapshotAndNavigationSemantics(EditingSessionAccessMode mode)
    {
        var factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync()).WithAccessMode(mode);
        await using var fixture = new FacadeTestFixture(factory);
        await fixture.InitializeAsync();
        var session = Session(fixture.Host);
        var snapshot = fixture.Facade.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(snapshot);
        var initial = fixture.Facade.CapturePresentation();
        Assert.True(initial.IsAvailable);
        Assert.Equal(mode, session.CaptureState().AccessMode);
        Assert.Equal(snapshot.SemanticModel.RootScopeId, initial.ActiveScopeId);
        Assert.Contains(initial.AvailableScopes, scope => scope.Id == BpmnDemoPipeline.ProcessOrderScopeId);
        var navigate = await fixture.Facade.NavigateToScopeAsync(initial.Stamp!, BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.True(navigate.Succeeded, NewDiagramDiagnostics(navigate.Diagnostics));
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, navigate.Presentation.ActiveScopeId);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, navigate.Presentation.ScopePath[^1].Id);
        Assert.Equal(mode == EditingSessionAccessMode.ReadOnly ? 0 : 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Same(snapshot, fixture.Facade.GetDocument().Snapshot);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(fixture.Facade.GetDocument().Snapshot!).AsSpan()));

        var replace = await fixture.Facade.SetDocumentAsync(snapshot);
        Assert.True(replace.Succeeded, NewDiagramDiagnostics(replace.Diagnostics));
        Assert.NotSame(session, Session(fixture.Host));
        Assert.True(session.CaptureState().IsClosed);
        Assert.Equal(mode, Session(fixture.Host).CaptureState().AccessMode);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(replace.Snapshot!).AsSpan()));
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
        Assert.NotEqual(initial.Stamp!.AttachmentVersion, fixture.Facade.CapturePresentation().Stamp!.AttachmentVersion);
        Assert.Equal(BpmnModelerOperationStatus.Rejected,
            (await fixture.Facade.NavigateToScopeAsync(initial.Stamp, initial.ActiveScopeId!)).Status);
    }

    [Fact]
    public async Task Stage3ReadOnlyRejectsPublicationLegacySelectionAndRoutingRecoveryIncludingNoOps()
    {
        var source = A24Snapshot();
        var input = new DocumentSnapshot(source.SemanticModel, source.VisualModel, source.Metadata,
            new DocumentPublicationSnapshot("original", "Original"));
        await using var fixture = new FacadeTestFixture(new(initialDocument: input, accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var before = fixture.Facade.GetDocument().Snapshot!;
        var context = fixture.Facade.CaptureEditorContext()!;
        var history = context.Presentation.HistoryStatus;
        foreach (var publication in new[] { before.Publication!, new DocumentPublicationSnapshot("changed", "Changed") })
        {
            var denied = await fixture.Facade.UpdatePublicationAsync(context.AttachmentVersion, before.DocumentId, before.Revision, publication);
            Assert.Equal(BpmnModelerOperationStatus.Rejected, denied.Status);
            Assert.Contains(denied.Diagnostics, item => item.Code == "BPMN_MODELER_READ_ONLY");
        }
        Assert.Null(await fixture.Facade.SelectVisualAsync(before.DocumentId, before.Revision,
            context.Presentation.PresentationContext, new("v-source")));
        Assert.Contains(fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>(), failed =>
            failed.Diagnostics.Any(diagnostic => diagnostic.Code == "BPMN_PRESENTATION_STAMP_REQUIRED"));
        foreach (var verify in new[] { false, true })
        {
            var recovery = await fixture.Facade.SetDocumentAsync(before,
                new BpmnModelerDocumentLoadOptions { VerifySavedRouting = verify, ReprepareIncompatibleRouting = true });
            Assert.Equal(BpmnModelerOperationStatus.Rejected, recovery.Status);
            Assert.Contains(recovery.Diagnostics, item => item.Code == "BPMN_MODELER_READ_ONLY_ROUTING_RECOVERY");
        }
        Assert.Empty(fixture.Facade.CapturePresentation().Selection);
        Assert.Same(before, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(history, Session(fixture.Host).CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task Stage3SelectionIsOrderedTypedAndNotifiesIndependentlyOfViewChanges()
    {
        await using var prepared = new FacadeTestFixture(new(initialDocument: A23Snapshot([("source", "target")])));
        await prepared.InitializeAsync();
        await using var fixture = new FacadeTestFixture(new(initialDocument: prepared.Facade.GetDocument().Snapshot, accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var session = Session(fixture.Host);
        var before = fixture.Facade.GetDocument().Snapshot!;
        var initial = fixture.Facade.CapturePresentation();
        var views = new List<bool>();
        fixture.Facade.ViewChanged += () => views.Add(fixture.Facade.CaptureEditorContext()!.CanReplaceDocument);
        // Establish the existing ViewChanged attachment baseline before transient inspection.
        await fixture.Host.ZoomInAsync();
        views.Clear();
        var selected = await fixture.Facade.SelectVisualAsync(initial.Stamp!, new("v-source"));
        Assert.True(selected.Succeeded, NewDiagramDiagnostics(selected.Diagnostics));
        var target = Assert.Single(selected.Presentation.Selection);
        Assert.Equal(new VisualStateId("v-source"), target.VisualStateId);
        Assert.Equal(before.VisualModel.VisualStates.Single(item => item.Id == target.VisualStateId).SemanticElementId, target.SemanticId);
        Assert.Equal(BpmnSemanticTargetKind.Element, target.Kind);
        Assert.NotNull(selected.Presentation.PrimaryPart);
        Assert.Single(fixture.Notifications.OfType<BpmnSelectionChangedEventArgs>());
        // Existing ViewChanged reports readiness transitions during Scene rebuilding.
        // Dedicated selection notification must not add a view event at unchanged readiness.
        var previousReady = true;
        foreach (var ready in views)
        {
            Assert.NotEqual(previousReady, ready);
            previousReady = ready;
        }
        Assert.True(previousReady);
        var stamp = fixture.Facade.CapturePresentation().Stamp!;
        await fixture.Host.ZoomOutAsync();
        Assert.Equal(stamp, fixture.Facade.CapturePresentation().Stamp);
        Assert.Single(fixture.Notifications.OfType<BpmnSelectionChangedEventArgs>());
        Assert.Equal(BpmnModelerOperationStatus.Rejected,
            (await fixture.Facade.SelectVisualAsync(initial.Stamp!, new("v-source"))).Status);

        var connector = before.VisualModel.VisualStates.Single(item =>
            before.SemanticModel.TryGetRelationship(item.SemanticElementId, out _));
        var next = new EditorStateSnapshot(selection: [connector.Id, new("v-source")], viewport: session.CaptureState().EditorState.Viewport);
        Assert.True((await session.UpdateEditorStateAsync(next)).Succeeded);
        await session.WaitForIdleAsync();
        var multi = fixture.Facade.CapturePresentation();
        Assert.Equal([connector.Id, new VisualStateId("v-source")], multi.Selection.Select(item => item.VisualStateId));
        Assert.Equal(BpmnSemanticTargetKind.Relationship, multi.Selection[0].Kind);
        Assert.Equal(connector.SemanticElementId, multi.Selection[0].SemanticId);
        Assert.Null(multi.PrimaryPart);
        Assert.True((await session.UpdateEditorStateAsync(new(viewport: next.Viewport))).Succeeded);
        await session.WaitForIdleAsync();
        _ = fixture.Facade.CapturePresentation();
        Assert.True((await session.UpdateEditorStateAsync(next)).Succeeded);
        await session.WaitForIdleAsync();
        Assert.NotEqual(multi.Stamp, fixture.Facade.CapturePresentation().Stamp);
        Assert.Equal(BpmnModelerOperationStatus.Rejected,
            (await fixture.Facade.SelectVisualAsync(multi.Stamp!, new("v-source"))).Status);
        Assert.Same(before, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Null(fixture.Host.CaptureState().ElementToolbox);
    }

    [Fact]
    public async Task Stage3ContextAwayAndBackAndOtherInstanceNeverReviveAStamp()
    {
        var factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync()).WithAccessMode(EditingSessionAccessMode.ReadOnly);
        await using var first = new FacadeTestFixture(factory);
        await using var second = new FacadeTestFixture(factory);
        await Task.WhenAll(first.InitializeAsync().AsTask(), second.InitializeAsync().AsTask());
        var original = first.Facade.CapturePresentation();
        var other = second.Facade.CapturePresentation();
        Assert.NotEqual(original.Stamp!.InstanceId, other.Stamp!.InstanceId);
        Assert.Equal(BpmnModelerOperationStatus.Rejected,
            (await second.Facade.NavigateToScopeAsync(original.Stamp, other.ActiveScopeId!)).Status);
        Assert.True((await first.Facade.NavigateToScopeAsync(original.Stamp, BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        Assert.True((await first.Facade.NavigateToScopeAsync(first.Facade.CapturePresentation().Stamp!, original.ActiveScopeId!)).Succeeded);
        Assert.NotEqual(original.Stamp, first.Facade.CapturePresentation().Stamp);
        Assert.Equal(other.Stamp, second.Facade.CapturePresentation().Stamp);
        Assert.Equal(BpmnModelerOperationStatus.Rejected,
            (await first.Facade.NavigateToScopeAsync(original.Stamp, original.ActiveScopeId!)).Status);
        var current = first.Facade.CapturePresentation().Stamp!;
        Assert.True((await first.Facade.NavigateToScopeAsync(current, original.ActiveScopeId!)).Succeeded);
        Assert.Equal(current, first.Facade.CapturePresentation().Stamp);
        Assert.Equal(BpmnModelerOperationStatus.Rejected,
            (await first.Facade.NavigateToScopeAsync(current, new("missing-scope"))).Status);
        Assert.Equal(current, first.Facade.CapturePresentation().Stamp);
    }

    [Fact]
    public async Task Stage3FailedReplacementAndCancellationKeepThePreviousAttachmentUsable()
    {
        var failing = new RecordingRenderExecution { RenderResult = new Canvas2DInteropOperationResult { Succeeded = false, Code = "TEST_STAGE3" } };
        await using var fixture = new FacadeTestFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly),
            () => CreateRenderer(failing));
        await fixture.InitializeAsync();
        var snapshot = fixture.Facade.GetDocument().Snapshot!;
        var before = fixture.Facade.CapturePresentation();
        var session = Session(fixture.Host);
        Assert.Equal(BpmnModelerOperationStatus.Failed, (await fixture.Facade.SetDocumentAsync(snapshot)).Status);
        Assert.Equal(before.Stamp, fixture.Facade.CapturePresentation().Stamp);
        Assert.Same(session, Session(fixture.Host));
        Assert.Equal(1, failing.DisposeCount);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(BpmnModelerOperationStatus.Cancelled, (await fixture.Facade.SetDocumentAsync(snapshot, cancelled.Token)).Status);
        Assert.Equal(BpmnModelerOperationStatus.Cancelled,
            (await fixture.Facade.SelectVisualAsync(before.Stamp!, new("v-source"), cancelled.Token)).Status);
        Assert.Equal(before.Stamp, fixture.Facade.CapturePresentation().Stamp);
        Assert.True((await fixture.Facade.SelectVisualAsync(before.Stamp!, new("v-source"))).Succeeded);
        Assert.Same(snapshot, fixture.Facade.GetDocument().Snapshot);
        await fixture.DisposeAsync();
        Assert.False(fixture.Facade.CapturePresentation().IsAvailable);
        Assert.True(session.CaptureState().IsClosed);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable,
            (await fixture.Facade.NavigateToScopeAsync(before.Stamp!, before.ActiveScopeId!)).Status);
    }

    [Fact]
    public async Task Stage3SemanticOnlyPoolSelectionHasNoFabricatedVisualIdentity()
    {
        await using var editable = new FacadeTestFixture(await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync());
        await editable.InitializeAsync();
        var session = Session(editable.Host);
        await EnableOrganizationalProfileAsync(session);
        var pool = await AddOrganizationalPoolFromMenuAsync(editable.Host);
        var snapshot = editable.Facade.GetDocument().Snapshot!;
        await using var fixture = new FacadeTestFixture(new(initialDocument: snapshot, accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var readOnly = Session(fixture.Host);
        Assert.True((await readOnly.UpdateEditorStateAsync(new(semanticSceneSelection: pool,
            viewport: readOnly.CaptureState().EditorState.Viewport))).Succeeded);
        await readOnly.WaitForIdleAsync();
        var target = Assert.Single(fixture.Facade.CapturePresentation().Selection);
        Assert.Equal(pool, target.SemanticId);
        Assert.Equal(BpmnSemanticTargetKind.Element, target.Kind);
        Assert.Null(target.VisualStateId);
        Assert.NotEmpty(target.Parts);
        Assert.All(target.Parts, part => Assert.Equal(BpmnPresentationPartKind.SemanticRegion, part.Kind));
        Assert.Equal(snapshot, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(0, readOnly.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("clipped")]
    [InlineData("conflict")]
    [InlineData("decoration")]
    public void Stage3PresentationResolverFailsClosedForInvalidProvenance(string kind)
    {
        var snapshot = A24Snapshot();
        var visual = snapshot.VisualModel.VisualStates[0];
        var origin = new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.SemanticElement | Canvas2DSceneOriginCategory.VisualState,
            kind == "conflict" ? new("missing") : visual.SemanticElementId, visual.Id);
        var item = new Canvas2DSceneItem(new("test:part"), Canvas2DSceneLayer.Content, 0,
            Canvas2DSceneGeometry.Rectangle(new(0, 0, 100, 80)), origin,
            isVisible: kind != "hidden", clip: kind == "clipped" ? new(300, 300, 20, 20) : null,
            hitTestPolicy: kind == "decoration" ? Canvas2DHitTestPolicy.None : new(Canvas2DHitTestMode.Bounds));
        Assert.Null(BpmnPresentationSelection.ResolvePart(snapshot, item));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stage3ReplacementCancellationAndDisposalCancelQueuedPresentationWork(bool dispose)
    {
        var factory = new BpmnModelerCompositionFactory(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly);
        var active = new RecordingRenderExecution();
        var candidate = new RecordingRenderExecution();
        var pointers = new LoadingPointerObserverFactory();
        using var facade = new BpmnModelerFacade(factory, static _ => Task.CompletedTask);
        await using var host = new DocumentCanvasHost(factory, CreateRenderer(active),
            new RecordingSurfaceObserverFactory(new RecordingSurfaceObserver(new(900, 600, 1))), pointers,
            replacementRendererFactory: () => CreateRenderer(candidate));
        facade.Attach(host);
        await host.InitializeAsync("active", "standby", "container");
        var snapshot = facade.GetDocument().Snapshot!;
        var stamp = facade.CapturePresentation().Stamp!;
        var session = Session(host);
        using var cancellation = new CancellationTokenSource();
        var replacement = facade.SetDocumentAsync(snapshot, cancellation.Token).AsTask();
        await pointers.Candidate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var selection = facade.SelectVisualAsync(stamp, new("v-source"), cancellation.Token).AsTask();
        Assert.False(selection.IsCompleted);
        if (dispose) facade.Dispose(); else cancellation.Cancel();
        pointers.Candidate.Release.TrySetResult();
        Assert.Equal(BpmnModelerOperationStatus.Cancelled, (await replacement.WaitAsync(TimeSpan.FromSeconds(10))).Status);
        Assert.Equal(BpmnModelerOperationStatus.Cancelled, (await selection.WaitAsync(TimeSpan.FromSeconds(10))).Status);
        Assert.Same(session, Session(host));
        Assert.Same(snapshot, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(1, candidate.DisposeCount);
        Assert.Equal(1, pointers.Candidate.DisposeCount);
        if (!dispose)
        {
            Assert.Equal(stamp, facade.CapturePresentation().Stamp);
            Assert.True((await facade.SelectVisualAsync(stamp, new("v-source"))).Succeeded);
        }
        else Assert.False(facade.CapturePresentation().IsAvailable);
        await host.DisposeAsync();
        Assert.Equal(1, active.DisposeCount);
        Assert.True(session.CaptureState().IsClosed);
    }

    [Fact]
    public async Task Stage3PrimaryPartTracksTheActualHitAndIgnoresHover()
    {
        await using var fixture = new FacadeTestFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var session = Session(fixture.Host);
        var pointer = PointerObserver(fixture.Host);
        var scene = session.CaptureState().CurrentScene!;
        var body = scene.Items.Single(item => item.Origin.VisualStateId?.Value == "v-source" && item.Layer == Canvas2DSceneLayer.Content && item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        var point = new Inceptus.DiagramEngine.Contracts.Geometry.PointD(body.Bounds.Left + 12, body.Bounds.Top + 12);
        await pointer.ContextMenuDocumentPointAsync(scene, point);
        var selected = fixture.Facade.CapturePresentation();
        Assert.Equal(body.Id, selected.PrimaryPart!.SceneObjectId);
        Assert.Equal(BpmnPresentationPartKind.NodeBody, selected.PrimaryPart.Kind);
        Assert.Null(fixture.Host.CaptureState().ContextMenu);
        var count = fixture.Notifications.OfType<BpmnSelectionChangedEventArgs>().Count();
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, point);
        Assert.Equal(selected.Stamp, fixture.Facade.CapturePresentation().Stamp);
        Assert.Equal(count, fixture.Notifications.OfType<BpmnSelectionChangedEventArgs>().Count());
        var label = session.CaptureState().CurrentScene!.Items.First(item =>
            item.Origin.VisualStateId?.Value == "v-source" && item.Layer == Canvas2DSceneLayer.Label && item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        await pointer.ContextMenuDocumentPointAsync(session.CaptureState().CurrentScene!, Center(label.Bounds));
        var relabeled = fixture.Facade.CapturePresentation();
        Assert.Equal(label.Id, relabeled.PrimaryPart!.SceneObjectId);
        Assert.Equal(BpmnPresentationPartKind.NodeLabel, relabeled.PrimaryPart.Kind);
        Assert.NotEqual(selected.Stamp, relabeled.Stamp);
        Assert.Equal(count + 1, fixture.Notifications.OfType<BpmnSelectionChangedEventArgs>().Count());
    }

    [Fact]
    public async Task Stage3RequestQueuedDuringReplacementRevalidatesAfterTheAttachmentSwitch()
    {
        var factory = new BpmnModelerCompositionFactory(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly);
        var pointers = new LoadingPointerObserverFactory();
        using var facade = new BpmnModelerFacade(factory, static _ => Task.CompletedTask);
        await using var host = new DocumentCanvasHost(factory, CreateRenderer(new RecordingRenderExecution()),
            new RecordingSurfaceObserverFactory(new RecordingSurfaceObserver(new(900, 600, 1))), pointers,
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        facade.Attach(host);
        await host.InitializeAsync("active", "standby", "container");
        var before = facade.CapturePresentation();
        var snapshot = facade.GetDocument().Snapshot!;
        var replacement = facade.SetDocumentAsync(snapshot).AsTask();
        await pointers.Candidate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var selection = facade.SelectVisualAsync(before.Stamp!, new("v-source")).AsTask();
        Assert.False(selection.IsCompleted);
        pointers.Candidate.Release.TrySetResult();
        Assert.True((await replacement.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
        var rejected = await selection.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(BpmnModelerOperationStatus.Rejected, rejected.Status);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "BPMN_PRESENTATION_STALE");
        Assert.Empty(facade.CapturePresentation().Selection);
        Assert.NotEqual(before.Stamp, facade.CapturePresentation().Stamp);
        Assert.Equal(snapshot, facade.GetDocument().Snapshot);
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public void Stage3VisualSelectionCannotChooseBetweenProcessPresentationInstances()
    {
        var snapshot = A24Snapshot();
        var visual = snapshot.VisualModel.VisualStates[0];
        var origin = new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.SemanticElement | Canvas2DSceneOriginCategory.VisualState,
            visual.SemanticElementId, visual.Id);
        Canvas2DSceneItem Part(string id, string boundary) => new(
            new Inceptus.DiagramEngine.Contracts.Presentation.PresentationScopePlacement(snapshot.SemanticModel.RootScopeId, default, new(boundary)),
            new(id), Canvas2DSceneLayer.Content, 0, Canvas2DSceneGeometry.Rectangle(new(0, 0, 100, 80)), origin,
            hitTestPolicy: new(Canvas2DHitTestMode.Bounds));
        var first = Part("first", "boundary-a");
        var labelInFirst = Part("first-label", "boundary-a");
        var second = Part("second", "boundary-b");
        Assert.True(BpmnPresentationSelection.HasSinglePresentationInstance([first, labelInFirst]));
        Assert.False(BpmnPresentationSelection.HasSinglePresentationInstance([first, second]));
        Assert.False(BpmnPresentationSelection.HasSinglePresentationInstance([]));
    }
}
