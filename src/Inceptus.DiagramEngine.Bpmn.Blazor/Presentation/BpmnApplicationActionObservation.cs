using System.Collections.Immutable;
using System.Diagnostics;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

// Passive, per-component evidence. No callbacks, tasks, native objects or host references escape.
internal sealed class BpmnApplicationActionObservation
{
    internal const int MaximumRequests = 64;
    internal const int MaximumEvents = 256;
    private readonly object _sync = new();
    private readonly List<BpmnObservedRequest> _requests = [];
    private readonly List<BpmnObservedActionEvent> _events = [];
    private bool _overflow;
    private bool _truncated;

    internal Guid Id { get; } = Guid.NewGuid();

    internal BpmnActionObservationRequest? BeginRequest(BpmnApplicationActionContext context, bool toolbar, bool typed)
    {
        lock (_sync)
        {
            if (_requests.Count == MaximumRequests) { _overflow = true; return null; }
            var stamp = context.Stamp;
            var key = new BpmnObservedRequest(stamp.InstanceId, stamp.AttachmentVersion, context.RequestVersion,
                context.ApplicationActionsRevision, toolbar ? "Toolbar" : "Popup", typed,
                Copy(stamp.DocumentId.Value)!, stamp.Revision.Value, Copy(context.Presentation.ActiveScopeId?.Value),
                Copy(stamp.PresentationContext.GetType().Name)!, stamp.ContextVersion, stamp.SelectionVersion,
                context.Target.Kind.ToString(), Copy(context.Target.SemanticId.Value)!,
                Copy(context.Target.VisualStateId?.Value), context.Part?.Kind.ToString());
            _requests.Add(key);
            RecordUnderLock(key.RequestVersion, BpmnActionObservationStage.RequestCreated, BpmnActionObservationPhase.None, null, null, null);
            return new(this, key.RequestVersion);
        }
    }

    internal void Record(long requestVersion, BpmnActionObservationStage stage, BpmnActionObservationPhase phase,
        string? actionId, string? detail, ImmutableArray<BpmnApplicationAction> actions)
    {
        lock (_sync) RecordUnderLock(requestVersion, stage, phase, actionId, detail, actions.IsDefault ? null : actions);
    }

    private void RecordUnderLock(long requestVersion, BpmnActionObservationStage stage, BpmnActionObservationPhase phase,
        string? actionId, string? detail, ImmutableArray<BpmnApplicationAction>? actions)
    {
        if (_events.Count == MaximumEvents) { _overflow = true; return; }
        var copied = ImmutableArray.CreateBuilder<BpmnObservedAction>();
        if (actions is { } values)
        {
            if (values.Length > 64) _truncated = true;
            foreach (var action in values.Take(64)) copied.Add(new(Copy(action.Id)!, action.IsEnabled));
        }
        _events.Add(new(_events.Count + 1, Stopwatch.GetTimestamp(), requestVersion, stage, phase,
            Copy(actionId), Copy(detail), copied.ToImmutable()));
    }

    private string? Copy(string? value)
    {
        if (value is null || value.Length <= 512) return value;
        _truncated = true;
        return value[..512];
    }

    internal BpmnApplicationActionObservationSnapshot Read()
    {
        lock (_sync) return new(Id, Stopwatch.Frequency, [.. _requests], [.. _events], _overflow, _truncated);
    }
}

internal sealed class BpmnActionObservationRequest(BpmnApplicationActionObservation receipt, long version)
{
    internal void Record(BpmnActionObservationStage stage, BpmnActionObservationPhase phase = BpmnActionObservationPhase.None,
        string? actionId = null, string? detail = null, ImmutableArray<BpmnApplicationAction> actions = default) =>
        receipt.Record(version, stage, phase, actionId, detail, actions);
}

internal enum BpmnActionObservationPhase { None, Discovery, Recheck, Handler }

internal enum BpmnActionObservationStage
{
    RequestCreated, ProviderCallStarted, HostProviderTaskCaptured, ProviderResultReceived, ProviderWaitCancelled, ProviderFailed,
    DiscoveryInstalled, DiscoveryInstallRejected, RecheckInstalled, RecheckRejected, FailureInstalled, FailureInstallRejected,
    InvocationAdmitted, DispatchEnqueued, QueueRejected, QueueDelivery, QueueDropped, ComponentDropped,
    EntryAccepted, EntryRejected, ConsumerCallAttempt, CompletionReceived, CompletionApplied, CompletionProcessed, CompletionIgnored, RequestRetired,
}

internal sealed record BpmnObservedRequest(Guid InstanceId, long AttachmentVersion, long RequestVersion,
    long ApplicationRevision, string Surface, bool Typed, string DocumentId, ulong DocumentRevision, string? ScopeId,
    string ViewKind, long ContextVersion, long SelectionVersion, string OwnerKind, string OwnerId, string? VisualId, string? Part);
internal sealed record BpmnObservedAction(string Id, bool Enabled);
internal sealed record BpmnObservedActionEvent(int Sequence, long Timestamp, long RequestVersion,
    BpmnActionObservationStage Stage, BpmnActionObservationPhase Phase, string? ActionId, string? Detail,
    ImmutableArray<BpmnObservedAction> Actions);
internal sealed record BpmnApplicationActionObservationSnapshot(Guid Id, long TimestampFrequency,
    ImmutableArray<BpmnObservedRequest> Requests, ImmutableArray<BpmnObservedActionEvent> Events, bool Overflow, bool Truncated);
