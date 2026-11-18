using System.Reflection;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false)]
    [InlineData(EditingSessionAccessMode.Editable, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true)]
    public async Task LifecycleReceiptUsesCompiledDemoControlsAndActualChildTask(EditingSessionAccessMode mode, bool pending)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var modeler = fixture.Renderer.Modeler;
        var source = modeler.GetDocument().Snapshot!;
        var before = NativeDocumentSerializer.Export(source);
        var session = Session(fixture.Host);
        var history = session.CaptureState().HistoryStatus;
        // Reuse the canonical host execution seam; only managed tests control rendering.
        // The browser scenarios use ordinary Blazor conditional removal with no delays.
        var canvas = new DocumentCanvas();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(DocumentCanvas).GetField("_host", flags)!.SetValue(canvas, fixture.Host);
        typeof(InceptusBpmnModeler).GetField("_canvas", flags)!.SetValue(modeler, canvas);
        Task? disposal = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (control, id) = await fixture.Renderer.RenderVerificationControlsAsync(false,
            EventCallback.Factory.Create(new object(), () =>
            {
                modeler.Dispose();
                disposal = canvas.DisposeAsync().AsTask();
                entered.SetResult();
            }));
        await fixture.Renderer.EnableVerificationControlsAsync(id);
        fixture.Execution.BlockRender = pending;
        var update = fixture.Renderer.ClickVerificationAsync(id, "update-unmount");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Renderer.ClickVerificationAsync(id, "read-lifecycle");
            var entry = Assert.IsType<Inceptus.DiagramEngine.Bpmn.Blazor.Presentation.BpmnLifecycleSnapshot>(control.LifecycleSnapshot);
            Assert.True(entry.AcceptedAtEntry);
            Assert.Equal(pending, entry.PendingAtEntry);
            Assert.Equal(pending, entry.AcceptedPendingAtEntryObserved);
            Assert.Equal(1, entry.InputRevision);
            Assert.Contains(entry.Events, e => e.Stage == "DecorationAccepted");
            if (pending)
            {
                Assert.False(disposal!.IsCompleted);
                Assert.False(entry.CompletionChainObserved);
                Assert.DoesNotContain(entry.Events, e => e.Stage == "RendererDisposed");
                await canvas.DisposeAsync(); // Duplicate must not overwrite original completion.
            }
        }
        finally { fixture.Execution.ReleaseRender(); }
        await update.WaitAsync(TimeSpan.FromSeconds(10));
        await disposal!.WaitAsync(TimeSpan.FromSeconds(10));
        await fixture.Renderer.ClickVerificationAsync(id, "read-lifecycle");
        var finished = control.LifecycleSnapshot!;
        Assert.Equal(nameof(TaskStatus.RanToCompletion), finished.DisposalStatus);
        Assert.Contains(finished.Events, e => e.Stage == "RendererDisposed");
        Assert.Contains(finished.Events, e => e.Stage == "SessionDisposed");
        Assert.Contains(finished.Events, e => e.Stage == "HostDisposed");
        Assert.Contains(finished.Events, e => e.Stage == "ComponentLifetimeDisposed");
        Assert.DoesNotContain(finished.Events, e => e.Failure);
        // These canonical mock observers do not establish real browser callback retirement.
        Assert.False(finished.CompletionChainObserved);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.True(before.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(source).AsSpan()));
        Assert.Equal(mode, session.CaptureState().AccessMode);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Fact]
    public async Task LifecycleReceiptKeepsEarlierHostFailuresWhenLaterDiagnosticsReplaceThem()
    {
        var observer = new RecordingSurfaceObserver(new(640, 360, 1))
        { DisposeException = new ArgumentException("surface") };
        await using var host = CreateHost(new RecordingRenderExecution(), observer);
        await host.InitializeAsync("canvas", "container");
        PointerObserver(host).DisposeException = new InvalidOperationException("pointer");
        var receipt = Assert.IsType<Inceptus.DiagramEngine.Bpmn.Blazor.Presentation.BpmnLifecycleObservation>(host.BeginLifecycleObservation());
        await host.DisposeAsync();
        var failures = receipt.Read().Events.Where(e => e.Stage == "HostCleanupFailure").ToArray();
        Assert.Equal(2, failures.Length);
        Assert.All(failures, e => Assert.True(e.Failure));
        Assert.Contains("InvalidOperationException", failures[0].Detail, StringComparison.Ordinal);
        Assert.Contains("ArgumentException", failures[1].Detail, StringComparison.Ordinal);
        Assert.Contains(receipt.Read().Events, e => e.Stage == "SessionDisposed");
        Assert.False(receipt.Read().CompletionChainObserved);
    }

    [Fact]
    public async Task LifecycleObservationIsOptInAndCannotArmAnUnavailableModeler()
    {
        var modeler = new InceptusBpmnModeler();
        Assert.Null(modeler.BeginLifecycleObservation());
        modeler.Dispose();
        Assert.Null(modeler.BeginLifecycleObservation());
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        Assert.Null(Session(fixture.Host).LifecycleObservation);
    }
}
