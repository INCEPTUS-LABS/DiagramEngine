using System.Collections.Immutable;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartActionsKeyboardPopupPreservesNativeEligibilityWithoutInventingHit(EditingSessionAccessMode mode)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true);
        var session = Session(fixture.Host);
        var document = fixture.Facade.GetDocument().Snapshot!;
        var history = session.CaptureState().HistoryStatus;
        Assert.True((await session.UpdateEditorStateAsync(new(selection: [new VisualStateId("v-source")],
            viewport: session.CaptureState().EditorState.Viewport))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.Null(fixture.Facade.CapturePresentation().PrimaryPart);

        Assert.True((await fixture.Host.OpenApplicationContextMenuFromSelectionAsync()).Succeeded);
        var menu = await ApplicationMenuAsync(fixture.Host);
        var context = menu.ApplicationContext!;
        Assert.Null(context.Part);
        Assert.Equal(new SemanticElementId("source"), context.Target.SemanticId);
        Assert.Equal(new VisualStateId("v-source"), context.Target.VisualStateId);
        Assert.Equal(mode == EditingSessionAccessMode.Editable, menu.PropertiesAvailable);
        Assert.Equal(mode == EditingSessionAccessMode.Editable, menu.DeletionAction is not null);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect")).Succeeded);
        var invocation = Assert.Single(fixture.Invocations);
        Assert.Null(invocation.Context.Part);
        Assert.Equal(context.Target.SemanticId, invocation.Context.Target.SemanticId);
        Assert.Same(document, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartActionsEnableExistingSelectionAndDispatchWithoutNativeMutation(EditingSessionAccessMode mode)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var document = fixture.Facade.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(document);
        var history = Session(fixture.Host).CaptureState().HistoryStatus;
        var native = Session(fixture.Host).CaptureState().ProjectedGraph;
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 3);
        var toolbar = await SmartToolbarAsync(fixture.Host);
        Assert.Equal(3, toolbar.Context.ApplicationActionsRevision);
        Assert.Equal(new SemanticElementId("source"), toolbar.Context.Target.SemanticId);
        Assert.Null(fixture.Host.CaptureState().ContextMenu);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(toolbar.Context.RequestVersion, "app:inspect")).Succeeded);
        await WaitForApplicationConditionAsync(() => fixture.Invocations.Count == 1);
        Assert.False((await fixture.Host.ExecuteApplicationActionAsync(toolbar.Context.RequestVersion, "app:inspect")).Succeeded);
        Assert.Same(document, fixture.Facade.GetDocument().Snapshot);
        Assert.Same(native, Session(fixture.Host).CaptureState().ProjectedGraph);
        Assert.Equal(history, Session(fixture.Host).CaptureState().HistoryStatus);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(document).AsSpan()));
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartActionsToolbarDisableLeavesPopupAndIndependentRequestAvailable(EditingSessionAccessMode mode)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 0);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var toolbar = await SmartToolbarAsync(fixture.Host);
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var popup = await ApplicationMenuAsync(fixture.Host);
        Assert.Null(fixture.Host.ApplicationToolbar);
        Assert.NotEqual(toolbar.Context.RequestVersion, popup.ApplicationContext!.RequestVersion);
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, false, 0);
        Assert.Null(fixture.Host.ApplicationToolbar);
        Assert.Equal(popup.ApplicationContext.RequestVersion, fixture.Host.CaptureState().ContextMenu!.ApplicationContext!.RequestVersion);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(popup.ApplicationContext.RequestVersion, "app:inspect")).Succeeded);
        Assert.Single(fixture.Invocations);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartActionsDismissalSurvivesPolicyRefreshAndPopupCloseUntilExplicitReactivation(EditingSessionAccessMode mode)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 1);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var original = await SmartToolbarAsync(fixture.Host);
        fixture.Host.DismissApplicationContextualActions();
        Assert.Null(fixture.Host.ApplicationToolbar);
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 2);
        Assert.Null(fixture.Host.ApplicationToolbar);
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        await ApplicationMenuAsync(fixture.Host);
        var suspended = await fixture.Host.RequestApplicationContextualActionsAsync(fixture.Facade.CapturePresentation().Stamp!, 2);
        Assert.Equal("BPMN_APPLICATION_TOOLBAR_SUSPENDED", Assert.Single(suspended.Diagnostics).Code);
        fixture.Host.CloseContextMenu();
        Assert.Null(fixture.Host.ApplicationToolbar);
        var stale = await fixture.Host.RequestApplicationContextualActionsAsync(original.Context.Stamp, 1);
        Assert.Equal("BPMN_APPLICATION_CONTEXT_STALE", Assert.Single(stale.Diagnostics).Code);
        Assert.Null(fixture.Host.ApplicationToolbar);
        Assert.True((await fixture.Host.RequestApplicationContextualActionsAsync(fixture.Facade.CapturePresentation().Stamp!, 2)).Succeeded);
        var current = await SmartToolbarAsync(fixture.Host);
        Assert.NotEqual(original.Context.RequestVersion, current.Context.RequestVersion);
        Assert.Equal(original.Context.Target.SemanticId, current.Context.Target.SemanticId);
        Assert.Equal(2, current.Context.ApplicationActionsRevision);
    }

    [Fact]
    public async Task SmartActionsSameProviderRevisionRefreshesSeparateRequestsWithoutNativeWork()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var label = "First";
        BpmnApplicationActionProvider provider = (_, _) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>([new("app:inspect", label)]);
        fixture.Host.SetApplicationActions(provider, true, false, true, 2);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var toolbar = await SmartToolbarAsync(fixture.Host);
        var native = Session(fixture.Host).CaptureState();
        label = "Second";
        fixture.Host.SetApplicationActions(provider, true, false, true, 3);
        var updated = await SmartToolbarAsync(fixture.Host);
        Assert.Equal("Second", Assert.Single(updated.Actions).Label);
        Assert.NotEqual(toolbar.Context.RequestVersion, updated.Context.RequestVersion);
        Assert.False((await fixture.Host.ExecuteApplicationActionAsync(toolbar.Context.RequestVersion, "app:inspect")).Succeeded);
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var popup = await ApplicationMenuAsync(fixture.Host);
        label = "Third";
        fixture.Host.SetApplicationActions(provider, true, false, true, 4);
        var refreshed = await ApplicationMenuAsync(fixture.Host);
        Assert.Equal("Third", Assert.Single(refreshed.ApplicationActions).Label);
        Assert.Equal(popup.ApplicationContext!.Target.SemanticId, refreshed.ApplicationContext!.Target.SemanticId);
        Assert.NotEqual(popup.ApplicationContext.RequestVersion, refreshed.ApplicationContext.RequestVersion);
        var current = Session(fixture.Host).CaptureState();
        Assert.Same(native.ProjectedGraph, current.ProjectedGraph);
        Assert.Same(native.LayoutResult, current.LayoutResult);
        Assert.Same(native.RoutingResult, current.RoutingResult);
        Assert.Equal(native.DocumentRevision, current.DocumentRevision);
        Assert.Equal(native.HistoryStatus, current.HistoryStatus);
    }

    [Theory]
    [InlineData(-1, false, "BPMN_APPLICATION_REVISION_INVALID")]
    [InlineData(2, false, "BPMN_APPLICATION_REVISION_INVALID")]
    [InlineData(3, true, "BPMN_APPLICATION_HANDLER_CONFLICT")]
    public async Task SmartActionsInvalidRegistrationRetiresBothSurfacesAndRecovers(long rejectedRevision, bool conflict, string code)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 3);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var toolbar = await SmartToolbarAsync(fixture.Host);
        fixture.Host.SetApplicationActions(ApplicationActions, true, conflict, true, rejectedRevision);
        fixture.Host.SetApplicationActions(ApplicationActions, true, conflict, true, rejectedRevision);
        await WaitForApplicationConditionAsync(() => fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>().Any());
        Assert.Single(fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>(), item => item.Diagnostics.Any(d => d.Code == code));
        Assert.False(fixture.Host.HasApplicationActions);
        Assert.Null(fixture.Host.ApplicationToolbar);
        Assert.False((await fixture.Host.ExecuteApplicationActionAsync(toolbar.Context.RequestVersion, "app:inspect")).Succeeded);
        var rejected = await fixture.Host.RequestApplicationContextualActionsAsync(fixture.Facade.CapturePresentation().Stamp!, 3);
        Assert.Equal(code, Assert.Single(rejected.Diagnostics).Code);
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 3);
        Assert.Equal(3, (await SmartToolbarAsync(fixture.Host)).Context.ApplicationActionsRevision);
    }

    [Fact]
    public async Task SmartActionsRevisionHighWaterSurvivesIdenticalSourceReplacementAndRetiresOldHandle()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 7);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var old = await SmartToolbarAsync(fixture.Host);
        var source = fixture.Facade.GetDocument().Snapshot!;
        Assert.True((await fixture.Facade.SetDocumentAsync(source)).Succeeded);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var current = await SmartToolbarAsync(fixture.Host);
        Assert.NotEqual(old.Context.Stamp.AttachmentVersion, current.Context.Stamp.AttachmentVersion);
        Assert.Equal(source, fixture.Facade.GetDocument().Snapshot);
        Assert.False((await fixture.Host.ExecuteApplicationActionAsync(old.Context.RequestVersion, "app:inspect")).Succeeded);
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 0);
        Assert.False(fixture.Host.HasApplicationActions);
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 7);
        Assert.Equal(7, (await SmartToolbarAsync(fixture.Host)).Context.ApplicationActionsRevision);
    }

    [Theory]
    [InlineData(BpmnModelerOperationStatus.Succeeded, false)]
    [InlineData(BpmnModelerOperationStatus.Cancelled, false)]
    [InlineData(BpmnModelerOperationStatus.Unavailable, true)]
    [InlineData(BpmnModelerOperationStatus.Failed, true)]
    public async Task SmartActionsTypedOutcomeReportsAttemptStatusWithoutNativeChanges(BpmnModelerOperationStatus status, bool reports)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var source = fixture.Facade.GetDocument().Snapshot;
        fixture.Host.SetApplicationActions(ApplicationActions, false, true, true, 0);
        fixture.Callback = invocation =>
        {
            fixture.Host.CompleteTypedApplicationActionInvocation(invocation.Context.RequestVersion, new(status));
            return Task.CompletedTask;
        };
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var toolbar = await SmartToolbarAsync(fixture.Host);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(toolbar.Context.RequestVersion, "app:inspect")).Succeeded);
        await WaitForApplicationConditionAsync(() => fixture.Invocations.Count == 1);
        if (reports)
        {
            await WaitForApplicationConditionAsync(() => fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>().Any());
            Assert.Equal(status, Assert.Single(fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>()).Status);
        }
        else Assert.Empty(fixture.Notifications.OfType<BpmnModelerOperationFailedEventArgs>());
        Assert.Same(source, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task SmartActionsLatePolicyDiscoveryCannotReplaceNewRevision()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BpmnApplicationActionProvider provider = async (context, token) =>
        {
            if (context.ApplicationActionsRevision == 1) { entered.TrySetResult(token); await release.Task; return [new("app:old", "Old")]; }
            return [new("app:new", "New")];
        };
        fixture.Host.SetApplicationActions(provider, true, false, true, 1);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Host.SetApplicationActions(provider, true, false, true, 2);
        var current = await SmartToolbarAsync(fixture.Host);
        release.TrySetResult();
        Assert.Equal("app:new", Assert.Single(current.Actions).Id);
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(2, fixture.Host.ApplicationToolbar!.Context.ApplicationActionsRevision);
    }

    [Fact]
    public async Task SmartActionsPopupExactHitDuringMultipleSelectionRetainsActualOwner()
    {
        var factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync()).WithAccessMode(EditingSessionAccessMode.ReadOnly);
        await using var fixture = new ApplicationActionFixture(factory);
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 0);
        var session = Session(fixture.Host);
        Assert.True((await session.UpdateEditorStateAsync(new(selection: [BpmnDemoPipeline.TaskVisualId, BpmnDemoPipeline.ApprovedTaskVisualId],
            viewport: session.CaptureState().EditorState.Viewport))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.Null(fixture.Host.ApplicationToolbar);
        var scene = session.CaptureState().CurrentScene!;
        var body = scene.Items.Single(item => item.Origin.VisualStateId == BpmnDemoPipeline.ApprovedTaskVisualId &&
            item.Layer == Canvas2DSceneLayer.Content && item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        await PointerObserver(fixture.Host).ContextMenuDocumentPointAsync(scene, body.Bounds.TopLeft + new VectorD(12, 12));
        var menu = await ApplicationMenuAsync(fixture.Host);
        Assert.Equal(body.Origin.SemanticElementId, menu.ApplicationContext!.Target.SemanticId);
        Assert.Equal(body.Id, menu.ApplicationContext.Part!.SceneObjectId);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(menu.ApplicationContext.RequestVersion, "app:inspect")).Succeeded);
        Assert.Equal(body.Origin.SemanticElementId, Assert.Single(fixture.Invocations).Context.Target.SemanticId);
    }

    [Fact]
    public async Task SmartActionsReactivationAcknowledgesAdmissionAndCallerCancellationDoesNotOwnDiscovery()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 0);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await SmartToolbarAsync(fixture.Host);
        fixture.Host.DismissApplicationContextualActions();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Host.SetApplicationActions(async (_, token) => { entered.TrySetResult(token); await release.Task; return [new("app:inspect", "Inspect")]; }, true, false, true, 1);
        using var caller = new CancellationTokenSource();
        Assert.True((await fixture.Host.RequestApplicationContextualActionsAsync(fixture.Facade.CapturePresentation().Stamp!, 1, caller.Token)).Succeeded);
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        caller.Cancel();
        Assert.False(token.IsCancellationRequested);
        release.TrySetResult();
        Assert.Single((await SmartToolbarAsync(fixture.Host)).Actions);
    }

    private static async Task<DocumentCanvasApplicationToolbarState> SmartToolbarAsync(DocumentCanvasHost host)
    {
        await WaitForApplicationConditionAsync(() => host.ApplicationToolbar is { IsDiscoveryPending: false, Actions.IsEmpty: false });
        return host.ApplicationToolbar!;
    }
}
