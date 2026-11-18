using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.UnitTests.Canvas2D;

public sealed class EditingSessionDecorationTests
{
    private static readonly Canvas2DSceneContributorId ProviderId = new("test:transient-annotations");

    [Fact]
    public async Task RevisionUpdatesAndClearRenderWithoutAnyPipelineOrDocumentChange()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var snapshot = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attached = await EditingSession.AttachAsync(document, renderer, EditingSessionTestHarness.Configuration(), pipeline);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        var before = session.CaptureState();
        var nativeFrame = execution.LastFrame!;

        foreach (var revision in new long[] { 1, 2, 3 })
        {
            var result = await session.ReplaceDecorationsAsync(revision, Input($"annotation-{revision}"));
            Assert.Equal(Canvas2DDecorationUpdateStatus.Presented, result.Status);
            Assert.True(result.Accepted);
            Assert.True(result.Presentation!.FramePresented);
            Assert.True(result.Presentation.DecorationsIncluded);
            Assert.Equal(revision, result.Presentation.InputRevision);
            Assert.Contains(execution.LastFrame!.Items, item => item.Content == $"annotation-{revision}");
            Assert.Equal(nativeFrame.Items.Length + 2, execution.LastFrame.Items.Length);
        }
        var clear = await session.ReplaceDecorationsAsync(4, null);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Presented, clear.Status);
        Assert.False(clear.Presentation!.DecorationsIncluded);
        Assert.Equal(nativeFrame.Items.Select(item => item.Id), execution.LastFrame!.Items.Select(item => item.Id));
        Assert.Equal(1, pipeline.DocumentRunCount);
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.Equal(snapshot, document.CaptureSnapshot());
        var after = session.CaptureState();
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(before.Generation, after.Generation);
    }

    [Fact]
    public async Task NonNewerAndCancelledInputsPreserveAcceptedFrameAndFrozenValues()
    {
        var (session, execution, _) = await AttachAsync();
        await using var owned = session;
        var values = new Dictionary<string, PropertyValue> { ["text"] = PropertyValue.FromText("frozen") };
        var frozen = new Canvas2DDecorationInput(ProviderId, new AnnotationProvider(), values);
        values["text"] = PropertyValue.FromText("mutated");
        await session.ReplaceDecorationsAsync(7, frozen);
        var frame = execution.LastFrame;
        foreach (var revision in new long[] { -1, 0, 6, 7 })
        {
            var rejected = await session.ReplaceDecorationsAsync(revision, Input("stale"));
            Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected, rejected.Status);
            Assert.False(rejected.Accepted);
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await session.ReplaceDecorationsAsync(8, null, cancellation.Token);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Cancelled, cancelled.Status);
        Assert.False(cancelled.Accepted);
        Assert.Same(frame, execution.LastFrame);
        Assert.Contains(frame!.Items, item => item.Content == "frozen");
        Assert.Equal(7, session.CaptureState().DecorationInputRevision);
    }

    [Fact]
    public async Task FailedNewProviderRetiresPriorValuesAndAcknowledgesNativeOnlyFallback()
    {
        var (session, execution, _) = await AttachAsync();
        await using var owned = session;
        await session.ReplaceDecorationsAsync(1, Input("old"));
        var failure = await session.ReplaceDecorationsAsync(2, new(ProviderId, new ThrowingProvider()));
        Assert.Equal(Canvas2DDecorationUpdateStatus.Failed, failure.Status);
        Assert.True(failure.Accepted);
        Assert.True(failure.Presentation!.FramePresented);
        Assert.False(failure.Presentation.DecorationsIncluded);
        Assert.NotEmpty(failure.Diagnostics);
        Assert.DoesNotContain(execution.LastFrame!.Items, item => item.Content == "old");
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
        Assert.Empty(session.CaptureState().RuntimeDiagnostics);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected,
            (await session.ReplaceDecorationsAsync(2, Input("retry"))).Status);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Presented,
            (await session.ReplaceDecorationsAsync(3, Input("retry"))).Status);
    }

    [Fact]
    public async Task GraphicsFailureNeverClaimsPresentationOrRollback()
    {
        var (session, execution, _) = await AttachAsync();
        await using var owned = session;
        execution.RenderException = new InvalidOperationException("graphics unavailable");
        var result = await session.ReplaceDecorationsAsync(1, Input("new"));
        Assert.Equal(Canvas2DDecorationUpdateStatus.Failed, result.Status);
        Assert.True(result.Accepted);
        Assert.False(result.Presentation!.FramePresented);
        Assert.False(session.CaptureState().IsCurrentScenePresented);
        Assert.Equal(1, session.CaptureState().DecorationInputRevision);
        execution.RenderException = null;
        Assert.True((await session.RenderCurrentAsync()).Succeeded);
        Assert.True(session.CaptureState().DecorationPresentation!.FramePresented);
    }

    [Fact]
    public async Task RenderAcknowledgementIsRequiredAndPostAcceptanceCancellationIsNotRollback()
    {
        var (session, execution, _) = await AttachAsync();
        await using var owned = session;
        execution.RenderEnteredSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        execution.RenderReleaseSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var update = session.ReplaceDecorationsAsync(1, Input("accepted"), cancellation.Token).AsTask();
        await execution.RenderEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(update.IsCompleted);
        Assert.False(session.CaptureState().IsCurrentScenePresented);
        Assert.False(session.CaptureState().DecorationPresentation!.FramePresented);
        var overlappingClear = await session.ReplaceDecorationsAsync(2, null);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Unavailable, overlappingClear.Status);
        Assert.False(overlappingClear.Accepted);
        cancellation.Cancel();
        execution.RenderReleaseSignal.SetResult();
        var result = await update;
        Assert.True(result.Accepted);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Presented, result.Status);
        Assert.True(result.Presentation!.FramePresented);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Presented, (await session.ReplaceDecorationsAsync(2, null)).Status);
    }

    [Fact]
    public async Task PanZoomAndDprRecomposeOnlyFromNativeBaseAndRetainLatestValues()
    {
        var (session, execution, pipeline) = await AttachAsync();
        await using var owned = session;
        await session.ReplaceDecorationsAsync(1, Input("old"));
        await session.ReplaceDecorationsAsync(2, Input("latest"));
        var nativeCount = session.CaptureState().CurrentScene!.Items.Length;
        for (var i = 0; i < 3; i++)
            pipeline.EnqueueScene((artifacts, visual, editor, _) => ValueTask.FromResult(
                ControlledEditingSessionPipeline.Success(artifacts, visual, editor)));
        await session.PanViewportAsync(new VectorD(-20, -10));
        var pan = session.CaptureState().EditorState.Viewport.Pan;
        await session.UpdateViewportAsync(new ViewportSnapshot(1.5, pan));
        Assert.True((await session.ResizeAsync(new Canvas2DSurfaceSize(800, 600, 2))).Succeeded);
        Assert.Equal(1.5, session.CaptureState().EditorState.Viewport.Zoom);
        Assert.Equal(pan, session.CaptureState().EditorState.Viewport.Pan);
        Assert.Contains(execution.LastFrame!.Items, item => item.Content == "latest");
        Assert.DoesNotContain(execution.LastFrame.Items, item => item.Content == "old");
        Assert.Equal(nativeCount + 2, execution.LastFrame.Items.Length);
        Assert.Equal(2, session.CaptureState().DecorationPresentation!.InputRevision);
        Assert.True(session.CaptureState().DecorationPresentation!.FramePresented);
        Assert.Equal(1, pipeline.DocumentRunCount);
    }

    [Fact]
    public async Task NativeChangeDuringDelayedRenderDoesNotAcknowledgeObsoleteFrame()
    {
        var (session, execution, pipeline) = await AttachAsync();
        await using var owned = session;
        execution.RenderEnteredSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        execution.RenderReleaseSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var update = session.ReplaceDecorationsAsync(1, Input("current")).AsTask();
        await execution.RenderEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pipeline.EnqueueScene((artifacts, visual, editor, _) => ValueTask.FromResult(
            ControlledEditingSessionPipeline.Success(artifacts, visual, editor)));
        var pan = session.PanViewportAsync(new VectorD(-20, -10)).AsTask();
        execution.RenderReleaseSignal.SetResult();
        var obsolete = await update;
        Assert.True(obsolete.Accepted);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Unavailable, obsolete.Status);
        Assert.False(obsolete.Presentation?.FramePresented ?? false);
        Assert.True((await pan).Succeeded);
        Assert.True(session.CaptureState().DecorationPresentation!.FramePresented);
        Assert.Contains(execution.LastFrame!.Items, item => item.Content == "current");
    }

    [Fact]
    public async Task DisposalDuringProviderRetiresLateOutputAndClosedUpdatesAreUnavailable()
    {
        var (session, execution, _) = await AttachAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var provider = new BlockingProvider(entered, release);
        var update = Task.Run(async () => await session.ReplaceDecorationsAsync(1, new(ProviderId, provider)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var before = execution.Calls.Count;
        var close = session.CloseAsync().AsTask();
        release.Set();
        var result = await update.WaitAsync(TimeSpan.FromSeconds(5));
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Accepted);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Unavailable, result.Status);
        Assert.Equal(before + 1, execution.Calls.Count); // Only renderer disposal, no late frame.
        Assert.True(execution.IsDisposed);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Unavailable,
            (await session.ReplaceDecorationsAsync(2, null)).Status);
    }

    private static Canvas2DDecorationInput Input(string text) =>
        new(ProviderId, new AnnotationProvider(), [new("text", PropertyValue.FromText(text))]);

    private static async Task<(EditingSession Session, Canvas2DRendererTestExecution Execution,
        ControlledEditingSessionPipeline Pipeline)> AttachAsync()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attached = await EditingSession.AttachAsync(EditingSessionTestHarness.CreateDocument(inputs), renderer,
            EditingSessionTestHarness.Configuration(), pipeline);
        return (Assert.IsType<EditingSession>(attached.Session), execution, pipeline);
    }

    private sealed class AnnotationProvider : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input) =>
            new(context.Parts.Where(part => part.Kind == Canvas2DDecorationPartKind.NodeBody).Select(part =>
            {
                var key = part.Item.Id.Value;
                return new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForExtension(ProviderId, key),
                    Canvas2DSceneLayer.Decoration, 0,
                    Canvas2DSceneGeometry.Text(new RectD(part.Item.Bounds.Left, part.Item.Bounds.Bottom + 8, 120, 18),
                        input["text"].TextValue), part.CreateOrigin(key),
                    style: new Canvas2DSceneStyle(fill: "#112233", fontFamily: context.DefaultFontFamily));
            }));
    }

    private sealed class ThrowingProvider : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input) =>
            throw new InvalidOperationException("provider failure");
    }

    private sealed class BlockingProvider(ManualResetEventSlim entered, ManualResetEventSlim release) : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return new();
        }
    }
}
