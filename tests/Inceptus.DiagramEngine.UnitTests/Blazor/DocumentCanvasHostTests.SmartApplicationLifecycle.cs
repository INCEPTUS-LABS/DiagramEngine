using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering.Interop;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false)]
    [InlineData(EditingSessionAccessMode.Editable, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true)]
    public async Task SmartActionsCompletedAttemptRediscoveryRejectsOldActivationIdentity(EditingSessionAccessMode mode, bool typed)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, !typed, typed, true, 0);
        if (typed) fixture.Callback = invocation =>
        {
            fixture.Host.CompleteTypedApplicationActionInvocation(invocation.Context.RequestVersion,
                new(BpmnModelerOperationStatus.Succeeded));
            return Task.CompletedTask;
        };
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var original = await SmartToolbarAsync(fixture.Host);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(original.Context.RequestVersion, "app:inspect")).Succeeded);
        await WaitForApplicationConditionAsync(() => fixture.Host.ApplicationToolbar is { IsDiscoveryPending: false } toolbar &&
            toolbar.Context.RequestVersion != original.Context.RequestVersion);
        var current = await SmartToolbarAsync(fixture.Host);
        Assert.NotEqual(original.Context.RequestVersion, current.Context.RequestVersion);
        Assert.Equal(original.Context.Target.SemanticId, current.Context.Target.SemanticId);
        Assert.Single(fixture.Invocations);
        // A delayed second DOM activation cannot borrow the regenerated controls' context.
        Assert.False((await fixture.Host.ExecuteApplicationActionAsync(original.Context.RequestVersion, "app:inspect")).Succeeded);
        Assert.Single(fixture.Invocations);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(current.Context.RequestVersion, "app:inspect")).Succeeded);
        await WaitForApplicationConditionAsync(() => fixture.Invocations.Count == 2);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false)]
    [InlineData(EditingSessionAccessMode.Editable, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true)]
    public async Task SmartActionsExplicitDismissalDuringAttemptSurvivesLateCompletion(EditingSessionAccessMode mode, bool typed)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Callback = async invocation =>
        {
            entered.TrySetResult();
            await release.Task;
            if (typed) fixture.Host.CompleteTypedApplicationActionInvocation(invocation.Context.RequestVersion,
                new(BpmnModelerOperationStatus.Succeeded));
            else fixture.Host.CompleteApplicationActionInvocation(invocation.Context.RequestVersion, failed: false);
            completed.TrySetResult();
        };
        fixture.Host.SetApplicationActions(ApplicationActions, !typed, typed, true, 0);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var original = await SmartToolbarAsync(fixture.Host);
        try
        {
            Assert.True((await fixture.Host.ExecuteApplicationActionAsync(original.Context.RequestVersion, "app:inspect")).Succeeded);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Host.DismissApplicationContextualActions();
        }
        finally { release.TrySetResult(); }
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(fixture.Host.ApplicationToolbar);
        Assert.False((await fixture.Host.ExecuteApplicationActionAsync(original.Context.RequestVersion, "app:inspect")).Succeeded);
        Assert.True((await fixture.Host.RequestApplicationContextualActionsAsync(fixture.Facade.CapturePresentation().Stamp!, 0)).Succeeded);
        Assert.NotEqual(original.Context.RequestVersion, (await SmartToolbarAsync(fixture.Host)).Context.RequestVersion);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false)]
    [InlineData(EditingSessionAccessMode.Editable, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true)]
    public async Task SmartActionsRevisionChangeRejectsQueuedEntryFromEitherSurface(EditingSessionAccessMode mode, bool popup)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 1);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var context = (await SmartToolbarAsync(fixture.Host)).Context;
        if (popup)
        {
            await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
            context = (await ApplicationMenuAsync(fixture.Host)).ApplicationContext!;
        }
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeDispatch = async () => { queued.TrySetResult(); await release.Task; };
        var invocation = fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect").AsTask();
        try
        {
            await queued.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 2);
            Assert.Empty(fixture.Invocations);
        }
        finally { release.TrySetResult(); }
        await invocation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(fixture.Invocations);
        if (popup) Assert.Equal(2, (await ApplicationMenuAsync(fixture.Host)).ApplicationContext!.ApplicationActionsRevision);
        else Assert.Equal(2, (await SmartToolbarAsync(fixture.Host)).Context.ApplicationActionsRevision);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, "render-failure")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "render-failure")]
    [InlineData(EditingSessionAccessMode.Editable, "cancelled-before-start")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "cancelled-before-start")]
    [InlineData(EditingSessionAccessMode.Editable, "cancelled-before-commit")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "cancelled-before-commit")]
    public async Task SmartActionsUncommittedReplacementPreservesAcceptedTargetAndPolicy(EditingSessionAccessMode mode, string scenario)
    {
        var factory = new BpmnModelerCompositionFactory(initialDocument: A24Snapshot(), accessMode: mode);
        var active = new RecordingRenderExecution();
        var candidate = new RecordingRenderExecution();
        if (scenario == "render-failure") candidate.RenderResult = new Canvas2DInteropOperationResult
        { Succeeded = false, Code = "TEST_SMART_REPLACEMENT_FAILURE" };
        var pointers = new LoadingPointerObserverFactory();
        using var facade = new BpmnModelerFacade(factory, static _ => Task.CompletedTask);
        await using var host = new DocumentCanvasHost(factory, CreateRenderer(active),
            new RecordingSurfaceObserverFactory(new RecordingSurfaceObserver(new(900, 600, 1))), pointers,
            replacementRendererFactory: () => CreateRenderer(candidate));
        facade.Attach(host);
        await host.InitializeAsync("smart-active", "smart-standby", "container");
        host.SetApplicationActions(ApplicationActions, true, false, true, 5);
        await SelectApplicationTargetAsync(facade, new("v-source"));
        var before = await SmartToolbarAsync(host);
        var source = facade.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        using var cancellation = new CancellationTokenSource();
        if (scenario == "cancelled-before-start") cancellation.Cancel();
        var replacement = facade.SetDocumentAsync(A24Snapshot(position: new(440, 360)), cancellation.Token).AsTask();
        try
        {
            if (scenario == "cancelled-before-commit")
            {
                await pointers.Candidate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(replacement.IsCompleted);
                cancellation.Cancel();
            }
        }
        finally { pointers.Candidate.Release.TrySetResult(); }
        var result = await replacement.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(scenario == "render-failure" ? BpmnModelerOperationStatus.Failed : BpmnModelerOperationStatus.Cancelled, result.Status);
        await host.ZoomInAsync();
        var retained = await SmartToolbarAsync(host);
        Assert.Equal(before.Context.Stamp.AttachmentVersion, retained.Context.Stamp.AttachmentVersion);
        Assert.Equal(before.Context.Target.SemanticId, retained.Context.Target.SemanticId);
        Assert.Equal(5, retained.Context.ApplicationActionsRevision);
        Assert.Equal("app:inspect", Assert.Single(retained.Actions).Id);
        Assert.Same(source, facade.GetDocument().Snapshot);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(facade.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartActionsDisposedInstanceCannotReviveOrAffectIndependentInstance(EditingSessionAccessMode mode)
    {
        await using var first = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await using var second = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await first.InitializeAsync();
        await second.InitializeAsync();
        var pending = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Host.SetApplicationActions(async (_, token) => { pending.TrySetResult(token); await release.Task; return [new("app:old", "Old")]; }, true, false, true, 0);
        second.Host.SetApplicationActions(ApplicationActions, true, false, true, 0);
        await SelectApplicationTargetAsync(first.Facade, new("v-source"));
        await SelectApplicationTargetAsync(second.Facade, new("v-source"));
        var token = await pending.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var independent = await SmartToolbarAsync(second.Host);
        first.Facade.Dispose();
        release.TrySetResult();
        Assert.True(token.IsCancellationRequested);
        Assert.Null(first.Host.ApplicationToolbar);
        Assert.Equal(independent.Context.RequestVersion, second.Host.ApplicationToolbar!.Context.RequestVersion);
        Assert.True((await second.Host.ExecuteApplicationActionAsync(independent.Context.RequestVersion, "app:inspect")).Succeeded);
        Assert.Empty(first.Invocations);
        Assert.Single(second.Invocations);
    }
}
