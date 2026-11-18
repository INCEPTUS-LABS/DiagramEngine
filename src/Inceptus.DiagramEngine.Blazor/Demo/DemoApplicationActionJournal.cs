using System.Diagnostics;
using System.Globalization;
using Inceptus.DiagramEngine.Bpmn.Blazor;

namespace Inceptus.DiagramEngine.Blazor.Demo;

/// <summary>Bounded application observations, never an engine dispatch or disposal acknowledgement.</summary>
internal sealed class DemoApplicationActionJournal
{
    private readonly object _sync = new();
    private readonly List<Attempt> _attempts = [];
    private readonly List<DemoActionObservation> _events = [];
    private long _nextAttempt;
    private long _sequence;
    private bool _overflow;
    internal const int MaximumAttempts = 64;
    internal const int MaximumEvents = 256;

    internal Attempt Begin(string kind, BpmnApplicationActionContext context, string? actionId,
        long generation, CancellationToken token, CancellationToken pageToken)
    {
        Attempt attempt;
        lock (_sync)
        {
            var stamp = context.Stamp;
            var identity = $"{kind}; mount={generation}; request={context.RequestVersion}; application={context.ApplicationActionsRevision}; " +
                $"instance={stamp.InstanceId}; attachment={stamp.AttachmentVersion}; document={stamp.DocumentId.Value}/{stamp.Revision.Value}; " +
                $"view={stamp.PresentationContext}; context={stamp.ContextVersion}; selection={stamp.SelectionVersion}; " +
                $"owner={context.Target.Kind}:{context.Target.SemanticId.Value}; visual={context.Target.VisualStateId?.Value ?? "none"}; " +
                $"part={context.Part?.Kind.ToString() ?? "unknown"}:{context.Part?.SceneObjectId.Value ?? "none"}; action={actionId ?? "none"}";
            if (identity.Length > 4096) { identity = identity[..4096]; _overflow = true; }
            attempt = new(this, ++_nextAttempt, identity, token, pageToken);
            if (_attempts.Count < MaximumAttempts) _attempts.Add(attempt);
            else _overflow = true;
            RecordUnderLock(attempt, "Entry", "Provider ordinals are local attempts, not a public discovery/recheck phase.");
        }
        attempt.RequestRegistration = token.Register(() => attempt.Record("RequestTokenObserved", "Supplied token cancellation observed; not the exact retirement time."));
        attempt.PageRegistration = pageToken.Register(() => attempt.Record("PageTokenObserved", "Application page cancellation observed separately."));
        return attempt;
    }

    internal DemoActionJournalSnapshot Read()
    {
        lock (_sync)
        {
            return new(_overflow, Stopwatch.Frequency,
                _attempts.Select(attempt => new DemoActionAttemptSnapshot(attempt.Id, attempt.Identity,
                    State(attempt.Consumer, attempt.ConsumerTerminal), State(attempt.Io, attempt.IoTerminal))).ToArray(),
                _events.ToArray());
        }
    }

    private static string State(Task? task, string? terminal) => task is null ? terminal ?? "Unknown" : task.Status.ToString();

    private void RecordUnderLock(Attempt attempt, string name, string detail)
    {
        if (_events.Count == MaximumEvents || !_attempts.Contains(attempt)) { _overflow = true; return; }
        _events.Add(new(++_sequence, Stopwatch.GetTimestamp(), attempt.Id, name,
            State(attempt.Consumer, attempt.ConsumerTerminal), attempt.Consumer is { IsCompleted: false },
            State(attempt.Io, attempt.IoTerminal), attempt.Io is { IsCompleted: false },
            attempt.Token.IsCancellationRequested, attempt.PageToken.IsCancellationRequested,
            detail.Length > 512 ? detail[..512] : detail));
    }

    internal sealed class Attempt(DemoApplicationActionJournal journal, long id, string identity,
        CancellationToken token, CancellationToken pageToken)
    {
        internal long Id { get; } = id;
        internal string Identity { get; } = identity;
        internal CancellationToken Token { get; private set; } = token;
        internal CancellationToken PageToken { get; private set; } = pageToken;
        internal CancellationTokenRegistration RequestRegistration { get; set; }
        internal CancellationTokenRegistration PageRegistration { get; set; }
        internal Task? Consumer { get; private set; }
        internal Task? Io { get; private set; }
        internal string? ConsumerTerminal { get; private set; }
        internal string? IoTerminal { get; private set; }

        internal void Record(string name, string detail = "")
        {
            lock (journal._sync) journal.RecordUnderLock(this, name, detail);
        }

        internal void TrackIo(Task task)
        {
            lock (journal._sync) { Io = task; journal.RecordUnderLock(this, "IoTaskCaptured", "Exact HTTP/deserialization Task sampled after creation."); }
        }

        internal Task<T> TrackConsumer<T>(Task<T> task)
        {
            TrackConsumerTask(task);
            return task;
        }

        internal Task TrackConsumerTask(Task task)
        {
            lock (journal._sync) { Consumer = task; journal.RecordUnderLock(this, "ConsumerTaskCaptured", "Exact Task returned to the modeler, not an observer Task."); }
            _ = ObserveCompletionAsync(task);
            return task;
        }

        private async Task ObserveCompletionAsync(Task task)
        {
#pragma warning disable CA1031 // Observe without changing the original Task's outcome or hiding its failure from the modeler.
            try { await task.ConfigureAwait(false); }
            catch (Exception exception) { Record("ConsumerExceptionObserved", exception.GetType().Name + ": " + exception.Message); }
#pragma warning restore CA1031
            finally
            {
                lock (journal._sync)
                {
                    ConsumerTerminal = task.Status.ToString();
                    IoTerminal = Io?.Status.ToString();
                    journal.RecordUnderLock(this, "ConsumerCompletionObserved", "Terminal state sampled after await; not the exact completion time or engine acceptance.");
                    Consumer = null;
                    Io = null;
                }
                // Do not dispose registrations inside the journal lock: cancellation may be recording an event.
                RequestRegistration.Dispose();
                PageRegistration.Dispose();
                lock (journal._sync)
                {
                    RequestRegistration = default;
                    PageRegistration = default;
                    Token = default;
                    PageToken = default;
                }
            }
        }
    }
}

internal sealed record DemoActionAttemptSnapshot(long Id, string Identity, string ConsumerState, string IoState);
internal sealed record DemoActionObservation(long Sequence, long Timestamp, long AttemptId, string Event,
    string ConsumerState, bool ConsumerIncomplete, string IoState, bool IoIncomplete,
    bool RequestCancelled, bool PageCancelled, string Detail);
internal sealed record DemoActionJournalSnapshot(bool Overflow, long TimestampFrequency,
    DemoActionAttemptSnapshot[] Attempts, DemoActionObservation[] Events)
{
    public override string ToString() =>
        $"Overflow={Overflow}; timestamp frequency={TimestampFrequency}; readback is sampled state, not engine acknowledgement.\n" +
        string.Join('\n', Attempts.Select(attempt => $"Attempt {attempt.Id}: {attempt.Identity}; consumer={attempt.ConsumerState}; io={attempt.IoState}")) + "\n" +
        string.Join('\n', Events.Select(item => string.Create(CultureInfo.InvariantCulture,
            $"{item.Sequence} ticks={item.Timestamp} attempt={item.AttemptId} {item.Event}: consumer={item.ConsumerState} incomplete={item.ConsumerIncomplete}; io={item.IoState} incomplete={item.IoIncomplete}; requestCancelled={item.RequestCancelled}; pageCancelled={item.PageCancelled}; {item.Detail}")));
}
