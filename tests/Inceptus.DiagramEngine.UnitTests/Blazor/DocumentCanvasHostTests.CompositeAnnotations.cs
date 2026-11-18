using System.Reflection;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Export;
using Inceptus.DiagramEngine.Canvas2D.Interaction;
using Inceptus.DiagramEngine.Canvas2D.Rendering.Interop;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private static readonly Canvas2DSceneContributorId CompositeProviderId = new("test:composite-annotations");

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task CompositeAnnotationsThroughPublicComponentUpdateWholeMessagesWithoutNativeChanges(EditingSessionAccessMode mode)
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
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: facade.CompositionFactory);
        facade.Attach(host);
        await host.InitializeAsync("composite-component", "composite-container");
        var before = Session(host).CaptureState();
        var source = component.CaptureDocumentSnapshot().Snapshot!;
        var nativeBytes = NativeDocumentSerializer.Export(source);
        var nativeCapture = (await host.CapturePresentationAsync())!.Capture!;
        var nativeExport = SvgGraphicalExporter.Export(GraphicalExportPreparer.Prepare(nativeCapture).Scene!).Artifact!.Content;
        var attachment = component.CaptureDecorationAttachment()!;

        AssertDecorationPresented(await component.ReplaceDecorationsAsync(attachment, 1,
            CompositeInput(host, "Waiting: 12", CompositeSvg("#123456"))), 1, true);
        var first = CompositeItems(execution);
        Assert.Equal(4, first.Length); // text-only, SVG+text, SVG-only on one exact native part.
        Assert.Equal(2, first.Count(item => item.GeometryKind == (int)Canvas2DSceneGeometryKind.Image));
        Assert.Equal(2, first.Count(item => item.GeometryKind == (int)Canvas2DSceneGeometryKind.Text));
        Assert.Equal("Waiting: 12", CompositeItem(execution, "combined:text").Content);
        var oldResource = Assert.Single(execution.LastContent!.DecorationResources!.Images,
            image => image.Reference == "test:combined");
        var independentText = CompositeItem(execution, "plain:text");
        var independentImage = CompositeItem(execution, "status:image");

        AssertDecorationPresented(await component.ReplaceDecorationsAsync(attachment, 2,
            CompositeInput(host, "Waiting: 21", CompositeSvg("#abcdef"))), 2, true);
        Assert.Equal(first.Select(item => item.Id), CompositeItems(execution).Select(item => item.Id));
        Assert.Equal("Waiting: 21", CompositeItem(execution, "combined:text").Content);
        var changedResource = Assert.Single(execution.LastContent!.DecorationResources!.Images,
            image => image.Reference == "test:combined");
        Assert.NotEqual(oldResource.ContentIdentity, changedResource.ContentIdentity);
        Assert.Contains("#abcdef", changedResource.SvgContent, StringComparison.Ordinal);
        Assert.Equal(independentText.Content, CompositeItem(execution, "plain:text").Content);
        Assert.Equal(independentImage.Content, CompositeItem(execution, "status:image").Content);

        AssertDecorationPresented(await component.ReplaceDecorationsAsync(attachment, 3,
            CompositeInput(host, "Waiting: 21", CompositeSvg("#abcdef"), "text")), 3, true);
        Assert.Equal(3, CompositeItems(execution).Length);
        Assert.DoesNotContain(CompositeItems(execution), item => item.Id == CompositeId("combined:image"));
        AssertDecorationPresented(await component.ReplaceDecorationsAsync(attachment, 4,
            CompositeInput(host, "Waiting: 21", CompositeSvg("#abcdef"), "image")), 4, true);
        Assert.DoesNotContain(CompositeItems(execution), item => item.Id == CompositeId("combined:text"));
        AssertDecorationPresented(await component.ReplaceDecorationsAsync(attachment, 5,
            CompositeInput(host, "Waiting: 21", CompositeSvg("#abcdef"), "removed")), 5, true);
        Assert.Equal(new[] { CompositeId("plain:text"), CompositeId("status:image") },
            CompositeItems(execution).Select(item => item.Id));
        Assert.Same(before.ProjectedGraph, Session(host).CaptureState().ProjectedGraph);
        Assert.Same(before.LayoutResult, Session(host).CaptureState().LayoutResult);
        Assert.Same(before.RoutingResult, Session(host).CaptureState().RoutingResult);
        await host.ZoomInAsync();
        Assert.Equal(2, CompositeItems(execution).Length);
        Assert.Equal(5, Session(host).CaptureState().DecorationInputRevision);

        Assert.Same(source, component.CaptureDocumentSnapshot().Snapshot);
        Assert.True(nativeBytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(source).AsSpan()));
        Assert.Equal(before.HistoryStatus, Session(host).CaptureState().HistoryStatus);
        Assert.Equal(mode, Session(host).CaptureState().AccessMode);
        var afterCapture = (await host.CapturePresentationAsync())!.Capture!;
        var afterExport = SvgGraphicalExporter.Export(GraphicalExportPreparer.Prepare(afterCapture).Scene!).Artifact!.Content;
        Assert.True(nativeExport.AsSpan().SequenceEqual(afterExport.AsSpan()));
        Assert.DoesNotContain(afterCapture.Scene.Items, item => item.Layer == Canvas2DSceneLayer.Decoration);
        AssertDecorationPresented(await component.ClearDecorationsAsync(attachment, 6), 6, false);
        Assert.Empty(CompositeItems(execution));
        Assert.Null(execution.LastContent!.DecorationResources);
        Assert.Empty(renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task CompositeInvalidResourcesAndPrecancelPreserveAcceptedSourceInputAndArtwork(EditingSessionAccessMode mode)
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(900, 600, 1)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: A24Snapshot(), accessMode: mode));
        await host.InitializeAsync("composite-rejection", "container");
        var attachment = host.CaptureDecorationAttachment()!;
        var source = host.CaptureDocumentSnapshot().Snapshot;
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 1,
            CompositeInput(host, "Retained", CompositeSvg("#123456"))), 1, true);
        var frame = execution.LastContent;
        var resources = frame!.DecorationResources!;
        var invalid = CompositeInput(host, "Must not appear", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>");
        var rejected = await host.ReplaceDecorationsAsync(attachment, 2, invalid);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected, rejected.Status);
        Assert.False(rejected.Accepted);
        Assert.NotEmpty(rejected.Diagnostics);
        Assert.Same(frame, execution.LastContent);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(Canvas2DDecorationUpdateStatus.Cancelled,
            (await host.ReplaceDecorationsAsync(attachment, 3,
                CompositeInput(host, "Cancelled", CompositeSvg("#abcdef")), cancelled.Token)).Status);
        await host.ZoomInAsync();
        Assert.Same(source, host.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(attachment, host.CaptureDecorationAttachment());
        Assert.Equal(1, Session(host).CaptureState().DecorationInputRevision);
        Assert.Equal("Retained", CompositeItem(execution, "combined:text").Content);
        Assert.Equal(resources.Identity, execution.LastContent!.DecorationResources!.Identity);
        Assert.Equal(resources.Images, execution.LastContent.DecorationResources.Images);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, "CANVAS2D_DECORATION_IMAGE_LOAD_FAILED")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "CANVAS2D_DECORATION_IMAGE_LOAD_FAILED")]
    [InlineData(EditingSessionAccessMode.Editable, "CANVAS2D_DECORATION_RESOURCE_TIMEOUT")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "CANVAS2D_DECORATION_RESOURCE_TIMEOUT")]
    public async Task CompositeResourceFailureAcknowledgesOnlyNativeFallbackAndDoesNotRetryOnRecomposition(
        EditingSessionAccessMode mode, string code)
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(900, 600, 1)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: A24Snapshot(), accessMode: mode));
        await host.InitializeAsync("composite-failure", "container");
        var attempts = 0;
        execution.RenderResultFactory = frame =>
        {
            if (frame.DecorationResources is null) return new() { Succeeded = true };
            attempts++;
            return new() { Succeeded = false, Code = code, SourceIdentity = "test:combined" };
        };
        var attachment = host.CaptureDecorationAttachment()!;
        var failed = await host.ReplaceDecorationsAsync(attachment, 1,
            CompositeInput(host, "No partial text", CompositeSvg("#123456")));
        Assert.Equal(Canvas2DDecorationUpdateStatus.Failed, failed.Status);
        Assert.True(failed.Accepted);
        Assert.True(failed.Presentation!.FramePresented);
        Assert.False(failed.Presentation.DecorationsIncluded);
        Assert.Contains(failed.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Empty(CompositeItems(execution));
        Assert.Null(execution.LastContent!.DecorationResources);
        await host.ZoomInAsync();
        Assert.Equal(1, attempts);
        Assert.Empty(CompositeItems(execution));
        Assert.Contains(Session(host).CaptureState().DecorationDiagnostics, diagnostic => diagnostic.Code == code);
        execution.RenderResultFactory = null;
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 2,
            CompositeInput(host, "Explicit retry", CompositeSvg("#123456"))), 2, true);
        Assert.Equal(4, CompositeItems(execution).Length);
        Assert.Empty(Session(host).CaptureState().DecorationDiagnostics);
    }

    [Fact]
    public async Task CompositeFallbackGraphicsFailureNeverAcknowledgesAFrame()
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(900, 600, 1)));
        await host.InitializeAsync("composite-fallback-failed", "container");
        execution.RenderResultFactory = frame => new()
        {
            Succeeded = false,
            Code = frame.DecorationResources is null ? "TEST_GRAPHICS_FAILURE" : "CANVAS2D_DECORATION_IMAGE_LOAD_FAILED",
        };
        var result = await host.ReplaceDecorationsAsync(host.CaptureDecorationAttachment()!, 1,
            CompositeInput(host, "Not presented", CompositeSvg("#123456")));
        Assert.True(result.Accepted);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Failed, result.Status);
        Assert.False(result.Presentation!.FramePresented);
        Assert.False(result.Presentation.DecorationsIncluded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code ==
            Inceptus.DiagramEngine.Canvas2D.Rendering.Canvas2DRendererDiagnosticCodes.RenderingFailed);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CANVAS2D_DECORATION_IMAGE_LOAD_FAILED");
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task CompositeIdenticalSourceReplacementRetiresResourcesAndRequiresFreshInput(EditingSessionAccessMode mode)
    {
        var replacement = new RecordingRenderExecution();
        await using var fixture = new FacadeTestFixture(new(initialDocument: A24Snapshot(), accessMode: mode),
            () => CreateRenderer(replacement));
        await fixture.InitializeAsync();
        var source = fixture.Facade.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        var oldAttachment = fixture.Facade.CaptureDecorationAttachment()!;
        AssertDecorationPresented(await fixture.Facade.ReplaceDecorationsAsync(oldAttachment, 7,
            CompositeInput(fixture.Host, "Old", CompositeSvg("#123456"))), 7, true);
        Assert.True((await fixture.Facade.SetDocumentAsync(source)).Succeeded);
        var current = fixture.Facade.CaptureDecorationAttachment()!;
        Assert.NotEqual(oldAttachment, current);
        Assert.Equal(source.DocumentId, fixture.Facade.GetDocument().Snapshot!.DocumentId);
        Assert.Equal(source.Revision, fixture.Facade.GetDocument().Snapshot!.Revision);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(fixture.Facade.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(0, Session(fixture.Host).CaptureState().DecorationInputRevision);
        await fixture.Host.ZoomInAsync();
        Assert.Empty(CompositeItems(replacement));
        Assert.Null(replacement.LastContent!.DecorationResources);
        var rejected = await fixture.Facade.ReplaceDecorationsAsync(oldAttachment, 8,
            CompositeInput(fixture.Host, "Stale", CompositeSvg("#abcdef")));
        Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected, rejected.Status);
        Assert.False(rejected.Accepted);
        AssertDecorationPresented(await fixture.Facade.ReplaceDecorationsAsync(current, 1,
            CompositeInput(fixture.Host, "Fresh", CompositeSvg("#abcdef"))), 1, true);
        Assert.Equal("Fresh", CompositeItem(replacement, "combined:text").Content);
    }

    [Fact]
    public async Task CompositeResourceOnlyUpdatesDoNotInvokeNativePipeline()
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(900, 600, 1)));
        await host.InitializeAsync("composite-counters", "container");
        var counters = host.CaptureState().PipelineCounters!;
        var before = (counters.ProjectionRuleInvocationCount, counters.LayoutInvocationCount,
            counters.RoutingInvocationCount, counters.SceneContributionInvocationCount);
        var attachment = host.CaptureDecorationAttachment()!;
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 1,
            CompositeInput(host, "Unchanged text", CompositeSvg("#123456"))), 1, true);
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(attachment, 2,
            CompositeInput(host, "Unchanged text", CompositeSvg("#abcdef"))), 2, true);
        AssertDecorationPresented(await host.ClearDecorationsAsync(attachment, 3), 3, false);
        Assert.Equal(before, (counters.ProjectionRuleInvocationCount, counters.LayoutInvocationCount,
            counters.RoutingInvocationCount, counters.SceneContributionInvocationCount));
    }

    private static string CompositeSvg(string fill) =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\" viewBox=\"0 0 20 20\"><rect x=\"1\" y=\"1\" width=\"18\" height=\"18\" fill=\"{fill}\"/></svg>";

    private static string CompositeId(string key) => Canvas2DSceneObjectIdentity.ForExtension(CompositeProviderId, key).Value;

    private static Canvas2DRenderItem[] CompositeItems(RecordingRenderExecution execution) =>
        execution.LastContent!.Items.Where(item => item.Layer == (int)Canvas2DSceneLayer.Decoration).ToArray();

    private static Canvas2DRenderItem CompositeItem(RecordingRenderExecution execution, string key) =>
        Assert.Single(CompositeItems(execution), item => item.Id == CompositeId(key));

    private static Canvas2DDecorationInput CompositeInput(DocumentCanvasHost host, string text, string svg, string variant = "both")
    {
        var target = Session(host).CaptureState().CurrentScene!.Items.First(Canvas2DNodeBodyMetadata.IsNodeBody);
        return new(CompositeProviderId, new CompositeHostProvider(),
        [new("target", PropertyValue.FromText(target.Id.Value)), new("text", PropertyValue.FromText(text)),
            new("variant", PropertyValue.FromText(variant))],
            new([new("test:combined", svg), new("test:status", CompositeSvg("#008800"))]));
    }

    private sealed class CompositeHostProvider : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input)
        {
            var matches = context.Parts.Where(part => part.Item.Id.Value == input["target"].TextValue).ToArray();
            if (matches.Length == 0) return Canvas2DDecorationContent.Empty;
            var part = Assert.Single(matches);
            var items = new List<Canvas2DSceneItem>();
            var x = part.Item.Bounds.Left;
            var y = part.Item.Bounds.Bottom + 5;
            Add("plain:text", Canvas2DSceneGeometry.Text(new(x, y, 180, 22), "Independent text"));
            var variant = input["variant"].TextValue;
            if (variant is "both" or "image")
                Add("combined:image", Canvas2DSceneGeometry.Image(new(x, y + 28, 20, 20), "test:combined"));
            if (variant is "both" or "text")
                Add("combined:text", Canvas2DSceneGeometry.Text(new(x + 26, y + 28, 180, 22), input["text"].TextValue));
            Add("status:image", Canvas2DSceneGeometry.Image(new(x, y + 56, 20, 20), "test:status"));
            return new(items);

            void Add(string key, Canvas2DSceneGeometry geometry) => items.Add(new(
                Canvas2DSceneObjectIdentity.ForExtension(CompositeProviderId, key), Canvas2DSceneLayer.Decoration,
                items.Count, geometry, part.CreateOrigin(key), clip: geometry.Bounds,
                style: new(fill: "#123456", fontFamily: context.DefaultFontFamily, fontSize: 12)));
        }
    }
}
