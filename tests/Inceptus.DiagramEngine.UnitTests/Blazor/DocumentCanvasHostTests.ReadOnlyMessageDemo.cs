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
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task ReadOnlyMessageDemoQuerySelectsDistinctHostAndRetiresPreviousSample()
    {
        await using var fixture = await DemoPageFixture.CreateAsync("read-only/");
        var originalHost = Assert.Single(fixture.Activator.ReadOnlyHosts);
        var originalModeler = Assert.Single(fixture.Activator.Modelers);
        await fixture.NavigateAsync("read-only/?sample=message");
        var messageHost = fixture.Activator.ReadOnlyHosts.Last();
        Assert.NotSame(originalHost, messageHost);
        Assert.True(messageHost.MessageFlow);
        Assert.False(messageHost.OrderProcess);
        Assert.True(ModelerDisposed(originalModeler));
        Assert.Empty(fixture.Activator.EditableHosts);
        // HtmlRenderer does not run browser font preparation. The real-browser scenario
        // verifies the ensuing modeler; this checks the actual route binding and retirement.
        Assert.Contains("Loading the built-in sample", await fixture.MarkupAsync(), StringComparison.Ordinal);
        await fixture.NavigateAsync("read-only/?sample=order");
        var orderHost = fixture.Activator.ReadOnlyHosts.Last();
        Assert.NotSame(messageHost, orderHost);
        Assert.True(orderHost.OrderProcess);
        Assert.False(orderHost.MessageFlow);
        await fixture.NavigateAsync("read-only/");
        var restored = fixture.Activator.Modelers.Last();
        Assert.Equal(EditingSessionAccessMode.ReadOnly, restored.AccessMode);
        Assert.False(fixture.Activator.ReadOnlyHosts.Last().MessageFlow);
        Assert.NotSame(originalModeler, restored);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReadOnlyMessageDemoStrictAttachmentAndCompiledActionsKeepExactPathOrLabelOwner(bool popup, bool label)
    {
        var source = await PrepareReadOnlyMessageDemoAsync();
        var flow = Assert.Single(source.SemanticModel.Relationships);
        Assert.Equal(BpmnMessageFlowDemoStartupDocumentProvider.MessageId, flow.Id);
        Assert.Equal(BpmnSemanticTypes.MessageFlow, flow.TypeId);
        Assert.Equal("demo:pools:send", flow.SourceId.Value);
        Assert.Equal("demo:pools:receive", flow.TargetId.Value);
        Assert.Single(source.VisualModel.EnumerateConnectorRoutingRecords());
        var bytes = NativeDocumentSerializer.Export(source);
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        var modeler = fixture.Renderer.Modeler;
        var retired = modeler.CapturePresentation().Stamp!;
        var loaded = await modeler.SetDocumentAsync(source, new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true });
        Assert.True(loaded.Succeeded, NewDiagramDiagnostics(loaded.Diagnostics));
        Assert.NotEqual(retired.AttachmentVersion, modeler.CapturePresentation().Stamp!.AttachmentVersion);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        await fixture.Renderer.EnableReadOnlyMessageToolbarAsync();
        var scene = Session(fixture.Host).CaptureState().CurrentScene!;
        var hit = label
            ? scene.Items.Single(item => item.Origin.VisualStateId == BpmnMessageFlowDemoStartupDocumentProvider.MessageVisualId &&
                item.Layer == Canvas2DSceneLayer.Label && item.HitTestPolicy.Mode != Canvas2DHitTestMode.None)
            : ApplicationCanonicalRoute(scene, BpmnMessageFlowDemoStartupDocumentProvider.MessageVisualId);
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
        Assert.False(menu.PropertiesAvailable);
        Assert.Null(menu.DeletionAction);
        var context = menu.ApplicationContext!;
        if (!popup)
        {
            fixture.Host.CloseContextMenu();
            context = (await SmartToolbarAsync(fixture.Host)).Context;
        }
        Assert.Equal(BpmnSemanticTargetKind.Relationship, context.Target.Kind);
        Assert.Equal(flow.Id, context.Target.SemanticId);
        Assert.Equal(BpmnMessageFlowDemoStartupDocumentProvider.MessageVisualId, context.Target.VisualStateId);
        Assert.Equal(hit.Id, context.Part!.SceneObjectId);
        Assert.Equal(label ? BpmnPresentationPartKind.ConnectorLabel : BpmnPresentationPartKind.ConnectorPath, context.Part.Kind);
        var surface = await fixture.Renderer.CaptureApplicationSurfaceAsync(fixture.Host);
        Assert.Equal("example:inspect", surface.Actions[0]);
        await fixture.Renderer.DispatchApplicationSurfaceAsync(surface.ComponentId, surface.Handlers[0]);
        Assert.Contains("Relationship: demo:pools:order-message", fixture.Demo.ApplicationDetails, StringComparison.Ordinal);
        Assert.Equal(EditingSessionAccessMode.ReadOnly, Session(fixture.Host).CaptureState().AccessMode);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
        var current = modeler.CaptureEditorContext()!;
        Assert.Equal(BpmnModelerOperationStatus.Rejected, (await modeler.UpdatePublicationAsync(current.AttachmentVersion,
            source.DocumentId, source.Revision, new("forbidden", "Not editable", ""))).Status);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Single(fixture.DocumentChanges);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    private static async Task<DocumentSnapshot> PrepareReadOnlyMessageDemoAsync()
    {
        var authored = await BpmnMessageFlowDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync();
        var source = authored.CaptureSnapshot();
        await using var measurement = new Canvas2DRenderer(new RecordingRenderExecution(), DemoBpmnImportPreparation.RendererConfiguration);
        Assert.True((await measurement.InitializeAsync("message-sample-measurement", new(1, 1, 1))).Succeeded);
        var prepared = await BpmnMessageFlowDemoStartupDocumentProvider.PrepareAsync(source, measurement, CancellationToken.None);
        Assert.True(prepared.Succeeded, NewDiagramDiagnostics(prepared.Diagnostics));
        return new(source.SemanticModel, new VisualModelSnapshot(source.DocumentId, source.Revision,
            source.VisualModel.VisualStates, source.VisualModel.ProfileElementPresentations,
            prepared.RoutingScopes, prepared.DocumentRoutingStates), source.Metadata, source.Publication);
    }

#pragma warning disable BL0006 // Reuse the canonical renderer to update the real public modeler parameter.
    private sealed partial class ApplicationComponentRenderer
    {
        internal Task EnableReadOnlyMessageToolbarAsync() => Dispatcher.InvokeAsync(() =>
            RenderRootComponentAsync(_modelerComponentId, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InceptusBpmnModeler.ApplicationContextualActionsEnabled)] = true,
            })));
    }
#pragma warning restore BL0006
}
