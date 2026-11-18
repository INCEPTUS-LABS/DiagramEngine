using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.HitTesting;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Runtime.Documents;
using System.Security.Cryptography;
using System.Text.Json;
using Inceptus.DiagramEngine.Canvas2D.Rendering.Interop;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("vertical")]
    [InlineData("diagonal")]
    public async Task B8RouteDemoQuerySelectsDistinctHostAndRetiresPreviousSample(string sample)
    {
        await using var fixture = await DemoPageFixture.CreateAsync("read-only/");
        var originalHost = Assert.Single(fixture.Activator.ReadOnlyHosts);
        var originalModeler = Assert.Single(fixture.Activator.Modelers);
        await fixture.NavigateAsync($"read-only/?sample={sample}");
        var messageHost = fixture.Activator.ReadOnlyHosts.Last();
        Assert.NotSame(originalHost, messageHost);
        Assert.Equal(sample == "vertical", messageHost.VerticalFlow);
        Assert.Equal(sample == "diagonal", messageHost.DiagonalFlow);
        Assert.False(messageHost.MessageFlow);
        Assert.False(messageHost.OrderProcess);
        Assert.True(ModelerDisposed(originalModeler));
        Assert.Empty(fixture.Activator.EditableHosts);
        // HtmlRenderer does not run browser font preparation. The real-browser scenario
        // verifies the ensuing modeler; this checks the actual route binding and retirement.
        if (sample == "vertical")
            Assert.Contains("Loading the built-in sample", await fixture.MarkupAsync(), StringComparison.Ordinal);
        else
            Assert.Equal(EditingSessionAccessMode.ReadOnly, fixture.Activator.Modelers.Last().AccessMode);
        await fixture.NavigateAsync("read-only/?sample=order");
        var orderHost = fixture.Activator.ReadOnlyHosts.Last();
        Assert.NotSame(messageHost, orderHost);
        Assert.True(orderHost.OrderProcess);
        Assert.False(orderHost.MessageFlow);
        await fixture.NavigateAsync("read-only/?sample=unknown");
        var restored = fixture.Activator.Modelers.Last();
        Assert.Equal(EditingSessionAccessMode.ReadOnly, restored.AccessMode);
        Assert.False(fixture.Activator.ReadOnlyHosts.Last().MessageFlow);
        Assert.NotSame(originalModeler, restored);
    }

    [Theory]
    [InlineData("vertical", false, false, false)]
    [InlineData("vertical", false, false, true)]
    [InlineData("vertical", false, true, false)]
    [InlineData("vertical", false, true, true)]
    [InlineData("vertical", true, false, false)]
    [InlineData("vertical", true, false, true)]
    [InlineData("vertical", true, true, false)]
    [InlineData("vertical", true, true, true)]
    [InlineData("diagonal", false, false, false)]
    [InlineData("diagonal", false, false, true)]
    [InlineData("diagonal", false, true, false)]
    [InlineData("diagonal", false, true, true)]
    [InlineData("diagonal", true, false, false)]
    [InlineData("diagonal", true, false, true)]
    [InlineData("diagonal", true, true, false)]
    [InlineData("diagonal", true, true, true)]
    public async Task B8RouteDemoStrictAttachmentAndCompiledActionsKeepExactOwner(string sample, bool readOnly, bool popup, bool label)
    {
        var source = await PrepareB8RouteDemoAsync(sample);
        var flow = Assert.Single(source.SemanticModel.Relationships);
        Assert.Equal(sample == "vertical" ? "demo:pools:vertical-flow" : "message", flow.Id.Value);
        var visual = source.VisualModel.VisualStates.Single(item => item.SemanticElementId == flow.Id).Id;
        Assert.Equal(sample == "vertical" ? BpmnSemanticTypes.SequenceFlow : BpmnSemanticTypes.MessageFlow, flow.TypeId);
        Assert.Equal(sample == "vertical" ? "demo:pools:prepare" : "a", flow.SourceId.Value);
        Assert.Equal(sample == "vertical" ? "demo:pools:pack" : "b", flow.TargetId.Value);
        var record = Assert.Single(source.VisualModel.EnumerateConnectorRoutingRecords());
        Assert.Equal(sample == "vertical" ? ConnectorRoutingType.Straight : ConnectorRoutingType.Manual, record.RoutingType);
        Assert.Equal(sample == "vertical"
            ? [new PointD(220, 177), new PointD(220, 321)]
            : new[] { new PointD(330, 212), new PointD(930, 580), new PointD(210, 1598.4) }, record.Path);
        var bytes = NativeDocumentSerializer.Export(source);
        var mode = readOnly ? EditingSessionAccessMode.ReadOnly : EditingSessionAccessMode.Editable;
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode, RecordedRouteDemoMetrics(source));
        var modeler = fixture.Renderer.Modeler;
        var retired = modeler.CapturePresentation().Stamp!;
        var loaded = await modeler.SetDocumentAsync(source, new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true, ReprepareIncompatibleRouting = false });
        Assert.True(loaded.Succeeded, NewDiagramDiagnostics(loaded.Diagnostics));
        Assert.NotEqual(retired.AttachmentVersion, modeler.CapturePresentation().Stamp!.AttachmentVersion);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        await fixture.Renderer.EnableReadOnlyMessageToolbarAsync();
        Assert.True((await Session(fixture.Host).UpdateViewportAsync(new ViewportSnapshot(0.5, default))).Succeeded);
        await Session(fixture.Host).WaitForIdleAsync();
        var scene = Session(fixture.Host).CaptureState().CurrentScene!;
        var hit = label
            ? scene.Items.Single(item => item.Origin.VisualStateId == visual &&
                item.Layer == Canvas2DSceneLayer.Label && item.HitTestPolicy.Mode != Canvas2DHitTestMode.None)
            : ApplicationCanonicalRoute(scene, visual);
        var point = Center(hit.Bounds);
        if (!label)
        {
            var path = Canvas2DConnectorPathMetadata.Resolve(hit).Select(hit.Transform.TransformPoint).ToArray();
            point = path.Zip(path.Skip(1), (a, b) => new[] { new PointD((a.X + b.X) / 2, (a.Y + b.Y) / 2),
                new PointD(a.X + (b.X - a.X) / 4, a.Y + (b.Y - a.Y) / 4) }).SelectMany(points => points)
                .First(candidate => new Canvas2DSceneHitTestService().HitTest(scene, candidate)?.SceneObjectId == hit.Id);
        }
        await PointerObserver(fixture.Host).ContextMenuDocumentPointAsync(scene, point);
        var menu = await ApplicationMenuAsync(fixture.Host);
        if (readOnly)
        {
            Assert.False(menu.PropertiesAvailable);
            Assert.Null(menu.DeletionAction);
        }
        var context = menu.ApplicationContext!;
        if (!popup)
        {
            fixture.Host.CloseContextMenu();
            context = (await SmartToolbarAsync(fixture.Host)).Context;
        }
        Assert.Equal(BpmnSemanticTargetKind.Relationship, context.Target.Kind);
        Assert.Equal(flow.Id, context.Target.SemanticId);
        Assert.Equal(visual, context.Target.VisualStateId);
        Assert.Equal(hit.Id, context.Part!.SceneObjectId);
        Assert.Equal(label ? BpmnPresentationPartKind.ConnectorLabel : BpmnPresentationPartKind.ConnectorPath, context.Part.Kind);
        var surface = await fixture.Renderer.CaptureApplicationSurfaceAsync(fixture.Host);
        Assert.Equal("example:inspect", surface.Actions[0]);
        await fixture.Renderer.DispatchApplicationSurfaceAsync(surface.ComponentId, surface.Handlers[0]);
        Assert.Contains("Relationship: " + flow.Id.Value, fixture.Demo.ApplicationDetails, StringComparison.Ordinal);
        Assert.Equal(mode, Session(fixture.Host).CaptureState().AccessMode);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
        var current = modeler.CaptureEditorContext()!;
        if (readOnly) Assert.Equal(BpmnModelerOperationStatus.Rejected, (await modeler.UpdatePublicationAsync(current.AttachmentVersion,
            source.DocumentId, source.Revision, new("forbidden", "Not editable", ""))).Status);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Single(fixture.DocumentChanges);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    private static async Task<DocumentSnapshot> PrepareB8RouteDemoAsync(string sample)
    {
        if (sample == "diagonal") return (await BpmnDemoStartupDocumentProvider.ReadDocumentAsync(
            "Inceptus.DiagramEngine.Blazor.Demo.diagonal-message.inceptus.json", CancellationToken.None)).CaptureSnapshot();
        var authored = await BpmnVerticalFlowDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync();
        var source = authored.CaptureSnapshot();
        await using var measurement = new Canvas2DRenderer(new RecordingRenderExecution(), DemoBpmnImportPreparation.RendererConfiguration);
        Assert.True((await measurement.InitializeAsync("message-sample-measurement", new(1, 1, 1))).Succeeded);
        var prepared = await BpmnVerticalFlowDemoStartupDocumentProvider.PrepareAsync(source, measurement, CancellationToken.None);
        Assert.True(prepared.Succeeded, NewDiagramDiagnostics(prepared.Diagnostics));
        return new(source.SemanticModel, new VisualModelSnapshot(source.DocumentId, source.Revision,
            source.VisualModel.VisualStates, source.VisualModel.ProfileElementPresentations,
            prepared.RoutingScopes, prepared.DocumentRoutingStates), source.Metadata, source.Publication);
    }

    [Theory]
    [InlineData("vertical")]
    [InlineData("diagonal")]
    public async Task B8RouteDemoStrictRejectionPreservesAcceptedSourceAndAttachment(string sample)
    {
        var source = await PrepareB8RouteDemoAsync(sample);
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(replacementExecution: RecordedRouteDemoMetrics(source));
        var modeler = fixture.Renderer.Modeler;
        Assert.True((await modeler.SetDocumentAsync(source, new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true })).Succeeded);
        var accepted = modeler.CapturePresentation().Stamp!;
        var bytes = NativeDocumentSerializer.Export(source);
        var incompatible = Assert.IsType<DocumentSnapshot>(DemoVerificationControls.CreateIncompatibleCandidate(source));
        var result = await modeler.SetDocumentAsync(incompatible,
            new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true, ReprepareIncompatibleRouting = false });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == "INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE");
        Assert.Equal(accepted, modeler.CapturePresentation().Stamp);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task B8RouteDemoDiagonalRejectsDifferentFontMeasurementsWithoutRecovery()
    {
        var source = await PrepareB8RouteDemoAsync("diagonal");
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        var before = fixture.Renderer.Modeler.GetDocument().Snapshot!;
        var stamp = fixture.Renderer.Modeler.CapturePresentation().Stamp;
        var result = await fixture.Renderer.Modeler.SetDocumentAsync(source,
            new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true, ReprepareIncompatibleRouting = false });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == "INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE");
        Assert.Equal(before, fixture.Renderer.Modeler.GetDocument().Snapshot);
        Assert.Equal(stamp, fixture.Renderer.Modeler.CapturePresentation().Stamp);
    }

    private static RecordingRenderExecution RecordedRouteDemoMetrics(DocumentSnapshot source)
    {
        // Replay the fixture's frozen browser measurements at the existing interop seam.
        // This is managed compatibility coverage; real font qualification remains a browser check.
        var measurements = source.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Geometry.TextMeasurements)
            .GroupBy(item => JsonSerializer.Serialize(new Canvas2DTextMeasurementRequestData(item.Request)))
            .ToDictionary(group => group.Key, group => Assert.Single(group.Select(item => item.Metrics).Distinct()));
        return new RecordingRenderExecution
        {
            MeasureTextResultFactory = request =>
            {
                if (!measurements.TryGetValue(JsonSerializer.Serialize(request), out var metrics)) return null;
                return new Canvas2DTextMeasurementInteropResult
                {
                    Succeeded = true,
                    Width = metrics.Width,
                    Ascent = metrics.Ascent,
                    Descent = metrics.Descent,
                    LineHeight = metrics.LineHeight,
                    BoundingX = metrics.BoundingBox.X,
                    BoundingY = metrics.BoundingBox.Y,
                    BoundingWidth = metrics.BoundingBox.Width,
                    BoundingHeight = metrics.BoundingBox.Height,
                    ResolvedFontIdentity = metrics.ResolvedFontIdentity,
                };
            },
        };
    }

    [Fact]
    public async Task B8RouteDemoEmbeddedDiagonalRetainsReviewedNativeBytes()
    {
        await using var stream = typeof(BpmnDemoStartupDocumentProvider).Assembly.GetManifestResourceStream(
            "Inceptus.DiagramEngine.Blazor.Demo.diagonal-message.inceptus.json")!;
        Assert.NotNull(stream);
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        Assert.Equal("C8AB8C02E4F6E5ACF180F180956616BC24807E05BDFC6A6A6D1E555386F3BF31",
            Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
        var source = await PrepareB8RouteDemoAsync("diagonal");
        Assert.Equal("a22:message-flow-acceptance", source.DocumentId.Value);
        Assert.Equal(41UL, source.Revision.Value);
        Assert.All(source.VisualModel.VisualStates, item => Assert.Empty(item.Route));
    }
}
