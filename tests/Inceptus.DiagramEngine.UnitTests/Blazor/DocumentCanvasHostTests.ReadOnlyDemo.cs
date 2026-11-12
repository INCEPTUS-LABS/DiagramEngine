using System.Collections.Concurrent;
using System.Reflection;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task Stage5DemoKeepsLogicalPresentationDuringAnUnpresentedHoverFrame()
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        var modeler = fixture.Renderer.Modeler;
        var session = Session(fixture.Host);
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        var body = session.CaptureState().CurrentScene!.Items.First(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId && item.Layer == Canvas2DSceneLayer.Content);
        await PointerObserver(fixture.Host).MoveDocumentPointAsync(session.CaptureState().CurrentScene!, Center(body.Bounds));
        fixture.Demo.RefreshPresentation();
        var coherent = fixture.Demo.Presentation;
        Assert.True(coherent!.IsAvailable);
        var native = NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!);
        var history = session.CaptureState().HistoryStatus;
        fixture.Execution.BlockRender = true;
        var leave = PointerObserver(fixture.Host).LeaveAsync();
        try
        {
            await fixture.Execution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(modeler.CapturePresentation().IsAvailable);
            fixture.Demo.RefreshPresentation();
            Assert.Same(coherent, fixture.Demo.Presentation);
        }
        finally
        {
            fixture.Execution.ReleaseRender();
            await leave.WaitAsync(TimeSpan.FromSeconds(5));
        }
        await fixture.Demo.NavigateAsync(BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, fixture.Demo.Presentation!.ActiveScopeId);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.True(native.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        fixture.Demo.Modeler = null;
        fixture.Demo.RefreshPresentation();
        Assert.Null(fixture.Demo.Presentation);
    }

    [Fact]
    public async Task ReadOnlyDemoCompilesOneSettingsActionAndReadOnlyPresentationCallbacks()
    {
        using var demo = new ReadOnlyBpmnDemoHost();
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(ReadOnlyBpmnDemoHost).GetProperty("Text", flags)!.SetValue(demo, services.GetRequiredService<IStringLocalizer<ModelerStrings>>());
        await Assert.IsAssignableFrom<Task>(typeof(ReadOnlyBpmnDemoHost).GetMethod("OnInitializedAsync", flags)!.Invoke(demo, null));
        using var builder = new RenderTreeBuilder();
        typeof(ReadOnlyBpmnDemoHost).GetMethod("BuildRenderTree", flags)!.Invoke(demo, [builder]);
#pragma warning disable BL0006 // Inspect compiled Razor output, including browser-event directive binding.
        var range = builder.GetFrames();
        var outerFrames = range.Array.Take(range.Count).ToArray();
        Assert.Single(outerFrames, frame => frame.FrameType == RenderTreeFrameType.Component && frame.ComponentType == typeof(DemoApplicationLayout));
        var content = Assert.IsType<RenderFragment>(Assert.Single(outerFrames, frame =>
            frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == nameof(DemoApplicationLayout.ChildContent)).AttributeValue);
        using var contentBuilder = new RenderTreeBuilder();
        content(contentBuilder);
        var contentRange = contentBuilder.GetFrames();
        var frames = contentRange.Array.Take(contentRange.Count).ToArray();
        var attributes = frames.Where(frame => frame.FrameType == RenderTreeFrameType.Attribute).ToArray();
        Assert.DoesNotContain(attributes, attribute => attribute.AttributeName.StartsWith('@'));
        Assert.Single(attributes, attribute => attribute.AttributeName == "onclick" && attribute.AttributeValue is not string);
        Assert.DoesNotContain(attributes, attribute => attribute.AttributeName == "onchange");
        Assert.Single(frames, frame => frame.FrameType == RenderTreeFrameType.Component && frame.ComponentType == typeof(InceptusBpmnModeler));
        Assert.Equal(EditingSessionAccessMode.ReadOnly,
            Assert.Single(attributes, attribute => attribute.AttributeName == nameof(InceptusBpmnModeler.AccessMode)).AttributeValue);
        Assert.IsType<BpmnApplicationActionProvider>(
            Assert.Single(attributes, attribute => attribute.AttributeName == nameof(InceptusBpmnModeler.ApplicationActionProvider)).AttributeValue);
        Assert.IsType<EventCallback<BpmnApplicationActionInvocationEventArgs>>(
            Assert.Single(attributes, attribute => attribute.AttributeName == nameof(InceptusBpmnModeler.ApplicationActionInvoked)).AttributeValue);
        Assert.IsType<EventCallback<BpmnModelerDisplayMode>>(
            Assert.Single(attributes, attribute => attribute.AttributeName == nameof(InceptusBpmnModeler.DisplayModeChanged)).AttributeValue);
#pragma warning restore BL0006
    }

    [Fact]
    public async Task Stage5DemoUsesPublicActionsNavigationAndReplacementWithoutNativeEdits()
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        var modeler = fixture.Renderer.Modeler;
        var source = modeler.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        var history = Session(fixture.Host).CaptureState().HistoryStatus;
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!,
            BpmnDemoPipeline.ProcessOrderSubProcessVisualId)).Succeeded);
        Assert.True((await fixture.Host.OpenApplicationContextMenuFromSelectionAsync()).Succeeded);
        var menu = await ApplicationMenuAsync(fixture.Host);
        Assert.Equal(["example:inspect", "example:linked-record"], menu.ApplicationActions.Select(action => action.Id));
        Assert.False(menu.ApplicationActions[1].IsEnabled);
        Assert.NotEmpty(menu.ApplicationActions[1].DisabledExplanation!);
        Assert.False(menu.PropertiesAvailable);
        Assert.Null(menu.DeletionAction);
        await fixture.Renderer.ClickApplicationActionAsync(fixture.Host, "example:inspect");
        Assert.Contains(menu.ApplicationContext!.Target.SemanticId.Value, fixture.Demo.ApplicationDetails, StringComparison.Ordinal);
        Assert.Contains("Application inspected Element:", fixture.Demo.ApplicationDetails, StringComparison.Ordinal);
        Assert.Empty(fixture.DocumentChanges);
        foreach (var scope in modeler.CapturePresentation().AvailableScopes)
        {
            await fixture.Demo.NavigateAsync(scope.Id);
            Assert.Equal(scope.Id, modeler.CapturePresentation().ActiveScopeId);
            Assert.Equal(history, Session(fixture.Host).CaptureState().HistoryStatus);
            Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        }
        var retired = modeler.CapturePresentation().Stamp!;
        await fixture.Demo.ReplaceAsync(source);
        Assert.NotEqual(retired.AttachmentVersion, modeler.CapturePresentation().Stamp!.AttachmentVersion);
        Assert.False((await modeler.SelectVisualAsync(retired, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        Assert.Equal(BpmnModelerDocumentChangeKind.DocumentReplacement, Assert.Single(fixture.DocumentChanges).Kind);
        var context = modeler.CaptureEditorContext()!;
        var mutation = await modeler.UpdatePublicationAsync(context.AttachmentVersion, source.DocumentId,
            source.Revision, new("forbidden", "Not editable", ""));
        Assert.Equal(BpmnModelerOperationStatus.Rejected, mutation.Status);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(EditingSessionAccessMode.ReadOnly, Session(fixture.Host).CaptureState().AccessMode);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("replacement")]
    [InlineData("disposed")]
    [InlineData("cancelled")]
    [InlineData("unknown-action")]
    public async Task Stage5DemoRejectsRetiredApplicationDetails(string change)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        var modeler = fixture.Renderer.Modeler;
        var source = modeler.GetDocument().Snapshot!;
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var context = (await ApplicationMenuAsync(fixture.Host)).ApplicationContext!;
        var before = fixture.Demo.ApplicationDetails;
        using var cancellation = new CancellationTokenSource();
        if (change == "selection")
            Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.StartEventVisualId)).Succeeded);
        if (change == "replacement") await fixture.Demo.ReplaceAsync(source);
        if (change == "disposed") fixture.Demo.Dispose();
        if (change == "cancelled") cancellation.Cancel();
        await fixture.Demo.InspectAsync(new(change == "unknown-action" ? "other" : "example:inspect", context, cancellation.Token));
        Assert.Equal(before, fixture.Demo.ApplicationDetails);
        Assert.Equal(source, modeler.GetDocument().Snapshot);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage5DemoActionLookupCooperatesWithCancellation()
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        var modeler = fixture.Renderer.Modeler;
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var context = (await ApplicationMenuAsync(fixture.Host)).ApplicationContext!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ReadOnlyBpmnDemoHost.DiscoverActionsAsync(context, cancellation.Token).AsTask());
    }

    private sealed class ReadOnlyDemoFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        internal ApplicationComponentRenderer Renderer { get; }
        internal DocumentCanvasHost Host { get; }
        internal RecordingRenderExecution Execution { get; }
        internal ReadOnlyBpmnDemoHost Demo { get; } = new();
        internal ConcurrentQueue<BpmnModelerDocumentChangedEventArgs> DocumentChanges { get; } = new();

        private ReadOnlyDemoFixture(ServiceProvider services, ApplicationComponentRenderer renderer, DocumentCanvasHost host,
            RecordingRenderExecution execution)
        { _services = services; Renderer = renderer; Host = host; Execution = execution; }

        internal static async Task<ReadOnlyDemoFixture> CreateAsync()
        {
            var prepared = await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync();
            var source = (await prepared.CreateAsync()).Document.CaptureSnapshot();
            var services = new ServiceCollection().AddSingleton<IJSRuntime>(new SiteToolbarJsRuntime())
                .AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
            var renderer = new ApplicationComponentRenderer(services);
            ReadOnlyDemoFixture? fixture = null;
            await renderer.InitializeAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InceptusBpmnModeler.InitialDocument)] = source,
                [nameof(InceptusBpmnModeler.AccessMode)] = EditingSessionAccessMode.ReadOnly,
                [nameof(InceptusBpmnModeler.ApplicationActionProvider)] = (BpmnApplicationActionProvider)ReadOnlyBpmnDemoHost.DiscoverActionsAsync,
                [nameof(InceptusBpmnModeler.ApplicationActionInvoked)] = EventCallback.Factory.Create<BpmnApplicationActionInvocationEventArgs>(
                    new object(), args => fixture!.Demo.InspectAsync(args)),
                [nameof(InceptusBpmnModeler.DocumentChanged)] = EventCallback.Factory.Create<BpmnModelerDocumentChangedEventArgs>(
                    new object(), args => fixture!.DocumentChanges.Enqueue(args)),
            }));
            var facade = Assert.IsType<BpmnModelerFacade>(typeof(InceptusBpmnModeler)
                .GetField("_facade", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer.Modeler));
            var execution = new RecordingRenderExecution();
            var host = CreateHost(execution, new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1600, 1000, 1)),
                compositionFactory: facade.CompositionFactory, replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
            fixture = new(services, renderer, host, execution);
            fixture.Demo.Modeler = renderer.Modeler;
            facade.Attach(host);
            await host.InitializeAsync("stage5-demo-active", "stage5-demo-standby", "stage5-demo-container");
            fixture.Demo.RefreshPresentation();
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            Demo.Dispose();
#pragma warning disable BL0006 // Reuse the existing renderer fixture to exercise the real public component.
            await Renderer.DisposeAsync();
#pragma warning restore BL0006
            await Host.DisposeAsync();
            await _services.DisposeAsync();
        }
    }
}
