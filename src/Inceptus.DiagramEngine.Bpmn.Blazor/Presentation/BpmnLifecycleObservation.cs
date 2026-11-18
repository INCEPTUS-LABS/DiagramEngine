using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

// The source Demo receives this receipt, never the live child/host/session/renderer.
internal sealed class BpmnLifecycleObservation(BpmnDecorationAttachment attachment)
{
    internal Guid Id { get; } = Guid.NewGuid();
    internal BpmnDecorationAttachment Attachment { get; } = attachment;
    internal LifecycleObservation Evidence { get; } = new(attachment.SessionAttachmentId);
    internal void ExpectOperation(long revision) => Evidence.ExpectOperation(revision);
    internal void TrackOperation(BpmnDecorationAttachment target, long revision, Task operation)
    {
        Evidence.TrackOperation(target == Attachment ? target.SessionAttachmentId : Guid.Empty, revision, operation);
    }

    internal BpmnLifecycleSnapshot Read()
    {
        var snapshot = Evidence.Read();
        return new(Id, Attachment, snapshot.Events.Select(e =>
            new BpmnLifecycleEvent(e.Sequence, e.Timestamp, e.Stage, e.Detail, e.Failure)).ToImmutableArray(),
            snapshot.Overflow, snapshot.Invalid, snapshot.InputRevision, snapshot.AcceptedAtEntry,
            snapshot.PendingAtEntry, snapshot.OperationStatus, snapshot.DisposalStatus);
    }
}

internal sealed record BpmnLifecycleEvent(int Sequence, long Timestamp, string Stage, string Detail, bool Failure);
internal sealed record BpmnLifecycleSnapshot(Guid Id, BpmnDecorationAttachment Attachment,
    ImmutableArray<BpmnLifecycleEvent> Events, bool Overflow, bool Invalid, long? InputRevision,
    bool? AcceptedAtEntry, bool? PendingAtEntry, string? OperationStatus, string? DisposalStatus)
{
    private static readonly string[] RequiredStages = ["NotificationsRetired", "PointerDisposed", "InteractionDisposed", "SurfaceDisposed",
        "RendererDisposed", "SessionDisposed", "HostDisposed", "MenuDisposed", "ComponentLifetimeDisposed",
        "PointerCallbackRetired", "SurfaceCallbackRetired"];
    internal bool CompletionChainObserved => DisposalStatus == nameof(TaskStatus.RanToCompletion) &&
        !Overflow && !Invalid && !Events.Any(e => e.Failure) &&
        RequiredStages.All(stage => Events.Any(e => e.Stage == stage));
    internal bool AcceptedPendingAtEntryObserved => !Overflow && !Invalid && AcceptedAtEntry == true && PendingAtEntry == true;
}
