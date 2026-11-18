using Inceptus.DiagramEngine.Canvas2D.EditingSession;

namespace Inceptus.DiagramEngine.UnitTests.Canvas2D;

public sealed class LifecycleObservationTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void EntrySamplesTheRegisteredTaskAndPriorAcceptance(bool accepted, bool completedBeforeEntry)
    {
        var attachment = Guid.NewGuid();
        var receipt = new LifecycleObservation(attachment);
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        receipt.ExpectOperation(7);
        if (accepted) receipt.Accepted(attachment, 7); // Can precede Task registration on a synchronous path.
        receipt.TrackOperation(attachment, 7, operation.Task);
        if (completedBeforeEntry) operation.SetResult();
        _ = receipt.Read(); // Terminal readback must not lose the entry-time completed fact.
        receipt.ComponentDisposalEntered();
        var entered = receipt.Read();
        Assert.Equal(accepted, entered.AcceptedAtEntry);
        Assert.Equal(!completedBeforeEntry, entered.PendingAtEntry);
        operation.TrySetResult();
        receipt.Accepted(attachment, 7); // Later acceptance cannot rewrite the earlier sample.
        var after = receipt.Read();
        Assert.Equal(entered.AcceptedAtEntry, after.AcceptedAtEntry);
        Assert.Equal(entered.PendingAtEntry, after.PendingAtEntry);
        Assert.False(after.Invalid);
    }

    [Theory]
    [InlineData("attachment")]
    [InlineData("revision")]
    [InlineData("duplicate-task")]
    [InlineData("duplicate-after-read")]
    [InlineData("late-task")]
    public void MismatchedOrLateRegistrationInvalidatesEvidence(string mismatch)
    {
        var attachment = Guid.NewGuid();
        var receipt = new LifecycleObservation(attachment);
        receipt.ExpectOperation(1);
        if (mismatch == "late-task") receipt.ComponentDisposalEntered();
        receipt.TrackOperation(mismatch == "attachment" ? Guid.NewGuid() : attachment,
            mismatch == "revision" ? 2 : 1, Task.CompletedTask);
        if (mismatch == "duplicate-task") receipt.TrackOperation(attachment, 1, Task.CompletedTask);
        if (mismatch == "duplicate-after-read")
        {
            _ = receipt.Read();
            receipt.TrackOperation(attachment, 1, Task.CompletedTask);
        }
        Assert.True(receipt.Read().Invalid);
    }

    [Fact]
    public void EndMarkersAndDuplicateDisposeDoNotReplaceActualPendingDisposal()
    {
        var receipt = new LifecycleObservation(Guid.NewGuid());
        var disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        receipt.ComponentDisposalEntered();
        receipt.TrackComponentDisposal(disposal.Task);
        receipt.Record("ComponentLifetimeDisposed");
        receipt.TrackComponentDisposal(Task.CompletedTask);
        Assert.NotEqual(nameof(TaskStatus.RanToCompletion), receipt.Read().DisposalStatus);
        disposal.SetResult();
        var finished = receipt.Read();
        Assert.Equal(nameof(TaskStatus.RanToCompletion), finished.DisposalStatus);
        Assert.Single(finished.Events, e => e.Stage == "ComponentCompletionObserved");
        Assert.Equal(finished.Events.ToArray(), receipt.Read().Events.ToArray());
    }

    [Fact]
    public void FailuresOverflowAndIndependentReceiptsRemainExplicit()
    {
        var receipt = new LifecycleObservation(Guid.NewGuid());
        var independent = new LifecycleObservation(Guid.NewGuid());
        receipt.Failure("FirstCleanupFailure", new InvalidOperationException("private detail"));
        receipt.Failure("SecondCleanupFailure", new ArgumentException("another detail"));
        receipt.TrackComponentDisposal(Task.FromException(new InvalidOperationException("terminal")));
        var snapshot = receipt.Read();
        Assert.Equal(nameof(TaskStatus.Faulted), snapshot.DisposalStatus);
        Assert.Equal(4, snapshot.Events.Count(e => e.Failure));
        Assert.DoesNotContain(snapshot.Events, e => e.Detail.Contains("private detail", StringComparison.Ordinal));
        for (var index = 0; index < 130; index++) receipt.Record("RepeatedObservation");
        Assert.True(receipt.Read().Overflow);
        Assert.Equal(128, receipt.Read().Events.Length);
        Assert.Empty(independent.Read().Events);
    }
}
