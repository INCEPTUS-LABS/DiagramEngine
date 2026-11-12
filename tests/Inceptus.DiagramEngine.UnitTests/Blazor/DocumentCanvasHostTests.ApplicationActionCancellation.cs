using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task Stage4ConcurrentInvocationCancellationAndDismissalWaitForExternalCancellationRegistrations()
    {
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var snapshot = fixture.Facade.GetDocument().Snapshot;
        var providerStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCalls = 0;
        var cancellationCalls = 0;
        fixture.Host.SetApplicationActions(async (_, token) =>
        {
            if (Interlocked.Increment(ref providerCalls) != 2)
                return [new("app:inspect", "Inspect")];
            using var registration = token.Register(() =>
            {
                Interlocked.Increment(ref cancellationCalls);
                cancellationEntered.TrySetResult();
                cancellationRelease.Task.GetAwaiter().GetResult();
                cancellationFinished.TrySetResult();
            });
            providerStarted.SetResult(token);
            // The host must finish cancellation without waiting for uncooperative discovery.
            await providerRelease.Task;
            providerFinished.TrySetResult();
            return [new("app:inspect", "Inspect")];
        }, true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var menu = await ApplicationMenuAsync(fixture.Host);
        using var callerCancellation = new CancellationTokenSource();
        var invocation = fixture.Host.ExecuteApplicationActionAsync(menu.ApplicationContext!.RequestVersion,
            "app:inspect", callerCancellation.Token).AsTask();
        CancellationToken applicationToken = default;
        try
        {
            applicationToken = await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            callerCancellation.Cancel();
            await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(BpmnModelerOperationStatus.Cancelled,
                (await invocation.WaitAsync(TimeSpan.FromSeconds(10))).Status);
            fixture.Host.CloseContextMenu();
            Assert.Null(fixture.Host.CaptureState().ContextMenu);
            Assert.False(cancellationFinished.Task.IsCompleted);
            // A second retirement must not dispose the shared CTS while its first CancelAsync
            // is still executing application registrations. Its public wait handle remains valid.
            Assert.True(applicationToken.WaitHandle.WaitOne(0));
            Assert.Equal(1, Volatile.Read(ref cancellationCalls));
        }
        finally
        {
            cancellationRelease.TrySetResult();
            providerRelease.TrySetResult();
        }
        await cancellationFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await providerFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForApplicationConditionAsync(() => ApplicationCancellationSourceIsDisposed(applicationToken));
        Assert.Empty(fixture.Invocations);
        Assert.Equal(snapshot, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    private static bool ApplicationCancellationSourceIsDisposed(CancellationToken token)
    {
        try { _ = token.WaitHandle; return false; }
        catch (ObjectDisposedException) { return true; }
    }
}
