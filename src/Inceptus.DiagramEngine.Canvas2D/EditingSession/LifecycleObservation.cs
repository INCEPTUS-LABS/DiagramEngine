using System.Collections.Immutable;
using System.Diagnostics;

namespace Inceptus.DiagramEngine.Canvas2D.EditingSession;

// Internal, opt-in evidence for the source Demo. No callbacks, IO or scheduling in
// the observed paths. This lock protects copied evidence only, never engine state.
internal sealed class LifecycleObservation(Guid sessionAttachmentId)
{
    private const int Capacity = 128;
    private readonly object _sync = new();
    private readonly List<LifecycleObservationEvent> _events = new(Capacity);
    private Task? _operation;
    private Task? _disposal;
    private long? _expectedRevision;
    private long? _acceptedRevision;
    private bool _entryObserved;
    private bool? _acceptedAtEntry;
    private bool? _pendingAtEntry;
    private bool _overflow;
    private bool _invalid;
    private string? _operationStatus;
    private string? _disposalStatus;

    internal void ExpectOperation(long revision)
    {
        lock (_sync)
        {
            if (_expectedRevision is not null || _entryObserved) { Invalidate("Operation already registered or disposal entered."); return; }
            _expectedRevision = revision;
            Record("OperationExpected", revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    internal void TrackOperation(Guid attachment, long revision, Task operation)
    {
        lock (_sync)
        {
            if (attachment != sessionAttachmentId || revision != _expectedRevision || _operation is not null || _operationStatus is not null || _entryObserved)
            { Invalidate("Operation identity mismatch or late registration."); return; }
            _operation = operation;
            Record("OperationTracked", operation.Status.ToString());
        }
    }

    internal void Accepted(Guid attachment, long revision)
    {
        lock (_sync)
        {
            if (attachment != sessionAttachmentId) { Invalidate("Acceptance attachment mismatch."); return; }
            _acceptedRevision = revision;
            Record("DecorationAccepted", revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    internal void ComponentDisposalEntered()
    {
        lock (_sync)
        {
            if (_entryObserved) return;
            // Sample the exact application-owned Task before disposal changes anything.
            _pendingAtEntry = _operation is { } operation ? !operation.IsCompleted : _operationStatus is not null ? false : null;
            _acceptedAtEntry = _expectedRevision is { } revision ? _acceptedRevision == revision : null;
            _entryObserved = true;
            Record("ComponentDisposalEntered", $"accepted={_acceptedAtEntry}; pending={_pendingAtEntry}");
        }
    }

    internal void TrackComponentDisposal(Task disposal)
    {
        lock (_sync)
        {
            // A duplicate Dispose call must not replace the original pending operation.
            if (_disposal is null && _disposalStatus is null) _disposal = disposal;
        }
    }

    internal void Record(string stage, string detail = "", bool failure = false)
    {
        lock (_sync)
        {
            if (_events.Count == Capacity) { _overflow = true; return; }
            _events.Add(new(_events.Count + 1, Stopwatch.GetTimestamp(), stage,
                detail.Length > 2048 ? detail[..2048] + " [truncated]" : detail, failure));
        }
    }

    internal void Failure(string stage, Exception exception) =>
        Record(stage, exception.GetType().FullName ?? exception.GetType().Name, failure: true);

    private void Invalidate(string reason)
    {
        _invalid = true;
        Record("ObservationInvalid", reason);
    }

    internal LifecycleObservationSnapshot Read()
    {
        lock (_sync)
        {
            ReadTask(ref _operation, ref _operationStatus, "OperationCompletionObserved");
            ReadTask(ref _disposal, ref _disposalStatus, "ComponentCompletionObserved");
            return new(_events.ToImmutableArray(), _overflow, _invalid, _expectedRevision,
                _acceptedAtEntry, _pendingAtEntry, _operationStatus ?? _operation?.Status.ToString(),
                _disposalStatus ?? _disposal?.Status.ToString());
        }
    }

    private void ReadTask(ref Task? task, ref string? status, string stage)
    {
        if (task is null || !task.IsCompleted) return;
        status = task.Status.ToString();
        Record(stage, status, task.IsFaulted || task.IsCanceled);
        if (task.Exception is { } exception) Failure(stage + "Failure", exception);
        task = null; // Keep copied results, not completed async state machines.
    }
}

internal sealed record LifecycleObservationEvent(int Sequence, long Timestamp, string Stage, string Detail, bool Failure);
internal sealed record LifecycleObservationSnapshot(ImmutableArray<LifecycleObservationEvent> Events,
    bool Overflow, bool Invalid, long? InputRevision, bool? AcceptedAtEntry, bool? PendingAtEntry,
    string? OperationStatus, string? DisposalStatus);
