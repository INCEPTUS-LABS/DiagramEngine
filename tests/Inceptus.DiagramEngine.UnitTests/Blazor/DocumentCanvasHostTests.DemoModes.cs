using System.Text.Json;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Export;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.JSInterop;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task ThreeModeNativeInspectionFollowsElementsRelationshipsAndClearWithoutSmartOrNativeMutation()
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(nativeInformation: true);
        var modeler = fixture.Renderer.Modeler;
        var source = modeler.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        Assert.False(modeler.ApplicationContextualActionsEnabled);
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        var task = Assert.Single(fixture.Demo.InformationTargets);
        Assert.Equal(BpmnSemanticTargetKind.Element, task.Kind);
        Assert.Equal(source, fixture.Demo.InformationDocument);
        Assert.Contains(DemoNativeInformation.Fields(source, task), field => field.Key == "Type");
        var relationship = source.SemanticModel.Relationships.Single(item => item.Id == BpmnDemoPipeline.FirstSequenceFlowId);
        var visual = source.VisualModel.VisualStates.First(item => item.SemanticElementId == relationship.Id);
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, visual.Id)).Succeeded);
        var flow = Assert.Single(fixture.Demo.InformationTargets);
        Assert.Equal(BpmnSemanticTargetKind.Relationship, flow.Kind);
        Assert.Equal(relationship.Id, flow.SemanticId);
        var fields = DemoNativeInformation.Fields(source, flow).ToDictionary();
        Assert.Contains(relationship.SourceId.Value, fields["Source"], StringComparison.Ordinal);
        Assert.Contains(relationship.TargetId.Value, fields["Target"], StringComparison.Ordinal);
        await Session(fixture.Host).UpdateEditorStateAsync(new EditorStateSnapshot(selection: [BpmnDemoPipeline.TaskVisualId, BpmnDemoPipeline.StartEventVisualId]));
        await Session(fixture.Host).WaitForIdleAsync();
        await fixture.Renderer.Dispatcher.InvokeAsync(() => modeler.SelectionChanged.InvokeAsync(new(modeler.CapturePresentation())));
        Assert.Equal(2, fixture.Demo.InformationTargets.Length);
        await Session(fixture.Host).UpdateEditorStateAsync(new EditorStateSnapshot());
        await Session(fixture.Host).WaitForIdleAsync();
        await fixture.Renderer.Dispatcher.InvokeAsync(() => modeler.SelectionChanged.InvokeAsync(new(modeler.CapturePresentation())));
        Assert.Empty(fixture.Demo.InformationTargets);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
        Assert.Empty(fixture.DocumentChanges);
    }

    [Fact]
    public async Task ThreeModeNativeInspectionPreservesCancelledSourceAndRetiresIdenticalReplacement()
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(nativeInformation: true);
        var modeler = fixture.Renderer.Modeler;
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        var stamp = modeler.CapturePresentation().Stamp!;
        var target = Assert.Single(fixture.Demo.InformationTargets);
        var source = fixture.Demo.InformationDocument!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await modeler.SetDocumentAsync(source, new() { VerifySavedRouting = true }, cancellation.Token);
        Assert.Equal(BpmnModelerOperationStatus.Cancelled, result.Status);
        fixture.Demo.RefreshPresentation();
        Assert.Equal(stamp, modeler.CapturePresentation().Stamp);
        var retained = Assert.Single(fixture.Demo.InformationTargets);
        Assert.Equal(target.SemanticId, retained.SemanticId);
        Assert.Equal(target.Kind, retained.Kind);
        Assert.Equal(target.VisualStateId, retained.VisualStateId);
        Assert.True(target.Parts.SequenceEqual(retained.Parts));
        Assert.Equal(source, fixture.Demo.InformationDocument);
        await fixture.Demo.ReplaceAsync(source);
        Assert.NotEqual(stamp.AttachmentVersion, modeler.CapturePresentation().Stamp!.AttachmentVersion);
        Assert.Empty(fixture.Demo.InformationTargets);
        Assert.Equal(source, fixture.Demo.InformationDocument);
    }

    [Fact]
    public async Task ThreeModeRoutingRetiresIndependentInstancesAndDoesNotEnableEditableVerticalStartup()
    {
        await using var fixture = await DemoPageFixture.CreateAsync("?sample=vertical");
        Assert.Single(fixture.Activator.EditableHosts);
        Assert.Empty(fixture.Activator.ReadOnlyHosts);
        var editable = Assert.Single(fixture.Activator.Modelers);
        await fixture.NavigateAsync("read-only/");
        var viewer = fixture.Activator.Modelers.Last();
        await fixture.NavigateAsync("simulation/");
        var simulation = fixture.Activator.Modelers.Last();
        Assert.True(ModelerDisposed(editable));
        Assert.True(ModelerDisposed(viewer));
        Assert.NotSame(viewer, simulation);
        Assert.Equal(EditingSessionAccessMode.ReadOnly, simulation.AccessMode);
        Assert.True(fixture.Activator.ReadOnlyHosts.Last().SimulationExamples);
        await fixture.NavigateAsync("read-only/");
        Assert.True(ModelerDisposed(simulation));
        Assert.False(fixture.Activator.ReadOnlyHosts.Last().SimulationExamples);
        Assert.DoesNotContain("Annotation example", await fixture.MarkupAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, GraphicalExportFormat.Svg)]
    [InlineData(EditingSessionAccessMode.ReadOnly, GraphicalExportFormat.Svg)]
    [InlineData(EditingSessionAccessMode.Editable, GraphicalExportFormat.Png)]
    [InlineData(EditingSessionAccessMode.ReadOnly, GraphicalExportFormat.Png)]
    public async Task ThreeModeSharedImageDownloadPreservesNativeBytesHistoryAndSaveState(EditingSessionAccessMode mode, GraphicalExportFormat format)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var modeler = fixture.Renderer.Modeler;
        var source = NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!);
        var before = modeler.NativeSaveStatus;
        var history = Session(fixture.Host).CaptureState().HistoryStatus;
        var runtime = new DemoImageRuntime();
        var result = await DemoGraphicalExport.DownloadAsync(modeler, runtime, format, () => true, CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(result.Artifact!.Content.ToArray(), Assert.IsType<byte[]>(Assert.Single(runtime.Downloads)[0]));
        Assert.Equal(format == GraphicalExportFormat.Svg ? "image/svg+xml" : "image/png", runtime.Downloads[0][1]);
        Assert.True(source.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(history, Session(fixture.Host).CaptureState().HistoryStatus);
        Assert.Equal(before!.IsDirty, modeler.NativeSaveStatus!.IsDirty);
        Assert.Equal(before.SavedCheckpoint, modeler.NativeSaveStatus.SavedCheckpoint);
        Assert.Equal(before.CurrentCheckpoint.DocumentId, modeler.NativeSaveStatus.CurrentCheckpoint.DocumentId);
        Assert.Equal(before.CurrentCheckpoint.Revision, modeler.NativeSaveStatus.CurrentCheckpoint.Revision);
        Assert.Empty(fixture.DocumentChanges);
    }

    [Theory]
    [InlineData("retired-host")]
    [InlineData("replacement")]
    [InlineData("cancelled")]
    [InlineData("browser-failure")]
    public async Task ThreeModeSharedImageDownloadRejectsRetiredOrFailedDelivery(string scenario)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        var modeler = fixture.Renderer.Modeler;
        var runtime = new DemoImageRuntime();
        using var cancellation = new CancellationTokenSource();
        runtime.BeforeResources = async () =>
        {
            if (scenario == "replacement") await fixture.Demo.ReplaceAsync(modeler.GetDocument().Snapshot!);
            if (scenario == "cancelled") cancellation.Cancel();
            if (scenario == "browser-failure") throw new JSException("resource unavailable");
        };
        var result = await DemoGraphicalExport.DownloadAsync(modeler, runtime, GraphicalExportFormat.Svg,
            () => scenario != "retired-host", cancellation.Token);
        Assert.Equal(scenario == "browser-failure" ? GraphicalExportStatus.Failed : GraphicalExportStatus.Cancelled, result.Status);
        if (scenario == "browser-failure") Assert.NotEmpty(result.Diagnostics);
        Assert.Empty(runtime.Downloads);
    }

    [Fact]
    public async Task ThreeModeMaintenanceOptInUsesCompiledBindingAndRetainsNoOrdinaryDiagnostics()
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        await fixture.Renderer.ExerciseMaintenanceBindingAsync();
        Assert.Empty(fixture.Renderer.Exceptions);
    }

#pragma warning disable BL0006 // Extend the canonical renderer with real compiled maintenance events.
    private sealed partial class ApplicationComponentRenderer
    {
        internal Task ExerciseMaintenanceBindingAsync() => Dispatcher.InvokeAsync(async () =>
        {
            var component = (DemoMaintenance)InstantiateComponent(typeof(DemoMaintenance));
            var id = AssignRootComponentId(component);
            bool? enabled = null;
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DemoMaintenance.EnabledChanged)] = EventCallback.Factory.Create<bool>(new object(), value => enabled = value),
                [nameof(DemoMaintenance.ChildContent)] = (RenderFragment)(builder => builder.AddContent(0, "Maintenance sentinel"))
            });
            await RenderRootComponentAsync(id, parameters);
            Assert.DoesNotContain(GetCurrentRenderTreeFrames(id).Array.Take(GetCurrentRenderTreeFrames(id).Count),
                frame => frame.FrameType == RenderTreeFrameType.Text && frame.TextContent == "Maintenance sentinel");
            var handler = Assert.Single(AnnotationAttributes(id, "input"), item => item.AttributeName == "onchange").AttributeEventHandlerId;
            Assert.NotEqual(0UL, handler);
            await DispatchEventAsync(handler, null, new ChangeEventArgs { Value = true });
            Assert.True(enabled);
            await RenderRootComponentAsync(id, ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(DemoMaintenance.Enabled)] = true }));
            Assert.Contains(GetCurrentRenderTreeFrames(id).Array.Take(GetCurrentRenderTreeFrames(id).Count),
                frame => frame.FrameType == RenderTreeFrameType.Text && frame.TextContent == "Maintenance sentinel");
        });
    }
#pragma warning restore BL0006

    private sealed class DemoImageRuntime : IJSRuntime, IJSObjectReference
    {
        internal List<object?[]> Downloads { get; } = [];
        internal Func<Task>? BeforeResources { get; set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier is "import" or "createGraphicalExport") return (TValue)(object)this;
            if (identifier == "resources")
            {
                if (BeforeResources is { } before) await before();
                return JsonSerializer.Deserialize<TValue>("{\"Images\":{},\"Fonts\":{}}")!;
            }
            if (identifier == "png") return (TValue)(object)new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            if (identifier == "downloadFile") Downloads.Add(args!);
            return default!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
