using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartInspectorReplacementBindingKeepsCapturedTargetAndComponentWhileRetiringAttachment(EditingSessionAccessMode mode)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var modeler = fixture.Renderer.Modeler;
        var source = modeler.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        var example = await OpenInspectorVerificationExampleAsync(modeler);
        var details = example.Details;
        var presentation = modeler.CapturePresentation();
        var stamp = presentation.Stamp!;
        var retiredContext = new BpmnApplicationActionContext(presentation, Assert.Single(presentation.Selection), null, 1, example.Revision);
        var history = Session(fixture.Host).CaptureState().HistoryStatus;
        var changes = 0;
        var focusTransfers = 0;
        var componentId = await fixture.Renderer.RenderInspectorVerificationAsync(example, default,
            EventCallback.Factory.Create(new object(), () => changes++),
            EventCallback.Factory.Create(new object(), () =>
            {
                Assert.Equal(stamp, modeler.CapturePresentation().Stamp);
                Assert.False(example.InspectorLifecycleBusy);
                Assert.True(example.DialogOpen);
                focusTransfers++;
            }));

        await fixture.Renderer.ClickInspectorVerificationAsync(componentId, "replace");

        Assert.True(example.InspectorReplacementResult!.Succeeded);
        Assert.Equal("Succeeded", example.InspectorLifecycleStatus);
        Assert.True(example.InspectorSameComponent);
        Assert.True(example.DialogOpen);
        Assert.Equal(details, example.Details);
        Assert.Equal(1, example.InvocationCount);
        Assert.Equal(1, changes);
        Assert.Equal(1, focusTransfers);
        var before = example.InspectorLifecycleBefore!;
        var after = example.InspectorLifecycleAfter!;
        Assert.Equal(stamp.InstanceId, before.Attachment!.InstanceId);
        Assert.Equal(before.Attachment.InstanceId, after.Attachment!.InstanceId);
        Assert.NotEqual(before.Attachment.AttachmentVersion, after.Attachment.AttachmentVersion);
        Assert.Equal(before.DocumentId, after.DocumentId);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.NativeSha256, after.NativeSha256);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(history, Session(fixture.Host).CaptureState().HistoryStatus);
        Assert.Equal(mode, modeler.CaptureEditorContext()!.Presentation.AccessMode);
        Assert.Equal(BpmnModelerDocumentChangeKind.DocumentReplacement, Assert.Single(fixture.DocumentChanges).Kind);
        Assert.False((await modeler.SelectVisualAsync(stamp, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable,
            (await example.TypedHandler(new("example:inspect", retiredContext, CancellationToken.None))).Status);
        Assert.Equal(1, example.InvocationCount);
        Assert.Equal(details, example.Details);
        Assert.False(ModelerDisposed(modeler));
        if (mode == EditingSessionAccessMode.ReadOnly)
        {
            var current = modeler.CaptureEditorContext()!;
            var mutation = await modeler.UpdatePublicationAsync(current.AttachmentVersion, source.DocumentId,
                source.Revision, new("forbidden", "Still read-only", ""));
            Assert.Equal(BpmnModelerOperationStatus.Rejected, mutation.Status);
            Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        }
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartInspectorUnmountBindingRetainsApplicationDialogWithoutClaimingCleanup(EditingSessionAccessMode mode)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        InceptusBpmnModeler? current = fixture.Renderer.Modeler;
        var example = await OpenInspectorVerificationExampleAsync(current, () => current);
        var details = example.Details;
        var source = current.GetDocument().Snapshot;
        var unmounts = 0;
        var changes = 0;
        var focusTransfers = 0;
        var componentId = await fixture.Renderer.RenderInspectorVerificationAsync(example,
            EventCallback.Factory.Create(new object(), () =>
            {
                Assert.Equal(1, focusTransfers);
                unmounts++;
                current = null;
            }),
            EventCallback.Factory.Create(new object(), () => changes++),
            EventCallback.Factory.Create(new object(), () =>
            {
                Assert.NotNull(current);
                Assert.False(example.InspectorLifecycleBusy);
                Assert.True(example.DialogOpen);
                focusTransfers++;
            }));

        await fixture.Renderer.ClickInspectorVerificationAsync(componentId, "unmount");

        Assert.Equal(1, unmounts);
        Assert.Equal(1, changes);
        Assert.Equal(1, focusTransfers);
        Assert.Equal("UnmountRequested", example.InspectorLifecycleStatus);
        Assert.Contains("cleanup completion is not observed", example.InspectorLifecycleFeedback, StringComparison.Ordinal);
        Assert.NotNull(example.InspectorLifecycleBefore!.Attachment);
        Assert.Null(example.InspectorLifecycleAfter!.Attachment);
        Assert.Null(example.InspectorReplacementResult);
        Assert.False(example.InspectorSameComponent);
        Assert.True(example.DialogOpen);
        Assert.Equal(details, example.Details);
        Assert.Same(source, fixture.Renderer.Modeler.GetDocument().Snapshot);
        Assert.Empty(fixture.DocumentChanges);
        // The parent performs normal conditional removal in the real Demo. This existing
        // renderer fixture removes its root after the event batch; neither observation
        // is promoted to browser asynchronous-cleanup evidence.
        await fixture.Renderer.RemoveVerificationModelerAsync();
        Assert.True(ModelerDisposed(fixture.Renderer.Modeler));
        Assert.True(example.DialogOpen);
        Assert.Equal(details, example.Details);
        Assert.False(example.CanUseInspectorLifecycle);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartInspectorLifecycleControlsRequireOptInAndOpenDialog(EditingSessionAccessMode mode)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var modeler = fixture.Renderer.Modeler;
        var example = new DemoApplicationActionExample(() => modeler);
        var source = modeler.GetDocument().Snapshot;
        var attachment = modeler.CaptureDecorationAttachment();
        var callbacks = 0;
        var callback = EventCallback.Factory.Create(new object(), () => callbacks++);
        var componentId = await fixture.Renderer.RenderInspectorVerificationAsync(example, callback);
        await fixture.Renderer.AssertInspectorVerificationHiddenAsync(componentId);
        await example.ReplaceFromInspectorAsync();
        await example.UnmountFromInspectorAsync(callback);
        example.SetVerificationEnabled(true);
        Assert.False(example.DialogOpen);
        await example.ReplaceFromInspectorAsync();
        await example.UnmountFromInspectorAsync(callback);
        Assert.Equal(0, callbacks);
        Assert.Null(example.InspectorLifecycleBefore);
        Assert.Null(example.InspectorReplacementResult);
        Assert.Equal("Idle", example.InspectorLifecycleStatus);
        Assert.Same(source, modeler.GetDocument().Snapshot);
        Assert.Equal(attachment, modeler.CaptureDecorationAttachment());
        Assert.Empty(fixture.DocumentChanges);

        // Dispatch a previously rendered button after its dialog guard has changed.
        // An obsolete event must not move focus or request any lifecycle operation.
        var opened = await OpenInspectorVerificationExampleAsync(modeler);
        var focusTransfers = 0;
        var guardedChanges = 0;
        var openedId = await fixture.Renderer.RenderInspectorVerificationAsync(opened, callback,
            EventCallback.Factory.Create(new object(), () => guardedChanges++),
            EventCallback.Factory.Create(new object(), () => focusTransfers++));
        opened.CloseDialog();
        await fixture.Renderer.ClickInspectorVerificationAsync(openedId, "replace");
        // The first event rerenders disabled controls, so invoke the originally captured
        // unmount event separately through a fresh open component below.
        opened = await OpenInspectorVerificationExampleAsync(modeler);
        openedId = await fixture.Renderer.RenderInspectorVerificationAsync(opened, callback,
            EventCallback.Factory.Create(new object(), () => guardedChanges++),
            EventCallback.Factory.Create(new object(), () => focusTransfers++));
        opened.CloseDialog();
        await fixture.Renderer.ClickInspectorVerificationAsync(openedId, "unmount");
        Assert.Equal(0, focusTransfers);
        Assert.Equal(0, guardedChanges);
        Assert.Equal(0, callbacks);
        Assert.Equal(attachment, modeler.CaptureDecorationAttachment());
        Assert.Empty(fixture.DocumentChanges);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Theory]
    [InlineData("", EditingSessionAccessMode.Editable)]
    [InlineData("simulation/", EditingSessionAccessMode.ReadOnly)]
    public async Task SmartInspectorHostBindingUsesNormalParentUnmountAndRetainsApplicationOwner(string path, EditingSessionAccessMode mode)
    {
        await using var fixture = await DemoPageFixture.CreateAsync(path);
        await fixture.EnableMaintenanceAsync();
        var modeler = Assert.Single(fixture.Activator.Modelers);
        var layout = Assert.Single(fixture.Activator.Layouts);
        var lifecycle = Assert.Single(fixture.Activator.VerificationControls);
        var host = mode == EditingSessionAccessMode.ReadOnly
            ? (ComponentBase)Assert.Single(fixture.Activator.ReadOnlyHosts)
            : Assert.Single(fixture.Activator.EditableHosts);
        var binding = CaptureInspectorHostBinding(host);
        Assert.True(binding.CanUnmount);
        Assert.True(binding.Unmount.HasDelegate);
        await fixture.Renderer.Dispatcher.InvokeAsync(() => binding.Unmount.InvokeAsync());
        Assert.True(ModelerDisposed(modeler));
        Assert.False(lifecycle.Mounted);
        var retiredBinding = CaptureInspectorHostBinding(host);
        Assert.False(retiredBinding.CanUnmount);
        Assert.Same(binding.Example, retiredBinding.Example);
        await fixture.Renderer.Dispatcher.InvokeAsync(() => lifecycle.MountRequested.InvokeAsync());
        var replacement = fixture.Activator.Modelers.Last();
        Assert.NotSame(modeler, replacement);
        Assert.Equal(mode, replacement.AccessMode);
        Assert.Same(binding.Example, CaptureInspectorHostBinding(host).Example);
        Assert.Same(layout, Assert.Single(fixture.Activator.Layouts));
        Assert.Empty(fixture.Navigation.Navigations);
    }

    private static async Task<DemoApplicationActionExample> OpenInspectorVerificationExampleAsync(
        InceptusBpmnModeler modeler, Func<InceptusBpmnModeler?>? current = null)
    {
        var presentation = modeler.CapturePresentation();
        Assert.True(presentation.IsAvailable);
        // Reuse the coherent target when the preceding lifecycle guard left it selected.
        // Opening another application inspector does not require another native activation.
        if (presentation.Selection.Length != 1 || presentation.Selection[0].VisualStateId != BpmnDemoPipeline.TaskVisualId)
        {
            var selection = await modeler.SelectVisualAsync(presentation.Stamp!, BpmnDemoPipeline.TaskVisualId);
            Assert.True(selection.Succeeded, $"Selection setup: {selection.Status}; {NewDiagramDiagnostics(selection.Diagnostics)}");
            presentation = modeler.CapturePresentation();
        }
        Assert.Equal(BpmnDemoPipeline.TaskVisualId, Assert.Single(presentation.Selection).VisualStateId);
        var example = new DemoApplicationActionExample(current ?? (() => modeler));
        example.SetVerificationEnabled(true);
        var context = new BpmnApplicationActionContext(presentation, Assert.Single(presentation.Selection), null, 1, example.Revision);
        Assert.True((await example.TypedHandler(new("example:inspect", context, CancellationToken.None))).Succeeded);
        return example;
    }

#pragma warning disable BL0006 // Reuse canonical compiled Razor frame/event fixtures; no browser harness or private runtime path.
    private static (DemoApplicationActionExample Example, bool CanUnmount, EventCallback Unmount) CaptureInspectorHostBinding(ComponentBase host)
    {
        using var outer = new RenderTreeBuilder();
        host.GetType().GetMethod("BuildRenderTree", PropertiesUxFlags)!.Invoke(host, [outer]);
        var range = outer.GetFrames();
        var content = Assert.IsType<RenderFragment>(Assert.Single(range.Array.Take(range.Count), frame =>
            frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == nameof(DemoApplicationLayout.ChildContent)).AttributeValue);
        using var child = new RenderTreeBuilder();
        content(child);
        var frames = child.GetFrames();
        var index = Array.FindIndex(frames.Array, 0, frames.Count, frame =>
            frame.FrameType == RenderTreeFrameType.Component && frame.ComponentType == typeof(DemoApplicationActions));
        using var maintenance = new RenderTreeBuilder();
        if (index < 0)
        {
            var nested = Assert.IsType<RenderFragment>(Assert.Single(frames.Array.Take(frames.Count), frame =>
                frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == nameof(DemoMaintenance.ChildContent)).AttributeValue);
            nested(maintenance);
            frames = maintenance.GetFrames();
            index = Array.FindIndex(frames.Array, 0, frames.Count, frame =>
                frame.FrameType == RenderTreeFrameType.Component && frame.ComponentType == typeof(DemoApplicationActions));
        }
        Assert.True(index >= 0);
        var attributes = frames.Array.Skip(index + 1).TakeWhile(frame => frame.FrameType == RenderTreeFrameType.Attribute).ToArray();
        return (Assert.IsType<DemoApplicationActionExample>(Assert.Single(attributes, frame => frame.AttributeName == nameof(DemoApplicationActions.Example)).AttributeValue),
            Assert.IsType<bool>(Assert.Single(attributes, frame => frame.AttributeName == nameof(DemoApplicationActions.CanUnmount)).AttributeValue),
            Assert.IsType<EventCallback>(Assert.Single(attributes, frame => frame.AttributeName == nameof(DemoApplicationActions.UnmountRequested)).AttributeValue));
    }

    private sealed partial class ApplicationComponentRenderer
    {
        internal Task<int> RenderInspectorVerificationAsync(DemoApplicationActionExample example,
            EventCallback unmountRequested, EventCallback changed = default, EventCallback beforeOperation = default) => Dispatcher.InvokeAsync(async () =>
        {
            var component = (DemoApplicationInspectorVerification)InstantiateComponent(typeof(DemoApplicationInspectorVerification));
            var id = AssignRootComponentId(component);
            await RenderRootComponentAsync(id, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DemoApplicationInspectorVerification.Example)] = example,
                [nameof(DemoApplicationInspectorVerification.CanUnmount)] = true,
                [nameof(DemoApplicationInspectorVerification.UnmountRequested)] = unmountRequested,
                [nameof(DemoApplicationInspectorVerification.BeforeOperation)] = beforeOperation,
                [nameof(DemoApplicationInspectorVerification.Changed)] = changed,
            }));
            return id;
        });

        internal Task ClickInspectorVerificationAsync(int id, string action) => Dispatcher.InvokeAsync(async () =>
        {
            var attributes = AnnotationAttributes(id, "button", "data-inspector-verification-action", action);
            Assert.DoesNotContain(attributes, item => item.AttributeName == "disabled" && Equals(item.AttributeValue, true));
            var handler = Assert.Single(attributes, item => item.AttributeName == "onclick").AttributeEventHandlerId;
            Assert.NotEqual(0UL, handler);
            await DispatchEventAsync(handler, null, new MouseEventArgs());
        });

        internal Task AssertInspectorVerificationHiddenAsync(int id) => Dispatcher.InvokeAsync(() =>
        {
            var frames = GetCurrentRenderTreeFrames(id);
            Assert.DoesNotContain(frames.Array.Take(frames.Count), frame => frame.FrameType == RenderTreeFrameType.Element && frame.ElementName == "button");
        });
    }
#pragma warning restore BL0006
}
