using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, "replace-identical", BpmnModelerOperationStatus.Succeeded)]
    [InlineData(EditingSessionAccessMode.Editable, "replace-cancelled", BpmnModelerOperationStatus.Cancelled)]
    [InlineData(EditingSessionAccessMode.Editable, "replace-incompatible", BpmnModelerOperationStatus.Rejected)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "replace-identical", BpmnModelerOperationStatus.Succeeded)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "replace-cancelled", BpmnModelerOperationStatus.Cancelled)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "replace-incompatible", BpmnModelerOperationStatus.Rejected)]
    public async Task DemoVerificationReplacementControlsUseCurrentAttachmentAndStrictLoading(
        EditingSessionAccessMode mode, string action, BpmnModelerOperationStatus expected)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var modeler = fixture.Renderer.Modeler;
        var source = modeler.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        var attachment = modeler.CaptureDecorationAttachment()!;
        var session = Session(fixture.Host);
        var history = session.CaptureState().HistoryStatus;
        AssertDecorationPresented(await modeler.ReplaceDecorationsAsync(attachment, 7,
            DemoAnnotationProvider.CreateInput("Retained verification")), 7, true);
        var (control, componentId) = await fixture.Renderer.RenderVerificationControlsAsync(allowReplacement: true);
        await fixture.Renderer.EnableVerificationControlsAsync(componentId);
        await fixture.Renderer.ClickVerificationAsync(componentId, action);

        Assert.Equal(expected, control.DocumentResult!.Status);
        Assert.Equal(expected.ToString(), control.Status);
        Assert.True(control.SameComponent);
        Assert.Same(modeler, control.Modeler);
        Assert.Equal(attachment, control.SnapshotBefore!.Attachment);
        Assert.Equal(7, control.SnapshotBefore.InputRevision);
        Assert.Equal(source.DocumentId.Value, control.SnapshotAfter!.DocumentId);
        Assert.Equal(source.Revision.Value, control.SnapshotAfter.DocumentRevision);
        Assert.Equal(control.SnapshotBefore.NativeSha256, control.SnapshotAfter.NativeSha256);
        Assert.Equal(source, modeler.GetDocument().Snapshot);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(mode, modeler.CaptureEditorContext()!.Presentation.AccessMode);
        Assert.Equal(history, Session(fixture.Host).CaptureState().HistoryStatus);
        Assert.False(ModelerDisposed(modeler));

        if (expected == BpmnModelerOperationStatus.Succeeded)
        {
            Assert.NotSame(session, Session(fixture.Host));
            Assert.True(session.CaptureState().IsClosed);
            Assert.NotEqual(attachment, control.SnapshotAfter.Attachment);
            Assert.Equal(attachment.InstanceId, control.SnapshotAfter.Attachment!.InstanceId);
            Assert.Equal(0, control.SnapshotAfter.InputRevision);
            Assert.Equal(BpmnModelerDocumentChangeKind.DocumentReplacement, Assert.Single(fixture.DocumentChanges).Kind);
            await fixture.Host.ZoomInAsync();
            Assert.Equal(0, modeler.CaptureEditorContext()!.Presentation.DecorationInputRevision);
            Assert.False(modeler.CaptureEditorContext()!.Presentation.DecorationPresentation?.DecorationsIncluded == true);
            await fixture.Renderer.ClickVerificationAsync(componentId, "stale-handle");
            Assert.Equal(Canvas2DDecorationUpdateStatus.Rejected, control.DecorationResult!.Status);
            Assert.False(control.DecorationResult.Accepted);
            Assert.Equal(0, modeler.CaptureEditorContext()!.Presentation.DecorationInputRevision);
            await fixture.Renderer.UpdateFreshDemoAnnotationsAsync();
            var fresh = modeler.CaptureEditorContext()!.Presentation;
            Assert.Equal(1, fresh.DecorationInputRevision);
            Assert.True(fresh.DecorationPresentation!.FramePresented);
            Assert.True(fresh.DecorationPresentation.DecorationsIncluded);
        }
        else
        {
            Assert.Same(session, Session(fixture.Host));
            Assert.Same(source, modeler.GetDocument().Snapshot);
            Assert.Equal(attachment, modeler.CaptureDecorationAttachment());
            Assert.Equal(7, control.SnapshotAfter.InputRevision);
            Assert.Empty(fixture.DocumentChanges);
            if (action == "replace-incompatible")
                Assert.Contains(control.DocumentResult.Diagnostics, diagnostic =>
                    diagnostic.Code == "INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE");
            var oldFrame = fixture.Execution.LastContent;
            await fixture.Host.ZoomInAsync();
            Assert.NotSame(oldFrame, fixture.Execution.LastContent);
            Assert.Contains(fixture.Execution.LastContent!.Items, item => item.Content == "Retained verification");
            Assert.Equal(7, modeler.CaptureEditorContext()!.Presentation.DecorationInputRevision);
            Assert.True(modeler.CaptureEditorContext()!.Presentation.DecorationPresentation!.DecorationsIncluded);
            AssertDecorationPresented(await modeler.ReplaceDecorationsAsync(attachment, 8,
                DemoAnnotationProvider.CreateInput("Still accepted")), 8, true);
            AssertDecorationPresented(await modeler.ClearDecorationsAsync(attachment, 9), 9, false);
        }

        // Verification controls do not alter the native ReadOnly command boundary.
        if (mode == EditingSessionAccessMode.ReadOnly)
        {
            var current = modeler.CaptureEditorContext()!;
            var mutation = await modeler.UpdatePublicationAsync(current.AttachmentVersion, source.DocumentId,
                source.Revision, new("forbidden", "Still read-only", ""));
            Assert.Equal(BpmnModelerOperationStatus.Rejected, mutation.Status);
        }
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false)]
    [InlineData(EditingSessionAccessMode.Editable, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true)]
    public async Task DemoVerificationRecordsActualPublicTaskStateAtUnmountRequest(EditingSessionAccessMode mode, bool delayRender)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var unmountRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unmountCount = 0;
        var (control, componentId) = await fixture.Renderer.RenderVerificationControlsAsync(allowReplacement: true,
            EventCallback.Factory.Create(new object(), () =>
            {
                unmountCount++;
                unmountRequested.TrySetResult();
            }));
        await fixture.Renderer.EnableVerificationControlsAsync(componentId);
        fixture.Execution.BlockRender = delayRender;
        var operation = fixture.Renderer.ClickVerificationAsync(componentId, "update-unmount");
        try
        {
            var first = await Task.WhenAny(unmountRequested.Task, operation).WaitAsync(TimeSpan.FromSeconds(10));
            if (ReferenceEquals(first, operation))
            {
                await operation;
                Assert.Empty(fixture.Renderer.Exceptions);
                Assert.True(unmountRequested.Task.IsCompleted, "The compiled update/unmount handler completed without requesting unmount.");
            }
            await unmountRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(delayRender, control.PendingAtUnmountRequest);
            Assert.True(control.AcceptedAtUnmountRequest);
            Assert.NotNull(control.PendingDecorationOperation);
            Assert.Equal(!delayRender, control.PendingDecorationOperation.IsCompleted);
            Assert.Equal(0, control.SnapshotBefore!.InputRevision);
            // Root removal must enter a fresh renderer dispatch after the event's render
            // batch. The real Demo uses conditional child removal, covered separately.
            await fixture.Renderer.RemoveVerificationModelerAsync();
            Assert.True(ModelerDisposed(fixture.Renderer.Modeler));
            Assert.Null(fixture.Renderer.Modeler.CaptureDecorationAttachment());
            if (delayRender) Assert.False(operation.IsCompleted);
        }
        finally { fixture.Execution.ReleaseRender(); }
        await operation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, unmountCount);
        Assert.Same(await control.PendingDecorationOperation!, control.DecorationResult);
        Assert.True(control.DecorationResult!.Accepted);
        Assert.Equal(delayRender ? Canvas2DDecorationUpdateStatus.Unavailable : Canvas2DDecorationUpdateStatus.Presented,
            control.DecorationResult.Status);
        Assert.Equal(!delayRender, control.DecorationResult.Presentation?.FramePresented == true);
        Assert.Contains("separate observations", control.Feedback, StringComparison.Ordinal);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Fact]
    public async Task DemoAnnotationControlAdvancesPastOtherPublicDecorationSubmissions()
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        await fixture.Renderer.ExerciseAnnotationRevisionAfterExternalInputAsync();
        Assert.Empty(fixture.DocumentChanges);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

#pragma warning disable BL0006 // Dispatch actual compiled Demo controls through the existing canonical renderer fixture.
    private sealed partial class ApplicationComponentRenderer
    {
        internal Task<(DemoVerificationControls Control, int ComponentId)> RenderVerificationControlsAsync(
            bool allowReplacement, EventCallback unmountRequested = default) => Dispatcher.InvokeAsync(async () =>
        {
            var control = (DemoVerificationControls)InstantiateComponent(typeof(DemoVerificationControls));
            var componentId = AssignRootComponentId(control);
            await RenderRootComponentAsync(componentId, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DemoVerificationControls.Modeler)] = Modeler,
                [nameof(DemoVerificationControls.Mounted)] = true,
                [nameof(DemoVerificationControls.MountGeneration)] = 1L,
                [nameof(DemoVerificationControls.MountId)] = "verification-test-mount",
                [nameof(DemoVerificationControls.AllowReplacement)] = allowReplacement,
                [nameof(DemoVerificationControls.UnmountRequested)] = unmountRequested,
            }));
            var frames = GetCurrentRenderTreeFrames(componentId);
            Assert.DoesNotContain(frames.Array.Take(frames.Count), frame =>
                frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName.StartsWith('@'));
            Assert.DoesNotContain(frames.Array.Take(frames.Count), frame =>
                frame.FrameType == RenderTreeFrameType.Element && frame.ElementName == "button");
            Assert.Null(control.DocumentResult);
            Assert.Null(control.DecorationResult);
            Assert.Null(control.SnapshotBefore);
            return (control, componentId);
        });

        internal Task EnableVerificationControlsAsync(int componentId) => Dispatcher.InvokeAsync(async () =>
        {
            var attributes = AnnotationAttributes(componentId, "input", "data-verification-action", "enable");
            var handler = Assert.Single(attributes, attribute => attribute.AttributeName == "onchange").AttributeEventHandlerId;
            Assert.NotEqual(0UL, handler);
            await DispatchEventAsync(handler, null, new ChangeEventArgs { Value = true });
        });

        internal Task ClickVerificationAsync(int componentId, string action) => Dispatcher.InvokeAsync(async () =>
        {
            var attributes = AnnotationAttributes(componentId, "button", "data-verification-action", action);
            Assert.DoesNotContain(attributes, attribute => attribute.AttributeName == "disabled" && Equals(attribute.AttributeValue, true));
            var handler = Assert.Single(attributes, attribute => attribute.AttributeName == "onclick").AttributeEventHandlerId;
            Assert.NotEqual(0UL, handler);
            await DispatchEventAsync(handler, null, new MouseEventArgs());
        });

        internal Task UpdateFreshDemoAnnotationsAsync() => Dispatcher.InvokeAsync(async () =>
        {
            var annotations = (DemoAnnotations)InstantiateComponent(typeof(DemoAnnotations));
            var componentId = AssignRootComponentId(annotations);
            await RenderRootComponentAsync(componentId, ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(DemoAnnotations.Maintenance)] = true, [nameof(DemoAnnotations.Modeler)] = Modeler }));
            await ClickAnnotationAsync(componentId, "update");
            AssertAnnotationStatus(componentId, "Presented", 1L);
        });

        internal Task ExerciseAnnotationRevisionAfterExternalInputAsync() => Dispatcher.InvokeAsync(async () =>
        {
            var annotations = (DemoAnnotations)InstantiateComponent(typeof(DemoAnnotations));
            var componentId = AssignRootComponentId(annotations);
            await RenderRootComponentAsync(componentId, ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(DemoAnnotations.Maintenance)] = true, [nameof(DemoAnnotations.Modeler)] = Modeler }));
            var attachment = Modeler.CaptureDecorationAttachment()!;
            AssertDecorationPresented(await Modeler.ReplaceDecorationsAsync(attachment, 7,
                DemoAnnotationProvider.CreateInput("Another public caller")), 7, true);
            await ClickAnnotationAsync(componentId, "update");
            AssertAnnotationStatus(componentId, "Presented", 8L);
            Assert.Equal(8, Modeler.CaptureEditorContext()!.Presentation.DecorationInputRevision);
        });

        internal Task RemoveVerificationModelerAsync() => Dispatcher.InvokeAsync(() => RemoveRootComponent(_modelerComponentId));
    }
#pragma warning restore BL0006
}
