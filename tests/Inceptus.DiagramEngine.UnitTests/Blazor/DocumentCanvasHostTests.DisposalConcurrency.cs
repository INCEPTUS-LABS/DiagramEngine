using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task DisposeAllowsConcurrentHostReadbackAndSharesCompletionDuringSynchronousCleanup(EditingSessionAccessMode mode)
    {
        var prepared = await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync();
        var source = (await prepared.CreateAsync()).Document.CaptureSnapshot();
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new(900, 600, 1));
        await using var host = CreateHost(execution, observer,
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: source, accessMode: mode));
        await host.InitializeAsync("active", "standby", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var accepted = document.CaptureSnapshot();
        var bytes = NativeDocumentSerializer.Export(accepted);
        var history = session.CaptureState().HistoryStatus;
        Task<(DocumentCanvasHostState State, Task Completion)>? readback = null;
        var readbackCompletedDuringCleanup = false;
        observer.Disposing = () =>
        {
            // A synchronous cleanup callback must not own the host lock: an already
            // executing session notification may need that lock before Close can retire it.
            // A dedicated worker keeps this probe independent of thread-pool starvation.
            readback = Task.Factory.StartNew(() => (host.CaptureState(), host.DisposeAsync().AsTask()),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            readbackCompletedDuringCleanup = readback.Wait(TimeSpan.FromSeconds(5));
        };

        var disposal = host.DisposeAsync().AsTask();
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));
        var observed = await Assert.IsAssignableFrom<Task<(DocumentCanvasHostState, Task)>>(readback)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(readbackCompletedDuringCleanup, "Host readback was blocked by the synchronous cleanup lock.");
        Assert.True(observed.Item1.IsDisposed);
        Assert.Same(disposal, observed.Item2);
        Assert.Same(disposal, host.DisposeAsync().AsTask());
        Assert.True(session.CaptureState().IsClosed);
        Assert.Equal(mode, session.CaptureState().AccessMode);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.Same(accepted, document.CaptureSnapshot());
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(document.CaptureSnapshot()).AsSpan()));
        Assert.Equal(1, observer.DisposeCount);
        Assert.Equal(1, PointerObserver(host).DisposeCount);
        Assert.Equal(1, execution.DisposeCount);
        Assert.Empty(host.CaptureState().HostDiagnostics);
    }
}
