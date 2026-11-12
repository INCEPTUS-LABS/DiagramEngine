using System.Collections.Concurrent;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Bpmn.Commands;
using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.HitTesting;
using Inceptus.DiagramEngine.Canvas2D.Interaction;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Organizational.Profiles;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    [InlineData(EditingSessionAccessMode.Editable)]
    public async Task Stage4ApplicationActionsPreserveNativeStateAndCoexistWithNativePolicy(EditingSessionAccessMode mode)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        var before = fixture.Facade.GetDocument().Snapshot!;
        var history = Session(fixture.Host).CaptureState().HistoryStatus;
        var bytes = NativeDocumentSerializer.Export(before);
        var supplied = new List<BpmnApplicationAction> { new("app:inspect", "Inspect application state") };
        fixture.Host.SetApplicationActions((_, _) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>(supplied), true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        Assert.True((await fixture.Host.OpenApplicationContextMenuFromSelectionAsync()).Succeeded);
        var menu = await ApplicationMenuAsync(fixture.Host);
        var request = Assert.IsType<BpmnApplicationActionContext>(menu.ApplicationContext);
        Assert.Equal(new SemanticElementId("source"), request.Target.SemanticId);
        Assert.Equal(BpmnSemanticTargetKind.Element, request.Target.Kind);
        Assert.Equal(new VisualStateId("v-source"), request.Target.VisualStateId);
        Assert.Equal(fixture.Facade.CapturePresentation().Stamp, request.Stamp);
        Assert.Equal(mode == EditingSessionAccessMode.Editable, menu.PropertiesAvailable);
        Assert.Equal(mode == EditingSessionAccessMode.Editable, menu.DeletionAction is not null);
        Assert.Single(menu.ApplicationActions);
        supplied.Clear();
        Assert.Single(menu.ApplicationActions); // Provider-owned collection is never retained.
        supplied.Add(new("app:inspect", "Inspect application state"));
        var invoked = await fixture.Host.ExecuteApplicationActionAsync(request.RequestVersion, "app:inspect");
        Assert.True(invoked.Succeeded, NewDiagramDiagnostics(invoked.Diagnostics));
        await WaitForApplicationConditionAsync(() => fixture.Invocations.Count == 1);
        var callback = Assert.Single(fixture.Invocations);
        Assert.Equal("app:inspect", callback.ActionId);
        Assert.Equal(request.Stamp, callback.Context.Stamp);
        Assert.Equal(request.Target.SemanticId, callback.Context.Target.SemanticId);
        Assert.False((await fixture.Host.ExecuteApplicationActionAsync(request.RequestVersion, "app:inspect")).Succeeded);
        Assert.Single(fixture.Invocations);
        Assert.Same(before, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(history, Session(fixture.Host).CaptureState().HistoryStatus);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(fixture.Facade.GetDocument().Snapshot!).AsSpan()));
        Assert.Empty(fixture.Notifications.OfType<BpmnModelerDocumentChangedEventArgs>());
    }

    [Theory]
    [InlineData("empty-id")]
    [InlineData("whitespace-id")]
    [InlineData("duplicate-id")]
    [InlineData("empty-label")]
    [InlineData("null-action")]
    public async Task Stage4InvalidApplicationDescriptorsRejectTheWholeResult(string invalid)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var before = fixture.Facade.GetDocument().Snapshot;
        fixture.Host.SetApplicationActions((_, _) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>(invalid switch
        {
            "empty-id" => [new("", "Invalid")],
            "whitespace-id" => [new(" ", "Invalid")],
            "duplicate-id" => [new("app:duplicate", "First"), new("app:duplicate", "Second")],
            "empty-label" => [new("app:invalid", "")],
            _ => [new("app:valid", "Valid"), null!],
        }), true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        await WaitForApplicationConditionAsync(() => fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>().Any());
        Assert.True(fixture.Host.CaptureState().ContextMenu is null or { ApplicationActions.IsEmpty: true });
        Assert.Empty(fixture.Invocations);
        Assert.Same(before, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
        Assert.All(fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>(), failure => Assert.NotEmpty(failure.Diagnostics));
    }

    [Fact]
    public async Task Stage4DisabledAndUnknownActionsCannotInvokeTheConsumer()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions((_, _) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>(
            [new("app:disabled", "Resume", false, "The run is already active.")]), true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var menu = await ApplicationMenuAsync(fixture.Host);
        var action = Assert.Single(menu.ApplicationActions);
        Assert.False(action.IsEnabled);
        Assert.Equal("The run is already active.", action.DisabledExplanation);
        foreach (var id in new[] { "app:disabled", "app:unknown" })
        {
            var result = await fixture.Host.ExecuteApplicationActionAsync(menu.ApplicationContext!.RequestVersion, id);
            Assert.Equal(BpmnModelerOperationStatus.Rejected, result.Status);
        }
        Assert.Empty(fixture.Invocations);
    }

    [Theory]
    [InlineData("BPMN.Task")]
    [InlineData("BPMN.StartEvent")]
    [InlineData("BPMN.EndEvent")]
    [InlineData("BPMN.ExclusiveGateway")]
    [InlineData("BPMN.ParallelGateway")]
    [InlineData("BPMN.InclusiveGateway")]
    [InlineData("BPMN.EventBasedGateway")]
    public async Task Stage4KeyboardActionsUseTheSelectedSemanticOwnerWithoutRequiringPointerCoordinates(string semanticType)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(new(semanticType)), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        var session = Session(fixture.Host);
        Assert.True((await session.UpdateEditorStateAsync(new(selection: [new VisualStateId("v-source")],
            viewport: session.CaptureState().EditorState.Viewport))).Succeeded);
        await session.WaitForIdleAsync();
        var selected = fixture.Facade.CapturePresentation();
        Assert.Null(selected.PrimaryPart);
        Assert.True((await fixture.Host.OpenApplicationContextMenuFromSelectionAsync()).Succeeded);
        var menu = await ApplicationMenuAsync(fixture.Host);
        var context = menu.ApplicationContext!;
        Assert.Equal(selected.Stamp, context.Stamp);
        Assert.Null(context.Part);
        Assert.Equal(new SemanticElementId("source"), context.Target.SemanticId);
        Assert.Equal(BpmnSemanticTargetKind.Element, context.Target.Kind);
        Assert.Contains(context.Target.Parts, part => part.Kind == BpmnPresentationPartKind.NodeBody);
        // An application toolbar can associate with the same semantic target and map its
        // existing part bounds through the already public immutable Scene transform.
        var body = Assert.Single(context.Target.Parts, part => part.Kind == BpmnPresentationPartKind.NodeBody);
        var editor = fixture.Facade.CaptureEditorContext()!;
        Assert.Equal(context.Stamp.AttachmentVersion, editor.AttachmentVersion);
        var cssTopLeft = editor.Presentation.CurrentScene!.ViewportTransform.TransformPoint(body.Bounds.TopLeft);
        Assert.True(double.IsFinite(cssTopLeft.X) && double.IsFinite(cssTopLeft.Y));
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect")).Succeeded);
        Assert.Equal(context.Target.SemanticId, Assert.Single(fixture.Invocations).Context.Target.SemanticId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stage4PointerActionsPreserveExactBodyOrLabelPart(bool label)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        var scene = Session(fixture.Host).CaptureState().CurrentScene!;
        var part = scene.Items.First(item => item.Origin.VisualStateId?.Value == "v-source" &&
            item.Layer == (label ? Canvas2DSceneLayer.Label : Canvas2DSceneLayer.Content) && item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        var point = label ? Center(part.Bounds) : part.Bounds.TopLeft + new VectorD(12, 12);
        await PointerObserver(fixture.Host).ContextMenuDocumentPointAsync(scene, point);
        var menu = await ApplicationMenuAsync(fixture.Host);
        var context = menu.ApplicationContext!;
        Assert.Equal(part.Id, context.Part!.SceneObjectId);
        Assert.Equal(part.Bounds, context.Part.Bounds);
        Assert.Equal(label ? BpmnPresentationPartKind.NodeLabel : BpmnPresentationPartKind.NodeBody, context.Part.Kind);
        Assert.Equal(new SemanticElementId("source"), context.Target.SemanticId);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect")).Succeeded);
        Assert.Equal(part.Id, Assert.Single(fixture.Invocations).Context.Part!.SceneObjectId);
    }

    [Theory]
    [InlineData("sales")]
    [InlineData("warehouse")]
    [InlineData("supplier")]
    [InlineData("company")]
    public async Task Stage4SemanticOnlyPoolLaneAndParticipantActionsKeepDistinctOwners(string semanticId)
    {
        await using var prepared = new FacadeTestFixture(new(initialDocument: A22PoolSnapshot()));
        await prepared.InitializeAsync();
        var snapshot = prepared.Facade.GetDocument().Snapshot!;
        await using var fixture = new ApplicationActionFixture(new(initialDocument: snapshot, accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        var session = Session(fixture.Host);
        Assert.True((await session.UpdateEditorStateAsync(new(semanticSceneSelection: new(semanticId),
            viewport: session.CaptureState().EditorState.Viewport))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.True((await fixture.Host.OpenApplicationContextMenuFromSelectionAsync()).Succeeded);
        var menu = await ApplicationMenuAsync(fixture.Host);
        var context = menu.ApplicationContext!;
        Assert.Equal(new SemanticElementId(semanticId), context.Target.SemanticId);
        Assert.Equal(BpmnSemanticTargetKind.Element, context.Target.Kind);
        Assert.Null(context.Target.VisualStateId);
        Assert.All(context.Target.Parts, part => Assert.Equal(BpmnPresentationPartKind.SemanticRegion, part.Kind));
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect")).Succeeded);
        Assert.Equal(new SemanticElementId(semanticId), Assert.Single(fixture.Invocations).Context.Target.SemanticId);
        Assert.Equal(snapshot, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData("dismiss")]
    [InlineData("reopen")]
    [InlineData("selection")]
    [InlineData("replacement")]
    [InlineData("navigation")]
    [InlineData("disposal")]
    public async Task Stage4LateProviderResultsCannotReviveRetiredApplicationRequests(string change)
    {
        var factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync()).WithAccessMode(EditingSessionAccessMode.ReadOnly);
        await using var fixture = new ApplicationActionFixture(factory);
        await fixture.InitializeAsync();
        var before = fixture.Facade.GetDocument().Snapshot!;
        var initialSession = Session(fixture.Host);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        fixture.Host.SetApplicationActions(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) != 1) return [new("app:current", "Current")];
            started.SetResult(token);
            await release.Task; // Deliberately uncooperative provider; late output still must be rejected.
            finished.SetResult();
            return [new("app:retired", "Retired")];
        }, true);
        await SelectApplicationTargetAsync(fixture.Facade, BpmnDemoPipeline.TaskVisualId);
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var request = fixture.Host.CaptureState().ContextMenu!.ApplicationContext!;
        try
        {
            switch (change)
            {
                case "dismiss": fixture.Host.CloseContextMenu(); break;
                case "reopen":
                    fixture.Host.CloseContextMenu();
                    await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
                    Assert.NotEqual(request.RequestVersion, (await ApplicationMenuAsync(fixture.Host)).ApplicationContext!.RequestVersion);
                    break;
                case "selection": await SelectApplicationTargetAsync(fixture.Facade, BpmnDemoPipeline.ApprovedTaskVisualId); break;
                case "replacement": Assert.True((await fixture.Facade.SetDocumentAsync(before)).Succeeded); break;
                case "navigation": Assert.True((await fixture.Facade.NavigateToScopeAsync(fixture.Facade.CapturePresentation().Stamp!, BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded); break;
                case "disposal": fixture.Facade.Dispose(); break;
            }
            Assert.True(token.IsCancellationRequested);
        }
        finally { release.TrySetResult(); }
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForApplicationConditionAsync(() => fixture.Host.CaptureState().ContextMenu?.IsApplicationDiscoveryPending != true);
        Assert.DoesNotContain(fixture.Host.CaptureState().ContextMenu?.ApplicationActions ?? [], item => item.Id == "app:retired");
        Assert.False((await fixture.Host.ExecuteApplicationActionAsync(request.RequestVersion, "app:retired")).Succeeded);
        Assert.Empty(fixture.Invocations);
        var current = change == "disposal" ? AttachedDocument(initialSession).CaptureSnapshot() : fixture.Facade.GetDocument().Snapshot!;
        Assert.Equal(before, current);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage4HoverRecompositionKeepsTheSameApplicationRequestUsable()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var menu = await ApplicationMenuAsync(fixture.Host);
        var request = menu.ApplicationContext!;
        await PointerObserver(fixture.Host).MoveDocumentPointAsync(Session(fixture.Host).CaptureState().CurrentScene!, new(800, 600));
        Assert.Equal(request.Stamp, fixture.Facade.CapturePresentation().Stamp);
        Assert.Equal(request.RequestVersion, fixture.Host.CaptureState().ContextMenu!.ApplicationContext!.RequestVersion);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(request.RequestVersion, "app:inspect")).Succeeded);
        Assert.Single(fixture.Invocations);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    [InlineData(EditingSessionAccessMode.Editable)]
    public async Task Stage4ApplicationInvocationWaitsForEquivalentPointerLeavePresentation(EditingSessionAccessMode mode)
    {
        var invocations = new ConcurrentQueue<BpmnApplicationActionInvocationEventArgs>();
        using var facade = new BpmnModelerFacade(new(initialDocument: A24Snapshot(), accessMode: mode), notification =>
        {
            if (notification is BpmnApplicationActionDispatch dispatch && dispatch.TryBeginInvocation(out var args))
            {
                invocations.Enqueue(args!);
                dispatch.Complete();
            }
            return Task.CompletedTask;
        });
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: facade.CompositionFactory);
        facade.Attach(host);
        await host.InitializeAsync("application-hover-canvas", "application-hover-container");
        var discoveryCount = 0;
        host.SetApplicationActions((_, _) =>
        {
            Interlocked.Increment(ref discoveryCount);
            return ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>([new("app:inspect", "Inspect")]);
        }, true);
        await SelectApplicationTargetAsync(facade, new("v-source"));
        var session = Session(host);
        var body = session.CaptureState().CurrentScene!.Items.First(item =>
            item.Origin.VisualStateId == new VisualStateId("v-source") && item.Layer == Canvas2DSceneLayer.Content);
        await PointerObserver(host).MoveDocumentPointAsync(session.CaptureState().CurrentScene!, Center(body.Bounds));
        Assert.NotNull(session.CaptureState().EditorState.HoveredObjectId);
        Assert.True((await host.OpenApplicationContextMenuFromSelectionAsync()).Succeeded);
        var request = (await ApplicationMenuAsync(host)).ApplicationContext!;
        var before = facade.GetDocument().Snapshot!;
        var history = session.CaptureState().HistoryStatus;
        var native = NativeDocumentSerializer.Export(before);
        execution.BlockRender = true;
        var leave = PointerObserver(host).LeaveAsync();
        Task<BpmnPresentationOperationResult>? invocation = null;
        try
        {
            await execution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(session.CaptureState().IsCurrentScenePresented);
            Assert.Null(session.CaptureState().EditorState.HoveredObjectId);
            Assert.Equal(request.RequestVersion, host.CaptureState().ContextMenu!.ApplicationContext!.RequestVersion);
            invocation = host.ExecuteApplicationActionAsync(request.RequestVersion, "app:inspect").AsTask();
            Assert.False(invocation.IsCompleted); // An equivalent in-flight Scene must not reject the click.
            Assert.Equal(1, Volatile.Read(ref discoveryCount));
            Assert.Empty(invocations);
        }
        finally
        {
            execution.ReleaseRender();
            await leave.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.NotNull(invocation);
        Assert.True((await invocation.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        var received = Assert.Single(invocations);
        Assert.Equal(request.RequestVersion, received.Context.RequestVersion);
        Assert.Equal(request.Stamp, received.Context.Stamp);
        Assert.Equal(request.Target.SemanticId, received.Context.Target.SemanticId);
        Assert.Equal(request.Part, received.Context.Part);
        Assert.Equal(2, Volatile.Read(ref discoveryCount));
        Assert.Same(before, facade.GetDocument().Snapshot);
        Assert.True(native.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(facade.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stage4SpatialNodeAndSemanticOnlyHeaderKeepTheirExactMenuTargetAcrossHover(bool semanticHeader)
    {
        await using var prepared = new FacadeTestFixture(new(initialDocument: A22PoolSnapshot()));
        await prepared.InitializeAsync();
        var snapshot = prepared.Facade.GetDocument().Snapshot!;
        await using var fixture = new ApplicationActionFixture(new(initialDocument: snapshot, accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        var session = Session(fixture.Host);
        var scene = session.CaptureState().CurrentScene!;
        var semanticId = new SemanticElementId(semanticHeader ? "sales" : "sales-task");
        var candidates = scene.Items.Where(item => item.Origin.SemanticElementId == semanticId &&
            BpmnPresentationSelection.ResolvePart(snapshot, item) is not null).SelectMany(item =>
                new[] { Center(item.Bounds), item.Bounds.TopLeft + new VectorD(4, 4), item.Bounds.TopLeft + new VectorD(12, 12) });
        var hitTest = new Canvas2DSceneHitTestService();
        var point = candidates.First(candidate => hitTest.HitTest(scene, candidate)?.Origin.SemanticElementId == semanticId);
        var hit = hitTest.HitTest(scene, point)!;
        var hitItem = scene.Items.Single(candidate => candidate.Id == hit.SceneObjectId);
        if (!semanticHeader) Assert.NotNull(hitItem.SpatialRegion);
        await PointerObserver(fixture.Host).ContextMenuDocumentPointAsync(scene, point);
        var menu = await ApplicationMenuAsync(fixture.Host);
        var context = menu.ApplicationContext!;
        Assert.Equal(semanticId, context.Target.SemanticId);
        Assert.Equal(hitItem.Id, context.Part!.SceneObjectId);
        Assert.Equal(hitItem.SpatialRegion?.Id, menu.TargetPresentation?.Id);
        if (semanticHeader) Assert.Null(context.Target.VisualStateId);
        var history = session.CaptureState().HistoryStatus;
        await PointerObserver(fixture.Host).MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new(1300, 850));
        var afterHover = Assert.IsType<DocumentCanvasContextMenuState>(fixture.Host.CaptureState().ContextMenu);
        Assert.Equal(context.RequestVersion, afterHover.ApplicationContext!.RequestVersion);
        Assert.Equal(context.Stamp, afterHover.ApplicationContext.Stamp);
        Assert.Equal(hitItem.SpatialRegion?.Id, afterHover.TargetPresentation?.Id);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect")).Succeeded);
        Assert.Equal(semanticId, Assert.Single(fixture.Invocations).Context.Target.SemanticId);
        Assert.Equal(snapshot, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task Stage4ProviderExceptionsAreBoundedAndDoNotExposeConsumerDetails()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions((_, _) => throw new InvalidOperationException("private-provider-details"), true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        await WaitForApplicationConditionAsync(() => fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>().Any());
        Assert.Empty(fixture.Invocations);
        Assert.All(fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>(), failure =>
            Assert.All(failure.Diagnostics, diagnostic => Assert.DoesNotContain("private-provider-details", diagnostic.Message, StringComparison.Ordinal)));
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage4InvocationRevalidatesApplicationEnablementImmediatelyBeforeCallback()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var enabled = true;
        fixture.Host.SetApplicationActions((_, _) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>(
            [new("app:inspect", "Inspect", enabled, enabled ? null : "No longer applicable")]), true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var menu = await ApplicationMenuAsync(fixture.Host);
        Assert.True(Assert.Single(menu.ApplicationActions).IsEnabled);
        enabled = false;
        Assert.Equal(BpmnModelerOperationStatus.Rejected,
            (await fixture.Host.ExecuteApplicationActionAsync(menu.ApplicationContext!.RequestVersion, "app:inspect")).Status);
        Assert.Empty(fixture.Invocations);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage4ConsumerCallbackFailureIsBoundedWithoutPersistentRollbackOrDetails()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var before = fixture.Facade.GetDocument().Snapshot;
        fixture.Callback = _ => throw new InvalidOperationException("private-callback-details");
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var menu = await ApplicationMenuAsync(fixture.Host);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(menu.ApplicationContext!.RequestVersion, "app:inspect")).Succeeded);
        await WaitForApplicationConditionAsync(() => fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>().Any());
        Assert.Single(fixture.Invocations);
        Assert.All(fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>(), failure =>
            Assert.All(failure.Diagnostics, diagnostic => Assert.DoesNotContain("private-callback-details", diagnostic.Message, StringComparison.Ordinal)));
        Assert.Same(before, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage4CallbackInvocationOrderDoesNotWaitForEarlierApplicationCompletion()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackOrder = new ConcurrentQueue<long>();
        fixture.Callback = async invocation =>
        {
            callbackOrder.Enqueue(invocation.Context.RequestVersion);
            if (callbackOrder.Count == 1) { await release.Task; firstCompleted.TrySetResult(); }
            else secondStarted.TrySetResult();
        };
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var first = (await ApplicationMenuAsync(fixture.Host)).ApplicationContext!;
        try
        {
            Assert.True((await fixture.Host.ExecuteApplicationActionAsync(first.RequestVersion, "app:inspect")).Succeeded);
            Assert.False(firstCompleted.Task.IsCompleted);
            await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
            var second = (await ApplicationMenuAsync(fixture.Host)).ApplicationContext!;
            Assert.True((await fixture.Host.ExecuteApplicationActionAsync(second.RequestVersion, "app:inspect")).Succeeded);
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal([first.RequestVersion, second.RequestVersion], callbackOrder.ToArray());
            Assert.False(firstCompleted.Task.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await firstCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage4DismissalAfterDispatchWasQueuedStillPreventsConsumerEntry()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var menu = await ApplicationMenuAsync(fixture.Host);
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeDispatch = async () => { queued.TrySetResult(); await release.Task; };
        var invocation = fixture.Host.ExecuteApplicationActionAsync(menu.ApplicationContext!.RequestVersion, "app:inspect").AsTask();
        try
        {
            await queued.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(fixture.Invocations);
            fixture.Host.CloseContextMenu();
        }
        finally { release.TrySetResult(); }
        await invocation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(fixture.Invocations);
        Assert.Null(fixture.Host.CaptureState().ContextMenu);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage4ProviderAndInvocationCanReenterPublicOperationsWithoutDeadlock()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var before = fixture.Facade.GetDocument().Snapshot!;
        var callbackFinished = new TaskCompletionSource<BpmnModelerDocumentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Callback = async invocation =>
        {
            Assert.False(invocation.CancellationToken.IsCancellationRequested);
            callbackFinished.SetResult(await fixture.Facade.SetDocumentAsync(before));
        };
        fixture.Host.SetApplicationActions(async (_, token) =>
        {
            var capture = await fixture.Facade.GetDocumentAsync(token);
            Assert.Same(before, capture.Snapshot);
            return [new("app:replace", "Replace from application")];
        }, true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var menu = await ApplicationMenuAsync(fixture.Host);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(menu.ApplicationContext!.RequestVersion, "app:replace")
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
        Assert.True((await callbackFinished.Task.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
        Assert.NotEqual(menu.ApplicationContext.Stamp.AttachmentVersion, fixture.Facade.CapturePresentation().Stamp!.AttachmentVersion);
        Assert.Equal(before, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage4DifferentComponentsKeepActionRequestsAndCallbacksIndependent()
    {
        var factory = new BpmnModelerCompositionFactory(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly);
        await using var first = new ApplicationActionFixture(factory);
        await using var second = new ApplicationActionFixture(factory);
        await first.InitializeAsync();
        await second.InitializeAsync();
        first.Host.SetApplicationActions(ApplicationActions, true);
        second.Host.SetApplicationActions((_, _) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>([new("app:second", "Second")]), true);
        await SelectApplicationTargetAsync(first.Facade, new("v-source"));
        await SelectApplicationTargetAsync(second.Facade, new("v-source"));
        await first.Host.OpenApplicationContextMenuFromSelectionAsync();
        await second.Host.OpenApplicationContextMenuFromSelectionAsync();
        var firstMenu = await ApplicationMenuAsync(first.Host);
        var secondMenu = await ApplicationMenuAsync(second.Host);
        Assert.NotEqual(firstMenu.ApplicationContext!.Stamp.InstanceId, secondMenu.ApplicationContext!.Stamp.InstanceId);
        first.Host.CloseContextMenu();
        Assert.Equal(secondMenu.ApplicationContext.RequestVersion, second.Host.CaptureState().ContextMenu!.ApplicationContext!.RequestVersion);
        Assert.True((await second.Host.ExecuteApplicationActionAsync(secondMenu.ApplicationContext.RequestVersion, "app:second")).Succeeded);
        Assert.Empty(first.Invocations);
        Assert.Equal("app:second", Assert.Single(second.Invocations).ActionId);
        Assert.Equal(0, Session(first.Host).CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(0, Session(second.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stage4SequenceFlowPathAndLabelActionsRetainRelationshipIdentity(bool label)
    {
        var factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync()).WithAccessMode(EditingSessionAccessMode.ReadOnly);
        await using var fixture = new ApplicationActionFixture(factory);
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        var snapshot = fixture.Facade.GetDocument().Snapshot!;
        var relationship = new SemanticElementId("demo:bpmn:sequence-flow:event-based-gateway-to-message");
        var visual = snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == relationship);
        var context = await OpenApplicationPartAsync(fixture, visual.Id, label);
        Assert.Equal(relationship, context.Target.SemanticId);
        Assert.Equal(BpmnSemanticTargetKind.Relationship, context.Target.Kind);
        Assert.Equal(label ? BpmnPresentationPartKind.ConnectorLabel : BpmnPresentationPartKind.ConnectorPath, context.Part!.Kind);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect")).Succeeded);
        Assert.Equal(relationship, Assert.Single(fixture.Invocations).Context.Target.SemanticId);
        Assert.Equal(snapshot, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stage4MessageFlowPathAndLabelActionsNeverSubstituteEndpoints(bool label)
    {
        await using var prepared = A22ConnectionHost(new ToolboxSelectionState());
        await prepared.InitializeAsync("stage4-message-source", "container");
        var session = Session(prepared);
        var source = AttachedDocument(session);
        var relationship = new SemanticElementId("stage4:message");
        var visual = new VisualStateId("stage4:message:visual");
        Assert.True((await session.ExecuteAsync(new CreateBpmnMessageFlowCommand(source.DocumentId, source.Revision,
            relationship, visual, A22ViewTaskId, A22ToolPeerTask, A22SourceAnchor(A22ViewTaskVisualId),
            new(A22ToolPeerVisual.Value + ":target")))).IsCommitted);
        Assert.True((await session.ExecuteAsync(new UpdateBpmnMessageFlowNameCommand(source.DocumentId, source.Revision,
            relationship, "Application message"))).IsCommitted);
        await session.WaitForIdleAsync();
        var snapshot = source.CaptureSnapshot();
        await using var fixture = new ApplicationActionFixture(new(initialDocument: snapshot, accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        var context = await OpenApplicationPartAsync(fixture, visual, label);
        Assert.Equal(relationship, context.Target.SemanticId);
        Assert.Equal(BpmnSemanticTargetKind.Relationship, context.Target.Kind);
        Assert.Equal(label ? BpmnPresentationPartKind.ConnectorLabel : BpmnPresentationPartKind.ConnectorPath, context.Part!.Kind);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect")).Succeeded);
        Assert.Equal(relationship, Assert.Single(fixture.Invocations).Context.Target.SemanticId);
        Assert.Equal(snapshot, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage4SubProcessApplicationActionsCoexistWithReadOnlyNavigation()
    {
        var factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync()).WithAccessMode(EditingSessionAccessMode.ReadOnly);
        await using var fixture = new ApplicationActionFixture(factory);
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        await SelectApplicationTargetAsync(fixture.Facade, BpmnDemoPipeline.ProcessOrderSubProcessVisualId);
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var menu = await ApplicationMenuAsync(fixture.Host);
        Assert.NotNull(menu.ScopeNavigationAction);
        Assert.False(menu.PropertiesAvailable);
        Assert.Null(menu.DeletionAction);
        var target = menu.ApplicationContext!.Target;
        Assert.True(fixture.Facade.GetDocument().Snapshot!.SemanticModel.TryGetElement(target.SemanticId, out var semantic));
        Assert.Equal(BpmnSemanticTypes.SubProcess, semantic!.TypeId);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(menu.ApplicationContext.RequestVersion, "app:inspect")).Succeeded);
        Assert.Equal(target.SemanticId, Assert.Single(fixture.Invocations).Context.Target.SemanticId);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage4ConcealedOrganizationalTargetRetiresItsApplicationActions()
    {
        await using var prepared = new FacadeTestFixture(new(initialDocument: A22PoolSnapshot()));
        await prepared.InitializeAsync();
        var snapshot = prepared.Facade.GetDocument().Snapshot!;
        await using var fixture = new ApplicationActionFixture(new(initialDocument: snapshot, accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        var session = Session(fixture.Host);
        await session.UpdateEditorStateAsync(new(semanticSceneSelection: new("sales"), viewport: session.CaptureState().EditorState.Viewport));
        await session.WaitForIdleAsync();
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var menu = await ApplicationMenuAsync(fixture.Host);
        Assert.True((await session.UpdateModelProfileViewStateAsync(session.CaptureState().ModelProfileViewState
            .WithPreferredVisibility(OrganizationalModelProfile.Id, false))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.False((await fixture.Host.ExecuteApplicationActionAsync(menu.ApplicationContext!.RequestVersion, "app:inspect")).Succeeded);
        Assert.Empty(fixture.Invocations);
        Assert.Equal(snapshot, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage4CancellationRegistrationsCanReenterWithoutHoldingHostLocks()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Host.SetApplicationActions(async (_, token) =>
        {
            using var registration = token.Register(() =>
            {
                try
                {
                    // A different thread must acquire the host lock before Cancel returns.
                    // Same-thread Monitor reentrancy alone would hide lock-held consumer code.
                    Task.Run(() => { fixture.Facade.CapturePresentation(); fixture.Host.CloseContextMenu(); })
                        .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    cancellationCompleted.TrySetResult();
                }
                catch (Exception exception) { cancellationCompleted.TrySetException(exception); }
            });
            started.TrySetResult();
            await release.Task;
            return [];
        }, true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            fixture.Host.CloseContextMenu();
            await cancellationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { release.TrySetResult(); }
        Assert.Empty(fixture.Invocations);
    }

    [Fact]
    public async Task Stage4KeyboardMenuRejectsAmbiguousProcessPresentationInstances()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var providerCalls = 0;
        fixture.Host.SetApplicationActions((context, token) => { providerCalls++; return ApplicationActions(context, token); }, true);
        var session = Session(fixture.Host);
        await session.UpdateEditorStateAsync(new(selection: [new VisualStateId("v-source")], viewport: session.CaptureState().EditorState.Viewport));
        await session.WaitForIdleAsync();
        var original = session.CaptureState().CurrentScene!;
        var body = original.Items.First(item => item.Layer == Canvas2DSceneLayer.Content && item.Origin.VisualStateId?.Value == "v-source");
        var other = new Canvas2DSceneItem(new Inceptus.DiagramEngine.Contracts.Presentation.PresentationScopePlacement(
            session.CaptureState().ActiveScopeId, default, new("other-boundary")), new("stage4:other-instance"),
            body.Layer, body.ZIndex, body.Geometry, body.Origin, hitTestPolicy: body.HitTestPolicy);
        var ambiguous = new Canvas2DScene(original.DocumentId, original.SourceRevision, original.LayoutAlgorithmId,
            original.RoutingAlgorithmId, original.Configuration, original.Contributors, original.Viewport, original.ViewportTransform,
            original.ActiveToolId, original.FocusTargetId, original.ToolState, original.ContributorMetadata, original.Items.Append(other),
            presentationContext: original.PresentationContext, presentationPlan: original.PresentationPlan);
        SetSessionField(session, "_currentScene", ambiguous);
        try
        {
            Assert.Null(fixture.Facade.CapturePresentation().PrimaryPart);
            var result = await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
            Assert.Equal(BpmnModelerOperationStatus.Rejected, result.Status);
            Assert.Equal(0, providerCalls);
            Assert.Null(fixture.Host.CaptureState().ContextMenu);
        }
        finally { SetSessionField(session, "_currentScene", original); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stage4PointerBackgroundOrDecorationCannotReuseAPreviouslySelectedOwner(bool decoration)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var providerCalls = 0;
        fixture.Host.SetApplicationActions((context, token) => { providerCalls++; return ApplicationActions(context, token); }, true);
        var session = Session(fixture.Host);
        var original = session.CaptureState().CurrentScene!;
        var scene = original;
        if (decoration)
        {
            var unrelated = new Canvas2DSceneItem(new("stage4:decoration"), Canvas2DSceneLayer.Content, 1000,
                Canvas2DSceneGeometry.Rectangle(new(700, 500, 80, 50)),
                new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.EditorState, stableSourceKey: "stage4:decoration"),
                hitTestPolicy: new(Canvas2DHitTestMode.Bounds));
            scene = new Canvas2DScene(original.DocumentId, original.SourceRevision, original.LayoutAlgorithmId,
                original.RoutingAlgorithmId, original.Configuration, original.Contributors, original.Viewport, original.ViewportTransform,
                original.ActiveToolId, original.FocusTargetId, original.ToolState, original.ContributorMetadata, original.Items.Append(unrelated),
                presentationContext: original.PresentationContext, presentationPlan: original.PresentationPlan);
            SetSessionField(session, "_currentScene", scene);
        }
        try
        {
            await PointerObserver(fixture.Host).ContextMenuDocumentPointAsync(scene, new(740, 525));
            Assert.Equal(0, providerCalls);
            Assert.Null(fixture.Host.CaptureState().ContextMenu?.ApplicationContext);
            Assert.Empty(fixture.Invocations);
        }
        finally { if (decoration) SetSessionField(session, "_currentScene", original); }
    }

    private static async Task<BpmnApplicationActionContext> OpenApplicationPartAsync(ApplicationActionFixture fixture, VisualStateId visual, bool label)
    {
        var scene = Session(fixture.Host).CaptureState().CurrentScene!;
        var item = scene.Items.First(part => part.Origin.VisualStateId == visual &&
            part.Layer == (label ? Canvas2DSceneLayer.Label : Canvas2DSceneLayer.Connector) && part.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        PointD point;
        if (label) point = Center(item.Bounds);
        else
        {
            var route = Canvas2DConnectorPathMetadata.Resolve(item).Select(item.Transform.TransformPoint).ToArray();
            var candidates = route.Zip(route.Skip(1), (a, b) => new[] { new PointD((a.X + b.X) / 2, (a.Y + b.Y) / 2),
                new PointD(a.X + (b.X - a.X) / 4, a.Y + (b.Y - a.Y) / 4) }).SelectMany(points => points);
            point = candidates.First(candidate => new Canvas2DSceneHitTestService().HitTest(scene, candidate)?.SceneObjectId == item.Id);
        }
        await PointerObserver(fixture.Host).ContextMenuDocumentPointAsync(scene, point);
        var context = (await ApplicationMenuAsync(fixture.Host)).ApplicationContext!;
        Assert.Equal(item.Id, context.Part!.SceneObjectId);
        return context;
    }

    private static ValueTask<IEnumerable<BpmnApplicationAction>> ApplicationActions(BpmnApplicationActionContext context, CancellationToken token) =>
        ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>([new("app:inspect", "Inspect")]);

    private static async Task SelectApplicationTargetAsync(BpmnModelerFacade facade, VisualStateId visualId)
    {
        var result = await facade.SelectVisualAsync(facade.CapturePresentation().Stamp!, visualId);
        Assert.True(result.Succeeded, NewDiagramDiagnostics(result.Diagnostics));
    }

    private static async Task<DocumentCanvasContextMenuState> ApplicationMenuAsync(DocumentCanvasHost host)
    {
        await WaitForApplicationConditionAsync(() => host.CaptureState().ContextMenu is { IsApplicationDiscoveryPending: false } menu && !menu.ApplicationActions.IsEmpty);
        return host.CaptureState().ContextMenu!;
    }

    private static async Task WaitForApplicationConditionAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class ApplicationActionFixture : IAsyncDisposable
    {
        private readonly FacadeTestFixture _fixture;

        internal ApplicationActionFixture(BpmnModelerCompositionFactory factory)
        {
            _fixture = new(factory, invokeNotification: DispatchNotificationAsync);
        }

        internal BpmnModelerFacade Facade => _fixture.Facade;
        internal DocumentCanvasHost Host => _fixture.Host;
        internal ConcurrentQueue<object> Notifications => _fixture.Notifications;
        internal ConcurrentQueue<BpmnApplicationActionInvocationEventArgs> Invocations { get; } = new();
        internal Func<BpmnApplicationActionInvocationEventArgs, Task>? Callback { get; set; }
        internal Func<Task>? BeforeDispatch { get; set; }
        internal ValueTask InitializeAsync() => _fixture.InitializeAsync();

        private async Task DispatchNotificationAsync(object notification)
        {
            if (notification is not BpmnApplicationActionDispatch dispatch) return;
            if (BeforeDispatch is { } beforeDispatch) await beforeDispatch();
            if (!dispatch.TryBeginInvocation(out var invocation)) return;
            Invocations.Enqueue(invocation!);
            _ = CompleteCallbackAsync(dispatch, invocation!);
        }

        private async Task CompleteCallbackAsync(BpmnApplicationActionDispatch dispatch, BpmnApplicationActionInvocationEventArgs invocation)
        {
            try
            {
                if (Callback is { } callback) await callback(invocation);
                dispatch.Complete();
            }
            catch (Exception exception) { dispatch.Fail(exception); }
        }

        public ValueTask DisposeAsync() => _fixture.DisposeAsync();
    }
}
