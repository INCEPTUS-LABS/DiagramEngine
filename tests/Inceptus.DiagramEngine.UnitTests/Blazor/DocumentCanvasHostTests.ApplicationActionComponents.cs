using System.Collections.Concurrent;
using System.Reflection;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    [InlineData(EditingSessionAccessMode.Editable)]
    public async Task Stage4CompiledMenuClickInvokesRealRootCallbackOnItsDispatcher(EditingSessionAccessMode mode)
    {
        using var services = new ServiceCollection()
            .AddSingleton<IJSRuntime>(new SiteToolbarJsRuntime())
            .AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        await using var renderer = new ApplicationComponentRenderer(services);
        var invoked = new TaskCompletionSource<BpmnApplicationActionInvocationEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var discoveryCount = 0;
        BpmnApplicationActionProvider provider = (_, _) =>
        {
            Assert.True(renderer.Dispatcher.CheckAccess());
            discoveryCount++;
            return ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>(
                [new("app:first", "First action"), new("app:second", "Second action")]);
        };
        var callback = EventCallback.Factory.Create<BpmnApplicationActionInvocationEventArgs>(new object(),
            async args =>
            {
                Assert.True(renderer.Dispatcher.CheckAccess());
                var document = await renderer.Modeler.GetDocumentAsync(args.CancellationToken);
                Assert.True(document.Succeeded);
                invoked.TrySetResult(args);
            });
        await renderer.InitializeAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(InceptusBpmnModeler.InitialDocument)] = A24Snapshot(),
            [nameof(InceptusBpmnModeler.AccessMode)] = mode,
            [nameof(InceptusBpmnModeler.ApplicationActionProvider)] = provider,
            [nameof(InceptusBpmnModeler.ApplicationActionInvoked)] = callback,
        }));
        var facade = Assert.IsType<BpmnModelerFacade>(typeof(InceptusBpmnModeler)
            .GetField("_facade", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer.Modeler));
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400, 900, 1)),
            compositionFactory: facade.CompositionFactory);
        facade.Attach(host);
        await host.InitializeAsync("application-root-canvas", "application-root-container");
        var before = facade.GetDocument().Snapshot;
        var history = Session(host).CaptureState().HistoryStatus;
        await SelectApplicationTargetAsync(facade, new("v-source"));
        Assert.True((await host.OpenApplicationContextMenuFromSelectionAsync()).Succeeded);
        var menu = await ApplicationMenuAsync(host);
        var request = menu.ApplicationContext!;

        await renderer.ClickApplicationActionAsync(host, "app:first");
        var received = await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("app:first", received.ActionId);
        Assert.Equal(request.RequestVersion, received.Context.RequestVersion);
        Assert.Equal(request.Target.SemanticId, received.Context.Target.SemanticId);
        Assert.Equal(request.Stamp, received.Context.Stamp);
        Assert.Equal(2, discoveryCount);
        Assert.Null(host.CaptureState().ContextMenu);
        Assert.Same(before, facade.GetDocument().Snapshot);
        Assert.Equal(history, Session(host).CaptureState().HistoryStatus);
        Assert.Empty(renderer.Exceptions);
    }

    private sealed class ApplicationComponentRoot : InceptusBpmnModeler
    {
        public ApplicationComponentRoot() { }
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

    private sealed class ApplicationComponentCanvas : DocumentCanvas
    {
        public ApplicationComponentCanvas() { }
        // Browser resource ownership is already exercised by the host mocks and browser acceptance.
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }

#pragma warning disable BL0006 // Exercise compiled Razor event wiring and the actual root dispatcher.
    private sealed class ApplicationComponentRenderer(IServiceProvider services)
        : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        internal ApplicationComponentRoot Modeler { get; private set; } = null!;
        internal ConcurrentQueue<Exception> Exceptions { get; } = new();

        internal Task InitializeAsync(ParameterView parameters) => Dispatcher.InvokeAsync(async () =>
        {
            Modeler = (ApplicationComponentRoot)InstantiateComponent(typeof(ApplicationComponentRoot));
            await RenderRootComponentAsync(AssignRootComponentId(Modeler), parameters);
        });

        internal Task ClickApplicationActionAsync(DocumentCanvasHost host, string actionId) => Dispatcher.InvokeAsync(async () =>
        {
            var canvas = (ApplicationComponentCanvas)InstantiateComponent(typeof(ApplicationComponentCanvas));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(DocumentCanvas).GetField("_host", flags)!.SetValue(canvas, host);
            typeof(DocumentCanvas).GetField("_state", flags)!.SetValue(canvas, host.CaptureState());
            var canvasId = AssignRootComponentId(canvas);
            await RenderRootComponentAsync(canvasId, ParameterView.Empty);
            var frames = GetCurrentRenderTreeFrames(canvasId);
            var matchingHandlers = new List<ulong>();
            for (var index = 0; index < frames.Count; index++)
            {
                if (frames.Array[index].FrameType != RenderTreeFrameType.Element ||
                    frames.Array[index].ElementName != "button") continue;
                string? currentId = null;
                ulong handler = 0;
                for (var attributeIndex = index + 1; attributeIndex < frames.Count &&
                     frames.Array[attributeIndex].FrameType == RenderTreeFrameType.Attribute; attributeIndex++)
                {
                    var attribute = frames.Array[attributeIndex];
                    if (attribute.AttributeName == "data-application-action-id") currentId = attribute.AttributeValue as string;
                    if (attribute.AttributeName == "onclick") handler = attribute.AttributeEventHandlerId;
                }
                if (currentId == actionId) matchingHandlers.Add(handler);
            }
            var eventHandler = Assert.Single(matchingHandlers);
            Assert.NotEqual(0UL, eventHandler);
            await DispatchEventAsync(eventHandler, null, new MouseEventArgs());
        });

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => Exceptions.Enqueue(exception);
    }
#pragma warning restore BL0006
}
