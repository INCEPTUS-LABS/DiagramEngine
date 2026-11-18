using System.Reflection;
using Inceptus.Bpmn.Integration;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Export;
using Inceptus.DiagramEngine.Canvas2D.Interaction;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Canvas2D.Rendering.Interop;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Contracts.Presentation;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Profiles;
using Inceptus.DiagramEngine.Organizational.Commands;
using Inceptus.DiagramEngine.Organizational.Profiles;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private static readonly Canvas2DSceneContributorId DecorationTestProviderId = new("test:host-decorations");
    private static readonly string DecorationTestItemId =
        Canvas2DSceneObjectIdentity.ForExtension(DecorationTestProviderId, "annotation").Value;

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task DecorationsRefreshAndClearThroughPublicComponentWithoutNativeEdits(EditingSessionAccessMode mode)
    {
        using var services = new ServiceCollection().AddSingleton<IJSRuntime>(new SiteToolbarJsRuntime())
            .AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        await using var renderer = new ApplicationComponentRenderer(services);
        await renderer.InitializeAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(InceptusBpmnModeler.InitialDocument)] = A24Snapshot(),
            [nameof(InceptusBpmnModeler.AccessMode)] = mode,
        }));
        var component = renderer.Modeler;
        var facade = Assert.IsType<BpmnModelerFacade>(typeof(InceptusBpmnModeler)
            .GetField("_facade", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
        Assert.Null(component.CaptureDecorationAttachment());
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: facade.CompositionFactory);
        facade.Attach(host);
        await host.InitializeAsync("decoration-component", "decoration-container");
        var session = Session(host);
        var before = session.CaptureState();
        var document = component.CaptureDocumentSnapshot().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(document);
        var attachment = Assert.IsType<BpmnDecorationAttachment>(component.CaptureDecorationAttachment());
        var originalInput = DecorationInput(host, "#123456");

        var initial = await component.ReplaceDecorationsAsync(attachment, 1, originalInput);
        AssertDecorationPresented(initial, 1, included: true);
        Assert.Equal("#123456", DecorationFrameItem(execution).Fill);
        var second = await component.ReplaceDecorationsAsync(attachment, 2, DecorationInput(host, "#abcdef"));
        AssertDecorationPresented(second, 2, included: true);
        Assert.Equal("#abcdef", DecorationFrameItem(execution).Fill);
        Assert.Equal(3, execution.FullUploadCount);
        Assert.Same(before.CurrentScene, session.CaptureState().CurrentScene);
        Assert.Same(before.ProjectedGraph, session.CaptureState().ProjectedGraph);
        Assert.Same(before.LayoutResult, session.CaptureState().LayoutResult);
        Assert.Same(before.RoutingResult, session.CaptureState().RoutingResult);
        Assert.Same(document, component.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(before.EditorState, session.CaptureState().EditorState);
        Assert.DoesNotContain(session.CaptureState().CurrentScene!.Items, item => item.Id.Value == DecorationTestItemId);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(document).AsSpan()));

        var clear = await component.ClearDecorationsAsync(attachment, 3);
        AssertDecorationPresented(clear, 3, included: false);
        Assert.DoesNotContain(execution.LastContent!.Items, item => item.Id == DecorationTestItemId);
        var stale = await component.ReplaceDecorationsAsync(attachment, 2, originalInput);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected, stale.Status);
        Assert.False(stale.Accepted);
        Assert.Equal(4, execution.FullUploadCount);
        Assert.Empty(renderer.Exceptions);
        component.Dispose();
        Assert.Null(component.CaptureDecorationAttachment());
        Assert.Equal(Canvas2DDecorationUpdateStatus.Unavailable,
            (await component.ClearDecorationsAsync(attachment, 4)).Status);
    }

    [Fact]
    public async Task DecorationsOnlyUpdatesReuseNativePipelineAndFrozenInput()
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)));
        await host.InitializeAsync("decoration-counters", "decoration-container");
        var attachment = host.CaptureDecorationAttachment()!;
        var counters = host.CaptureState().PipelineCounters!;
        var before = (counters.ProjectionRuleInvocationCount, counters.LayoutInvocationCount,
            counters.RoutingInvocationCount, counters.SceneContributionInvocationCount);
        var input = DecorationInput(host, "#123456");
        var values = input.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        var frozen = new Canvas2DDecorationInput(DecorationTestProviderId, new HostDecorationProvider(), values);
        values["fill"] = PropertyValue.FromText("#abcdef");
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 1, frozen), 1, true);
        Assert.Equal("#123456", DecorationFrameItem(execution).Fill);
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 2, input), 2, true);
        AssertDecorationPresented(await host.ClearDecorationsAsync(attachment, 3), 3, false);
        Assert.Equal(before, (counters.ProjectionRuleInvocationCount, counters.LayoutInvocationCount,
            counters.RoutingInvocationCount, counters.SceneContributionInvocationCount));
    }

    [Fact]
    public async Task DecorationsRequireRendererAcknowledgmentAndBoundConcurrentAdmission()
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)));
        await host.InitializeAsync("decoration-delayed", "decoration-container");
        var attachment = host.CaptureDecorationAttachment()!;
        execution.BlockRender = true;
        using var cancellation = new CancellationTokenSource();
        var update = host.ReplaceDecorationsAsync(attachment, 1, DecorationInput(host, "#123456"), cancellation.Token).AsTask();
        try
        {
            await execution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(update.IsCompleted);
            var busy = await host.ClearDecorationsAsync(attachment, 2);
            Assert.Equal(Canvas2DDecorationUpdateStatus.Unavailable, busy.Status);
            Assert.False(busy.Accepted);
            cancellation.Cancel();
        }
        finally { execution.ReleaseRender(); }
        AssertDecorationPresented(await update.WaitAsync(TimeSpan.FromSeconds(10)), 1, true);
        Assert.Equal("#123456", DecorationFrameItem(execution).Fill);
        AssertDecorationPresented(await host.ClearDecorationsAsync(attachment, 3), 3, false);
        Assert.Equal(1, execution.MaximumConcurrency);
    }

    [Fact]
    public async Task DecorationsCancellationBeforeAcceptancePreservesPreviousFrame()
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)));
        await host.InitializeAsync("decoration-cancel", "decoration-container");
        var attachment = host.CaptureDecorationAttachment()!;
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 1, DecorationInput(host, "#123456")), 1, true);
        var frame = execution.LastContent;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = await host.ClearDecorationsAsync(attachment, 2, cancelled.Token);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Cancelled, result.Status);
        Assert.False(result.Accepted);
        Assert.Same(frame, execution.LastContent);
        AssertDecorationPresented(await host.ClearDecorationsAsync(attachment, 3), 3, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecorationsProviderFailureRetiresOldValuesWithoutNativeFailure(bool duplicateOutput)
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)));
        await host.InitializeAsync("decoration-failure", "decoration-container");
        var attachment = host.CaptureDecorationAttachment()!;
        var document = host.CaptureDocumentSnapshot().Snapshot;
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 1, DecorationInput(host, "#123456")), 1, true);
        var result = await host.ReplaceDecorationsAsync(attachment, 2,
            DecorationInput(host, "#abcdef", duplicateOutput ? "duplicate" : "throw"));
        Assert.Equal(Canvas2DDecorationUpdateStatus.Failed, result.Status);
        Assert.True(result.Accepted);
        Assert.NotEmpty(result.Diagnostics);
        Assert.InRange(result.Diagnostics.Length, 1, Canvas2DDecorationContent.MaximumDiagnostics);
        Assert.True(result.Presentation!.FramePresented);
        Assert.False(result.Presentation.DecorationsIncluded);
        Assert.DoesNotContain(execution.LastContent!.Items, item => item.Id == DecorationTestItemId);
        Assert.Equal(EditingSessionStatus.Ready, Session(host).CaptureState().Status);
        Assert.Same(document, host.CaptureDocumentSnapshot().Snapshot);
        var retry = await host.ReplaceDecorationsAsync(attachment, 2, DecorationInput(host, "#abcdef"));
        Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected, retry.Status);
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 3, DecorationInput(host, "#abcdef")), 3, true);
    }

    [Fact]
    public async Task DecorationsGraphicsFailureNeverAcknowledgesNewContent()
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)));
        await host.InitializeAsync("decoration-graphics", "decoration-container");
        var attachment = host.CaptureDecorationAttachment()!;
        execution.RenderResult = new Canvas2DInteropOperationResult { Succeeded = false, Code = "TEST_DECORATION_RENDER" };
        var failed = await host.ReplaceDecorationsAsync(attachment, 1, DecorationInput(host, "#123456"));
        Assert.Equal(Canvas2DDecorationUpdateStatus.Failed, failed.Status);
        Assert.True(failed.Accepted);
        Assert.False(failed.Presentation!.FramePresented);
        Assert.Equal(EditingSessionStatus.Ready, Session(host).CaptureState().Status);
        execution.RenderResult = new Canvas2DInteropOperationResult { Succeeded = true };
        AssertDecorationPresented(await host.ClearDecorationsAsync(attachment, 2), 2, false);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task DecorationsAttachmentRetiresOnIdenticalSourceReplacementButNotFailedReplacement(EditingSessionAccessMode mode)
    {
        var replacement = new RecordingRenderExecution();
        await using var fixture = new FacadeTestFixture(new(initialDocument: A24Snapshot(), accessMode: mode), () => CreateRenderer(replacement));
        await fixture.InitializeAsync();
        var attachment = fixture.Facade.CaptureDecorationAttachment()!;
        var snapshot = fixture.Facade.GetDocument().Snapshot!;
        var native = NativeDocumentSerializer.Export(snapshot);
        AssertDecorationPresented(await fixture.Facade.ReplaceDecorationsAsync(attachment, 7,
            DecorationInput(fixture.Host, "#123456")), 7, true);
        var oldSession = Session(fixture.Host);
        Assert.True((await fixture.Facade.SetDocumentAsync(snapshot)).Succeeded);
        var newSession = Session(fixture.Host);
        var current = fixture.Facade.CaptureDecorationAttachment()!;
        Assert.NotSame(oldSession, newSession);
        Assert.NotEqual(attachment, current);
        Assert.Equal(attachment.InstanceId, current.InstanceId);
        Assert.NotEqual(attachment.AttachmentVersion, current.AttachmentVersion);
        Assert.NotEqual(oldSession.DecorationAttachmentId, newSession.DecorationAttachmentId);
        Assert.Equal(attachment.DocumentId, current.DocumentId);
        Assert.True(oldSession.CaptureState().IsClosed);
        Assert.Equal(mode, newSession.CaptureState().AccessMode);
        Assert.Equal(0, newSession.CaptureState().DecorationInputRevision);
        Assert.False(newSession.CaptureState().DecorationPresentation?.DecorationsIncluded == true);
        AssertNativeSourceUnchanged();
        Assert.DoesNotContain(replacement.LastContent!.Items, item => item.Id == DecorationTestItemId);

        // A later native composition must not revive the retired attachment's frozen values.
        var firstFrame = replacement.LastContent;
        await fixture.Host.ZoomInAsync();
        Assert.NotSame(firstFrame, replacement.LastContent);
        Assert.DoesNotContain(replacement.LastContent!.Items, item => item.Id == DecorationTestItemId);
        Assert.Equal(0, newSession.CaptureState().DecorationInputRevision);
        var stale = await fixture.Facade.ReplaceDecorationsAsync(attachment, 8,
            DecorationInput(fixture.Host, "#fedcba"));
        Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected, stale.Status);
        Assert.False(stale.Accepted);
        var staleClear = await fixture.Facade.ClearDecorationsAsync(attachment, 9);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected, staleClear.Status);
        Assert.False(staleClear.Accepted);
        Assert.Equal(0, newSession.CaptureState().DecorationInputRevision);
        AssertDecorationPresented(await fixture.Facade.ReplaceDecorationsAsync(current, 1,
            DecorationInput(fixture.Host, "#abcdef")), 1, true);
        Assert.Equal("#abcdef", DecorationFrameItem(replacement).Fill);
        AssertNativeSourceUnchanged();

        var failingRenderer = new RecordingRenderExecution
        { RenderResult = new Canvas2DInteropOperationResult { Succeeded = false, Code = "TEST_REPLACEMENT_FAILURE" } };
        await using var failing = new FacadeTestFixture(new(initialDocument: A24Snapshot(), accessMode: mode), () => CreateRenderer(failingRenderer));
        await failing.InitializeAsync();
        var retained = failing.Facade.CaptureDecorationAttachment()!;
        AssertDecorationPresented(await failing.Facade.ReplaceDecorationsAsync(retained, 1,
            DecorationInput(failing.Host, "#123456")), 1, true);
        Assert.Equal(BpmnModelerOperationStatus.Failed,
            (await failing.Facade.SetDocumentAsync(failing.Facade.GetDocument().Snapshot)).Status);
        Assert.Equal(retained, failing.Facade.CaptureDecorationAttachment());
        AssertDecorationPresented(await failing.Facade.ClearDecorationsAsync(retained, 2), 2, false);

        void AssertNativeSourceUnchanged()
        {
            var accepted = fixture.Facade.GetDocument().Snapshot!;
            Assert.Equal(snapshot.DocumentId, accepted.DocumentId);
            Assert.Equal(snapshot.Revision, accepted.Revision);
            Assert.Equal(snapshot.SemanticModel.Elements.Select(element => element.Id),
                accepted.SemanticModel.Elements.Select(element => element.Id));
            Assert.Equal(snapshot.SemanticModel.Relationships.Select(relationship => relationship.Id),
                accepted.SemanticModel.Relationships.Select(relationship => relationship.Id));
            Assert.Equal(snapshot.VisualModel.VisualStates.Select(visual => visual.Id),
                accepted.VisualModel.VisualStates.Select(visual => visual.Id));
            Assert.Equal(snapshot, accepted);
            Assert.True(native.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(accepted).AsSpan()));
        }
    }

    [Theory]
    [InlineData("render-failure")]
    [InlineData("cancelled-before-start")]
    [InlineData("cancelled-before-commit")]
    public async Task ReadOnlyDecorationsPreserveSourceAttachmentAndValuesWhenReplacementDoesNotCommit(string scenario)
    {
        var factory = new BpmnModelerCompositionFactory(initialDocument: A24Snapshot(),
            accessMode: EditingSessionAccessMode.ReadOnly);
        var active = new RecordingRenderExecution();
        var candidate = new RecordingRenderExecution();
        if (scenario == "render-failure")
            candidate.RenderResult = new Canvas2DInteropOperationResult
            { Succeeded = false, Code = "TEST_READ_ONLY_REPLACEMENT_FAILURE" };
        var pointers = new LoadingPointerObserverFactory();
        using var facade = new BpmnModelerFacade(factory, static _ => Task.CompletedTask);
        await using var host = new DocumentCanvasHost(factory, CreateRenderer(active),
            new RecordingSurfaceObserverFactory(new RecordingSurfaceObserver(new(900, 600, 1))), pointers,
            replacementRendererFactory: () => CreateRenderer(candidate));
        facade.Attach(host);
        await host.InitializeAsync("active", "standby", "container");
        var session = Session(host);
        var snapshot = facade.GetDocument().Snapshot!;
        var sourceBytes = NativeDocumentSerializer.Export(snapshot);
        var attachment = facade.CaptureDecorationAttachment()!;
        AssertDecorationPresented(await facade.ReplaceDecorationsAsync(attachment, 7,
            DecorationInput(host, "#123456")), 7, true);
        var before = session.CaptureState();
        var stamp = facade.CapturePresentation().Stamp;
        var frame = active.LastContent;
        var uploads = active.FullUploadCount;
        var replacementSnapshot = A24Snapshot(position: new(440, 360));
        Assert.NotEqual(snapshot.VisualModel.VisualStates[0].Position, replacementSnapshot.VisualModel.VisualStates[0].Position);

        using var cancellation = new CancellationTokenSource();
        if (scenario == "cancelled-before-start") cancellation.Cancel();
        var replacement = facade.SetDocumentAsync(replacementSnapshot, cancellation.Token).AsTask();
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
        Assert.Equal(scenario == "render-failure" ? BpmnModelerOperationStatus.Failed : BpmnModelerOperationStatus.Cancelled,
            (await replacement.WaitAsync(TimeSpan.FromSeconds(10))).Status);
        Assert.Equal(stamp, facade.CapturePresentation().Stamp);
        Assert.Same(frame, active.LastContent);
        Assert.Equal(uploads, active.FullUploadCount);
        Assert.Equal(0, active.DisposeCount);
        Assert.Equal(scenario == "cancelled-before-start" ? 0 : 1, candidate.DisposeCount);
        if (scenario == "cancelled-before-commit") Assert.Equal(1, pointers.Candidate.DisposeCount);
        AssertPreservedSourceAndDecorations();

        // Rebuild the native scene without resubmitting decoration input. A retained last
        // frame alone cannot prove that the accepted values survived replacement cleanup.
        await host.ZoomInAsync();
        Assert.NotSame(before.CurrentScene, session.CaptureState().CurrentScene);
        Assert.NotSame(frame, active.LastContent);
        Assert.True(active.FullUploadCount > uploads);
        Assert.True(session.CaptureState().DecorationPresentation!.BaseSceneGeneration >
            before.DecorationPresentation!.BaseSceneGeneration);
        AssertPreservedSourceAndDecorations();

        var stale = await facade.ClearDecorationsAsync(attachment, 7);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected, stale.Status);
        Assert.False(stale.Accepted);
        AssertPreservedSourceAndDecorations();
        AssertDecorationPresented(await facade.ClearDecorationsAsync(attachment, 8), 8, false);
        Assert.DoesNotContain(active.LastContent!.Items, item => item.Id == DecorationTestItemId);
        Assert.Equal(attachment, facade.CaptureDecorationAttachment());
        Assert.Same(snapshot, facade.GetDocument().Snapshot);

        void AssertPreservedSourceAndDecorations()
        {
            Assert.Same(session, Session(host));
            Assert.Same(snapshot, facade.GetDocument().Snapshot);
            Assert.True(sourceBytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(facade.GetDocument().Snapshot!).AsSpan()));
            Assert.Equal(attachment, facade.CaptureDecorationAttachment());
            var current = facade.CaptureEditorContext()!.Presentation;
            Assert.Equal(EditingSessionAccessMode.ReadOnly, current.AccessMode);
            Assert.Equal(EditingSessionStatus.Ready, current.Status);
            Assert.False(session.CaptureState().IsClosed);
            Assert.Equal(before.HistoryStatus, current.HistoryStatus);
            Assert.Equal(7, current.DecorationInputRevision);
            Assert.Equal(7, current.DecorationPresentation!.InputRevision);
            Assert.True(current.DecorationPresentation.FramePresented);
            Assert.True(current.DecorationPresentation.DecorationsIncluded);
            Assert.Empty(current.DecorationDiagnostics);
            Assert.Equal("#123456", DecorationFrameItem(active).Fill);
        }
    }

    [Fact]
    public async Task DecorationsAreIndependentAcrossHostsAndRejectForeignAttachments()
    {
        var firstExecution = new RecordingRenderExecution();
        var secondExecution = new RecordingRenderExecution();
        await using var first = CreateHost(firstExecution, new RecordingSurfaceObserver(new(1400, 900, 1)));
        await using var second = CreateHost(secondExecution, new RecordingSurfaceObserver(new(1400, 900, 1)));
        await first.InitializeAsync("decoration-a", "container-a");
        await second.InitializeAsync("decoration-b", "container-b");
        var firstAttachment = first.CaptureDecorationAttachment()!;
        var secondAttachment = second.CaptureDecorationAttachment()!;
        Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected,
            (await second.ClearDecorationsAsync(firstAttachment, 1)).Status);
        firstExecution.BlockRender = true;
        var firstUpdate = first.ReplaceDecorationsAsync(firstAttachment, 1, DecorationInput(first, "#123456")).AsTask();
        try
        {
            await firstExecution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AssertDecorationPresented(await second.ReplaceDecorationsAsync(secondAttachment, 1,
                DecorationInput(second, "#abcdef")), 1, true);
            Assert.Equal("#abcdef", DecorationFrameItem(secondExecution).Fill);
            Assert.False(firstUpdate.IsCompleted);
        }
        finally { firstExecution.ReleaseRender(); }
        AssertDecorationPresented(await firstUpdate.WaitAsync(TimeSpan.FromSeconds(10)), 1, true);
    }

    [Fact]
    public async Task DecorationsSurviveZoomResizeDprAndNativeRecomposition()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new(1400, 900, 1));
        await using var host = CreateHost(execution, surface);
        await host.InitializeAsync("decoration-surfaces", "decoration-container");
        var attachment = host.CaptureDecorationAttachment()!;
        var document = host.CaptureDocumentSnapshot().Snapshot;
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 1, DecorationInput(host, "#123456")), 1, true);
        await host.ZoomInAsync();
        Assert.Equal("#123456", DecorationFrameItem(execution).Fill);
        await surface.RaiseAsync(new(960, 540, 2));
        Assert.Equal("#123456", DecorationFrameItem(execution).Fill);
        await surface.RaiseAsync(new(1400, 900, 1));
        Assert.Equal("#123456", DecorationFrameItem(execution).Fill);
        Assert.Equal(attachment, host.CaptureDecorationAttachment());
        Assert.Same(document, host.CaptureDocumentSnapshot().Snapshot);
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 2, DecorationInput(host, "#abcdef")), 2, true);
        await host.ZoomOutAsync();
        Assert.Equal("#abcdef", DecorationFrameItem(execution).Fill);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecorationsBeyondNativeExtentsNeverEnterGraphicalExportPublicationOrFit(bool semanticOnly)
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("decoration-export", "decoration-container");
        var session = Session(host);
        await SaveDefaultPublicationAsync(session);
        var document = AttachedDocument(session);
        var poolId = new SemanticElementId("test:decoration-pool");
        Assert.True((await session.ExecuteAsync(new SetModelProfileAvailabilityCommand(document.DocumentId, document.Revision,
            [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.True((await session.ExecuteAsync(new CreateOrganizationalPoolCommand(document.DocumentId, document.Revision,
            poolId, session.CaptureState().ActiveScopeId, OrganizationalPoolCreationMode.AdoptEligibleUnassigned,
            "Decoration test pool"))).IsCommitted);
        await session.WaitForIdleAsync();
        var before = session.CaptureState();
        var native = NativeDocumentSerializer.Export(document);
        var capture = (await host.CapturePresentationAsync())!.Capture!;
        var graphical = GraphicalExportPreparer.Prepare(capture).Scene!;
        var svg = SvgGraphicalExporter.Export(graphical);
        var operations = new DemoDocumentOperations(host);
        var options = new BpmnPublishingOptions { IncludePools = true, InitialViewMode = BpmnPublishingInitialViewMode.FitEntireDiagram };
        var published = await operations.PublishProcessAsync(options);
        Assert.True(published.Succeeded, PublishDiagnostics(published.Diagnostics));
        var poolHeaderId = Canvas2DSceneObjectIdentity.ForExtension(new("inceptus:organizational/scene/pools"),
            $"{poolId.Value}:pool-header");
        var target = semanticOnly
            ? before.CurrentScene!.Items.Single(item => item.Id == poolHeaderId && item.Origin.SemanticElementId == poolId)
            : before.CurrentScene!.Items.First(Canvas2DNodeBodyMetadata.IsNodeBody);
        Assert.Equal(semanticOnly, target.Origin.ProjectedObjectId is null);
        var values = DecorationInput(host, "#123456", "far").Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        values["target"] = PropertyValue.FromText(target.Id.Value);
        var input = new Canvas2DDecorationInput(DecorationTestProviderId, new HostDecorationProvider(), values);
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(host.CaptureDecorationAttachment()!, 1, input), 1, true);
        Assert.Equal(1000000, DecorationFrameItem(execution).GeometryBounds.X);
        Assert.Same(before.CurrentScene, session.CaptureState().CurrentScene);
        Assert.Same(before.EditorState, session.CaptureState().EditorState);
        var afterCapture = (await host.CapturePresentationAsync())!.Capture!;
        var afterGraphical = GraphicalExportPreparer.Prepare(afterCapture).Scene!;
        Assert.Equal(graphical.Bounds, afterGraphical.Bounds);
        Assert.Equal(graphical.Items.AsEnumerable(), afterGraphical.Items.AsEnumerable());
        Assert.True(svg.Artifact!.Content.AsSpan().SequenceEqual(SvgGraphicalExporter.Export(afterGraphical).Artifact!.Content.AsSpan()));
        var afterPublished = await operations.PublishProcessAsync(options);
        Assert.True(afterPublished.Succeeded, PublishDiagnostics(afterPublished.Diagnostics));
        // Identical complete publication bytes also preserve contentBounds and the fit input.
        Assert.True(published.Payload.AsSpan().SequenceEqual(afterPublished.Payload.AsSpan()));
        Assert.True(native.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(document).AsSpan()));
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(1000000, DecorationFrameItem(execution).GeometryBounds.X);
    }

    [Fact]
    public async Task DecorationsRecomposeForNativeScopeChangesWithoutRevivingOldInput()
    {
        var execution = new RecordingRenderExecution();
        var factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync())
            .WithAccessMode(EditingSessionAccessMode.ReadOnly);
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: factory);
        await host.InitializeAsync("decoration-scopes", "decoration-container");
        var initial = host.CapturePresentation();
        var attachment = host.CaptureDecorationAttachment()!;
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 1, DecorationInput(host, "#123456")), 1, true);
        var nested = await host.NavigateToScopeAsync(initial.Stamp!, BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.True(nested.Succeeded);
        Assert.DoesNotContain(execution.LastContent!.Items, item => item.Id == DecorationTestItemId);
        Assert.Equal(attachment, host.CaptureDecorationAttachment());
        AssertDecorationPresented(await host.ClearDecorationsAsync(attachment, 2), 2, false);
        Assert.True((await host.NavigateToScopeAsync(nested.Presentation.Stamp!, initial.ActiveScopeId!)).Succeeded);
        Assert.DoesNotContain(execution.LastContent!.Items, item => item.Id == DecorationTestItemId);
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task DecorationsAutomaticRecompositionFailureIsVisibleThroughPublicEditorContext()
    {
        var factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync())
            .WithAccessMode(EditingSessionAccessMode.ReadOnly);
        await using var fixture = new FacadeTestFixture(factory);
        await fixture.InitializeAsync();
        var attachment = fixture.Facade.CaptureDecorationAttachment()!;
        var original = fixture.Facade.CapturePresentation();
        var input = DecorationInput(fixture.Host, "#123456", "throw-nested");
        AssertDecorationPresented(await fixture.Facade.ReplaceDecorationsAsync(attachment, 1, input), 1, true);
        var before = fixture.Facade.CaptureEditorContext()!.Presentation;
        Assert.Empty(before.DecorationDiagnostics);
        var changed = await fixture.Facade.NavigateToScopeAsync(original.Stamp!, BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.True(changed.Succeeded);
        var current = fixture.Facade.CaptureEditorContext()!.Presentation;
        Assert.Equal(EditingSessionStatus.Ready, current.Status);
        Assert.Empty(current.RuntimeDiagnostics);
        Assert.Empty(current.PresentationDiagnostics);
        Assert.Contains(current.DecorationDiagnostics, diagnostic => diagnostic.Code == "CANVAS2D_DECORATION_FAILED");
        Assert.Equal(1, current.DecorationInputRevision);
        Assert.True(current.DecorationPresentation!.FramePresented);
        Assert.False(current.DecorationPresentation.DecorationsIncluded);
        Assert.Equal(current.PresentationContext, current.DecorationPresentation.PresentationContext);
        Assert.True(current.DecorationPresentation.BaseSceneGeneration > before.DecorationPresentation!.BaseSceneGeneration);
        Assert.Equal(before.DocumentRevision, current.DocumentRevision);
        Assert.Equal(before.HistoryStatus, current.HistoryStatus);
        AssertDecorationPresented(await fixture.Facade.ClearDecorationsAsync(attachment, 2), 2, false);
        Assert.Empty(fixture.Facade.CaptureEditorContext()!.Presentation.DecorationDiagnostics);
    }

    [Fact]
    public async Task DecorationsDelayedFrameCannotReachReplacementSurface()
    {
        var active = new RecordingRenderExecution();
        var candidate = new RecordingRenderExecution();
        await using var host = CreateHost(active, new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: new Inceptus.DiagramEngine.Bpmn.Blazor.Composition.BpmnModelerCompositionFactory(initialDocument: A24Snapshot()),
            replacementRendererFactory: () => CreateRenderer(candidate));
        await host.InitializeAsync("decoration-active", "decoration-standby", "decoration-container");
        var before = host.CaptureDocumentSnapshot().Snapshot!;
        var attachment = host.CaptureDecorationAttachment()!;
        active.BlockRender = true;
        var update = host.ReplaceDecorationsAsync(attachment, 1, DecorationInput(host, "#123456")).AsTask();
        Task<BpmnModelerDocumentResult>? replacement = null;
        try
        {
            await active.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            replacement = host.SetDocumentAsync(before).AsTask();
            Assert.False(replacement.IsCompleted);
            Assert.Null(candidate.LastContent);
        }
        finally { active.ReleaseRender(); }
        AssertDecorationPresented(await update.WaitAsync(TimeSpan.FromSeconds(10)), 1, true);
        Assert.True((await replacement!.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
        Assert.DoesNotContain(candidate.LastContent!.Items, item => item.Id == DecorationTestItemId);
        Assert.Equal(1, active.DisposeCount);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected,
            (await host.ClearDecorationsAsync(attachment, 2)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecorationsDisposalRetiresLateProviderAndRendererOutput(bool delayRenderer)
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)));
        await host.InitializeAsync("decoration-disposal", "decoration-container");
        var attachment = host.CaptureDecorationAttachment()!;
        var input = DecorationInput(host, "#123456");
        using var releaseProvider = new ManualResetEventSlim();
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!delayRenderer)
            input = new Canvas2DDecorationInput(DecorationTestProviderId,
                new DelayedHostDecorationProvider(providerStarted, releaseProvider), input.Values);
        execution.BlockRender = delayRenderer;
        var operation = Task.Run(async () => await host.ReplaceDecorationsAsync(attachment, 1, input));
        Task? disposal = null;
        try
        {
            await (delayRenderer ? execution.RenderStarted.Task : providerStarted.Task).WaitAsync(TimeSpan.FromSeconds(10));
            disposal = host.DisposeAsync().AsTask();
            Assert.Null(host.CaptureDecorationAttachment());
            Assert.False(disposal.IsCompleted);
        }
        finally
        {
            releaseProvider.Set();
            execution.ReleaseRender();
        }
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(10));
        await disposal!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Accepted);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Unavailable, result.Status);
        Assert.False(result.Presentation?.FramePresented ?? false);
        Assert.Equal(delayRenderer ? 2 : 1, execution.FullUploadCount);
        Assert.Equal(1, execution.DisposeCount);
        Assert.True(Session(host).CaptureState().IsClosed);
    }

    private static Canvas2DDecorationInput DecorationInput(DocumentCanvasHost host, string fill, string mode = "normal")
    {
        var target = Session(host).CaptureState().CurrentScene!.Items.First(Canvas2DNodeBodyMetadata.IsNodeBody);
        return new(DecorationTestProviderId, new HostDecorationProvider(), new Dictionary<string, PropertyValue>
        {
            ["target"] = PropertyValue.FromText(target.Id.Value),
            ["fill"] = PropertyValue.FromText(fill),
            ["mode"] = PropertyValue.FromText(mode),
            ["source-scope"] = PropertyValue.FromText(Session(host).CaptureState().ActiveScopeId.Value),
        });
    }

    private static void AssertDecorationPresented(Canvas2DDecorationUpdateResult result, long revision, bool included)
    {
        Assert.Equal(Canvas2DDecorationUpdateStatus.Presented, result.Status);
        Assert.True(result.Accepted);
        Assert.Equal(revision, result.InputRevision);
        Assert.NotNull(result.Presentation);
        Assert.True(result.Presentation.FramePresented);
        Assert.Equal(included, result.Presentation.DecorationsIncluded);
        Assert.Equal(revision, result.Presentation.InputRevision);
    }

    private static Canvas2DRenderItem DecorationFrameItem(RecordingRenderExecution execution) =>
        Assert.Single(execution.LastContent!.Items, item => item.Id == DecorationTestItemId);

    private sealed class HostDecorationProvider : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input)
        {
            if (input["mode"].TextValue == "throw") throw new InvalidOperationException("Test provider failure.");
            if (input["mode"].TextValue == "throw-nested" && context.PresentationContext is ProcessScopePresentationContext process &&
                process.ScopeId.Value != input["source-scope"].TextValue)
                throw new InvalidOperationException("Test provider cannot decorate this native context.");
            var matches = context.Parts.Where(item => item.Item.Id.Value == input["target"].TextValue).ToArray();
            if (matches.Length == 0) return Canvas2DDecorationContent.Empty;
            var part = Assert.Single(matches);
            Assert.DoesNotContain(context.BaseItems, item => item.Id.Value == DecorationTestItemId);
            var bounds = part.Item.Bounds;
            var item = new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForExtension(DecorationTestProviderId, "annotation"),
                Canvas2DSceneLayer.Decoration, 0,
                Canvas2DSceneGeometry.Rectangle(input["mode"].TextValue == "far"
                    ? new RectD(1000000, 1000000, 50, 12) : new RectD(bounds.X, bounds.Bottom + 4, 50, 12)),
                part.CreateOrigin("annotation"), style: new(fill: input["fill"].TextValue));
            return new(input["mode"].TextValue == "duplicate" ? [item, item] : [item]);
        }
    }

    // Synchronization controls belong only to this test fixture. The composed values still
    // come exclusively from the copied immutable input and borrowed native context.
    private sealed class DelayedHostDecorationProvider(TaskCompletionSource started, ManualResetEventSlim release)
        : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input)
        {
            started.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return new HostDecorationProvider().Compose(context, input);
        }
    }
}
