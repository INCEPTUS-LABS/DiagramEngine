using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false)]
    [InlineData(EditingSessionAccessMode.Editable, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true)]
    public async Task ActionObservationSeparatesInstallationAdmissionEntryAndCompletion(EditingSessionAccessMode mode, bool popup)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        var receipt = Assert.IsType<BpmnApplicationActionObservation>(fixture.Host.BeginApplicationActionObservation());
        Assert.Same(receipt, fixture.Host.BeginApplicationActionObservation());
        var source = fixture.Facade.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        var context = await ObserveActionTargetAsync(fixture, popup);
        Assert.True((await fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect")).Succeeded);
        await WaitForApplicationConditionAsync(() => receipt.Read().Events.Any(e =>
            e.RequestVersion == context.RequestVersion && e.Stage == BpmnActionObservationStage.CompletionProcessed));
        var snapshot = receipt.Read();
        var events = snapshot.Events.Where(e => e.RequestVersion == context.RequestVersion).ToArray();
        AssertObservationOrder(events, BpmnActionObservationStage.RequestCreated, BpmnActionObservationStage.DiscoveryInstalled,
            BpmnActionObservationStage.InvocationAdmitted, BpmnActionObservationStage.RecheckInstalled,
            BpmnActionObservationStage.DispatchEnqueued, BpmnActionObservationStage.QueueDelivery,
            BpmnActionObservationStage.EntryAccepted, BpmnActionObservationStage.CompletionReceived,
            BpmnActionObservationStage.CompletionProcessed, BpmnActionObservationStage.RequestRetired);
        Assert.Contains(events, e => e.Stage == BpmnActionObservationStage.ProviderCallStarted && e.Phase == BpmnActionObservationPhase.Discovery);
        Assert.Contains(events, e => e.Stage == BpmnActionObservationStage.ProviderCallStarted && e.Phase == BpmnActionObservationPhase.Recheck);
        var installed = Assert.Single(events, e => e.Stage == BpmnActionObservationStage.DiscoveryInstalled);
        Assert.Equal("app:inspect", Assert.Single(installed.Actions).Id);
        Assert.True(installed.Actions[0].Enabled);
        var request = Assert.Single(snapshot.Requests, r => r.RequestVersion == context.RequestVersion);
        Assert.Equal(context.Stamp.InstanceId, request.InstanceId);
        Assert.Equal(context.Stamp.AttachmentVersion, request.AttachmentVersion);
        Assert.Equal(context.Target.SemanticId.Value, request.OwnerId);
        Assert.Equal(popup ? "Popup" : "Toolbar", request.Surface);
        Assert.False(snapshot.Overflow);
        Assert.False(snapshot.Truncated);
        Assert.Single(fixture.Invocations);
        Assert.Same(source, fixture.Facade.GetDocument().Snapshot);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(fixture.Facade.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false)]
    [InlineData(EditingSessionAccessMode.Editable, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true)]
    public async Task ActionObservationProvesQueuedRetirementWithoutConsumerEntry(EditingSessionAccessMode mode, bool popup)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        var receipt = fixture.Host.BeginApplicationActionObservation()!;
        var context = await ObserveActionTargetAsync(fixture, popup);
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeDispatch = async () => { queued.TrySetResult(); await release.Task; };
        var invocation = fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect").AsTask();
        try
        {
            await queued.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 2);
        }
        finally { release.TrySetResult(); }
        await invocation.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForApplicationConditionAsync(() => receipt.Read().Events.Any(e => e.Stage == BpmnActionObservationStage.EntryRejected));
        var events = receipt.Read().Events.Where(e => e.RequestVersion == context.RequestVersion).ToArray();
        AssertObservationOrder(events, BpmnActionObservationStage.DispatchEnqueued, BpmnActionObservationStage.RequestRetired,
            BpmnActionObservationStage.EntryRejected);
        Assert.DoesNotContain(events, e => e.Stage == BpmnActionObservationStage.EntryAccepted);
        Assert.Empty(fixture.Invocations);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task ActionObservationRetainsLateCompletionIdentityAfterRequestRemoval(EditingSessionAccessMode mode)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        var receipt = fixture.Host.BeginApplicationActionObservation()!;
        var context = await ObserveActionTargetAsync(fixture, false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Callback = async _ => { entered.TrySetResult(); await release.Task; };
        try
        {
            await fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 2);
        }
        finally { release.TrySetResult(); }
        await WaitForApplicationConditionAsync(() => receipt.Read().Events.Any(e => e.Stage == BpmnActionObservationStage.CompletionIgnored));
        AssertObservationOrder(receipt.Read().Events.Where(e => e.RequestVersion == context.RequestVersion).ToArray(),
            BpmnActionObservationStage.EntryAccepted, BpmnActionObservationStage.RequestRetired,
            BpmnActionObservationStage.CompletionReceived, BpmnActionObservationStage.CompletionIgnored);
        Assert.Single(fixture.Invocations);
    }

    [Fact]
    public async Task ActionObservationDoesNotCallCancelledProviderWaitAnInstallationRejection()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot()));
        await fixture.InitializeAsync();
        var receipt = fixture.Host.BeginApplicationActionObservation()!;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Host.SetApplicationActions(async (_, _) => { started.TrySetResult(); await release.Task; return [new("old", "Old")]; }, true, false, true, 1);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Host.SetApplicationActions(ApplicationActions, true, false, true, 2);
            await WaitForApplicationConditionAsync(() => receipt.Read().Events.Any(e => e.Stage == BpmnActionObservationStage.ProviderWaitCancelled));
        }
        finally { release.TrySetResult(); }
        var request = Assert.Single(receipt.Read().Requests, r => r.ApplicationRevision == 1);
        Assert.DoesNotContain(receipt.Read().Events, e => e.RequestVersion == request.RequestVersion &&
            e.Stage is BpmnActionObservationStage.DiscoveryInstalled or BpmnActionObservationStage.DiscoveryInstallRejected);
    }

    [Fact]
    public async Task ActionObservationDistinguishesQueueDropFromRejectedAdmission()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot()));
        await fixture.InitializeAsync();
        var context = await ObserveActionTargetAsync(fixture, false);
        var receipt = new BpmnApplicationActionObservation();
        var request = receipt.BeginRequest(context, true, false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var queue = new BpmnModelerNotifications(async _ => { entered.TrySetResult(); await release.Task; });
        var first = queue.Enqueue(new object());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pending = queue.Enqueue(new BpmnApplicationActionDispatch(fixture.Host, context.RequestVersion, "app:inspect", observation: request));
        queue.Dispose();
        release.TrySetResult();
        await Task.WhenAll(first, pending).WaitAsync(TimeSpan.FromSeconds(10));
        await queue.Enqueue(new BpmnApplicationActionDispatch(fixture.Host, context.RequestVersion, "app:inspect", observation: request));
        AssertObservationOrder(receipt.Read().Events.ToArray(), BpmnActionObservationStage.DispatchEnqueued,
            BpmnActionObservationStage.QueueDropped, BpmnActionObservationStage.QueueRejected);
        Assert.DoesNotContain(receipt.Read().Events, e => e.Stage == BpmnActionObservationStage.QueueDelivery);
    }

    [Fact]
    public async Task ActionObservationIsBoundedDetachedAndInstanceLocal()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot()));
        await fixture.InitializeAsync();
        var context = await ObserveActionTargetAsync(fixture, false);
        var first = new BpmnApplicationActionObservation();
        var second = new BpmnApplicationActionObservation();
        var request = first.BeginRequest(context, true, true)!;
        var frozen = first.Read();
        request.Record(BpmnActionObservationStage.DiscoveryInstalled, actions: [new(new string('x', 513), "Label")]);
        for (var i = 0; i < BpmnApplicationActionObservation.MaximumEvents; i++) request.Record(BpmnActionObservationStage.EntryRejected);
        Assert.Single(frozen.Events);
        Assert.True(first.Read().Overflow);
        Assert.True(first.Read().Truncated);
        Assert.Equal(BpmnApplicationActionObservation.MaximumEvents, first.Read().Events.Length);
        Assert.Empty(second.Read().Events);
        for (var i = 0; i < BpmnApplicationActionObservation.MaximumRequests; i++) second.BeginRequest(context, false, false);
        Assert.Null(second.BeginRequest(context, false, false));
        Assert.True(second.Read().Overflow);
        Assert.Equal(BpmnApplicationActionObservation.MaximumRequests, second.Read().Requests.Length);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, BpmnModelerOperationStatus.Succeeded)]
    [InlineData(EditingSessionAccessMode.ReadOnly, BpmnModelerOperationStatus.Succeeded)]
    [InlineData(EditingSessionAccessMode.Editable, BpmnModelerOperationStatus.Unavailable)]
    [InlineData(EditingSessionAccessMode.ReadOnly, BpmnModelerOperationStatus.Unavailable)]
    [InlineData(EditingSessionAccessMode.Editable, BpmnModelerOperationStatus.Cancelled)]
    [InlineData(EditingSessionAccessMode.ReadOnly, BpmnModelerOperationStatus.Cancelled)]
    [InlineData(EditingSessionAccessMode.Editable, BpmnModelerOperationStatus.Failed)]
    [InlineData(EditingSessionAccessMode.ReadOnly, BpmnModelerOperationStatus.Failed)]
    public async Task ActionObservationReportsTypedHostCompletionSeparatelyFromConsumerReturn(
        EditingSessionAccessMode mode, BpmnModelerOperationStatus status)
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        fixture.TypedOutcome = new(status);
        var receipt = fixture.Host.BeginApplicationActionObservation()!;
        var context = await ObserveActionTargetAsync(fixture, false, typed: true);
        await fixture.Host.ExecuteApplicationActionAsync(context.RequestVersion, "app:inspect");
        await WaitForApplicationConditionAsync(() => receipt.Read().Events.Any(e => e.Stage == BpmnActionObservationStage.CompletionApplied));
        var events = receipt.Read().Events.Where(e => e.RequestVersion == context.RequestVersion).ToArray();
        AssertObservationOrder(events, BpmnActionObservationStage.CompletionReceived, BpmnActionObservationStage.CompletionApplied,
            BpmnActionObservationStage.RequestRetired);
        Assert.Equal(status.ToString(), Assert.Single(events, e => e.Stage == BpmnActionObservationStage.CompletionApplied).Detail);
        Assert.DoesNotContain(events, e => e.Stage == BpmnActionObservationStage.CompletionProcessed);
    }

    private static async Task<BpmnApplicationActionContext> ObserveActionTargetAsync(ApplicationActionFixture fixture, bool popup, bool typed = false)
    {
        fixture.Host.SetApplicationActions(ApplicationActions, !typed, typed, true, 1);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        var context = (await SmartToolbarAsync(fixture.Host)).Context;
        if (popup)
        {
            await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
            context = (await ApplicationMenuAsync(fixture.Host)).ApplicationContext!;
        }
        return context;
    }

    private static void AssertObservationOrder(BpmnObservedActionEvent[] events, params BpmnActionObservationStage[] stages)
    {
        var previous = 0;
        foreach (var stage in stages)
        {
            var observed = Assert.Single(events, e => e.Stage == stage);
            Assert.True(observed.Sequence > previous, $"{stage} did not follow sequence {previous}.");
            previous = observed.Sequence;
        }
    }
}
