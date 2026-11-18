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
    [InlineData(EditingSessionAccessMode.Editable, "render-failure")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "render-failure")]
    [InlineData(EditingSessionAccessMode.Editable, "cancelled-before-start")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "cancelled-before-start")]
    [InlineData(EditingSessionAccessMode.Editable, "cancelled-before-commit")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "cancelled-before-commit")]
    public async Task CompositeResourcesAndWholeMessagesSurviveUncommittedSourceReplacement(
        EditingSessionAccessMode mode, string scenario)
    {
        var factory = new BpmnModelerCompositionFactory(initialDocument: A24Snapshot(), accessMode: mode);
        var active = new RecordingRenderExecution();
        var candidate = new RecordingRenderExecution();
        if (scenario == "render-failure")
            candidate.RenderResult = new Canvas2DInteropOperationResult
            { Succeeded = false, Code = "TEST_COMPOSITE_REPLACEMENT_FAILURE" };
        var pointers = new LoadingPointerObserverFactory();
        using var facade = new BpmnModelerFacade(factory, static _ => Task.CompletedTask);
        await using var host = new DocumentCanvasHost(factory, CreateRenderer(active),
            new RecordingSurfaceObserverFactory(new RecordingSurfaceObserver(new(900, 600, 1))), pointers,
            replacementRendererFactory: () => CreateRenderer(candidate));
        facade.Attach(host);
        await host.InitializeAsync("composite-active", "composite-standby", "container");
        var session = Session(host);
        var source = facade.GetDocument().Snapshot!;
        var sourceBytes = NativeDocumentSerializer.Export(source);
        var attachment = facade.CaptureDecorationAttachment()!;
        var svg = CompositeSvg("#123456");
        AssertDecorationPresented(await facade.ReplaceDecorationsAsync(attachment, 7,
            CompositeInput(host, "Retained pair", svg)), 7, true);
        var before = session.CaptureState();
        var frame = active.LastContent!;
        var resources = frame.DecorationResources!;
        var items = CompositeItems(active);
        var uploads = active.FullUploadCount;
        var stamp = facade.CapturePresentation().Stamp;
        var replacementSource = A24Snapshot(position: new(440, 360));
        Assert.NotEqual(source.VisualModel.VisualStates[0].Position, replacementSource.VisualModel.VisualStates[0].Position);

        using var cancellation = new CancellationTokenSource();
        if (scenario == "cancelled-before-start") cancellation.Cancel();
        var replacement = facade.SetDocumentAsync(replacementSource, cancellation.Token).AsTask();
        try
        {
            if (scenario == "cancelled-before-commit")
            {
                await pointers.Candidate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(replacement.IsCompleted);
                Assert.True(candidate.FullUploadCount > 0);
                cancellation.Cancel();
            }
        }
        finally { pointers.Candidate.Release.TrySetResult(); }
        var result = await replacement.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(scenario == "render-failure" ? BpmnModelerOperationStatus.Failed : BpmnModelerOperationStatus.Cancelled,
            result.Status);
        Assert.Equal(stamp, facade.CapturePresentation().Stamp);
        Assert.Same(frame, active.LastContent);
        Assert.Equal(uploads, active.FullUploadCount);
        Assert.Equal(0, active.DisposeCount);
        Assert.Equal(scenario == "cancelled-before-start" ? 0 : 1, candidate.DisposeCount);
        if (scenario == "cancelled-before-commit") Assert.Equal(1, pointers.Candidate.DisposeCount);
        AssertRetained();

        // A fresh native composition and resource-bearing upload prove retained input,
        // independently of pixels or an acknowledged old frame left on the active canvas.
        await host.ZoomInAsync();
        Assert.NotSame(before.CurrentScene, session.CaptureState().CurrentScene);
        Assert.NotSame(frame, active.LastContent);
        Assert.True(active.FullUploadCount > uploads);
        Assert.True(session.CaptureState().DecorationPresentation!.BaseSceneGeneration >
            before.DecorationPresentation!.BaseSceneGeneration);
        AssertRetained();

        var duplicate = await facade.ClearDecorationsAsync(attachment, 7);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected, duplicate.Status);
        Assert.False(duplicate.Accepted);
        AssertRetained();
        AssertDecorationPresented(await facade.ReplaceDecorationsAsync(attachment, 8,
            CompositeInput(host, "Current pair", CompositeSvg("#abcdef"))), 8, true);
        Assert.Equal("Current pair", CompositeItem(active, "combined:text").Content);
        Assert.NotEqual(resources.Identity, active.LastContent!.DecorationResources!.Identity);
        Assert.Equal(attachment, facade.CaptureDecorationAttachment());
        Assert.Same(source, facade.GetDocument().Snapshot);

        void AssertRetained()
        {
            Assert.Same(session, Session(host));
            Assert.Same(source, facade.GetDocument().Snapshot);
            Assert.True(sourceBytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(facade.GetDocument().Snapshot!).AsSpan()));
            Assert.Equal(attachment, facade.CaptureDecorationAttachment());
            var current = facade.CaptureEditorContext()!.Presentation;
            Assert.Equal(mode, current.AccessMode);
            Assert.Equal(EditingSessionStatus.Ready, current.Status);
            Assert.Equal(before.HistoryStatus, current.HistoryStatus);
            Assert.Equal(7, current.DecorationInputRevision);
            Assert.Equal(7, current.DecorationPresentation!.InputRevision);
            Assert.True(current.DecorationPresentation.FramePresented);
            Assert.True(current.DecorationPresentation.DecorationsIncluded);
            Assert.Empty(current.DecorationDiagnostics);
            Assert.Equal(items.Select(item => item.Id), CompositeItems(active).Select(item => item.Id));
            Assert.Equal("Retained pair", CompositeItem(active, "combined:text").Content);
            Assert.Equal(resources.Identity, active.LastContent!.DecorationResources!.Identity);
            Assert.Equal(resources.Images, active.LastContent.DecorationResources.Images);
            Assert.Equal(svg, Assert.Single(active.LastContent.DecorationResources.Images,
                image => image.Reference == "test:combined").SvgContent);
        }
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task CompositePendingResourceFrameIsRetiredByNormalHostDisposal(EditingSessionAccessMode mode)
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(900, 600, 1)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: A24Snapshot(), accessMode: mode));
        await host.InitializeAsync("composite-disposal", "container");
        var attachment = host.CaptureDecorationAttachment()!;
        var input = CompositeInput(host, "Pending pair", CompositeSvg("#123456"));
        execution.BlockRender = true;
        var operation = host.ReplaceDecorationsAsync(attachment, 1, input).AsTask();
        Task? disposal = null;
        try
        {
            await execution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(operation.IsCompleted);
            Assert.Equal(1, Session(host).CaptureState().DecorationInputRevision);
            Assert.NotNull(execution.LastContent!.DecorationResources);
            Assert.Equal(4, CompositeItems(execution).Length);
            disposal = host.DisposeAsync().AsTask();
            Assert.Null(host.CaptureDecorationAttachment());
            Assert.False(disposal.IsCompleted);
        }
        finally { execution.ReleaseRender(); }
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(10));
        await disposal!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Accepted);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Unavailable, result.Status);
        Assert.False(result.Presentation?.FramePresented ?? false);
        Assert.Equal(1, execution.DisposeCount);
        Assert.True(Session(host).CaptureState().IsClosed);
        var afterDisposal = await host.ReplaceDecorationsAsync(attachment, 2, input);
        Assert.False(afterDisposal.Accepted);
        Assert.NotEqual(Canvas2DDecorationUpdateStatus.Presented, afterDisposal.Status);
        Assert.Equal(2, execution.FullUploadCount);
    }
}
