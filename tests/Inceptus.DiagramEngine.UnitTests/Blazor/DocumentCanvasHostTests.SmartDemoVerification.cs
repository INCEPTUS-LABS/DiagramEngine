using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("", EditingSessionAccessMode.Editable)]
    [InlineData("simulation/", EditingSessionAccessMode.ReadOnly)]
    public async Task SmartDemoVerificationHostsWireRegistrationAndGuardRetiredFailureCallbacks(string path, EditingSessionAccessMode mode)
    {
        await using var fixture = await DemoPageFixture.CreateAsync(path);
        await fixture.EnableMaintenanceAsync();
        var modeler = Assert.Single(fixture.Activator.Modelers);
        object host = mode == EditingSessionAccessMode.ReadOnly ? Assert.Single(fixture.Activator.ReadOnlyHosts) :
            Assert.Single(fixture.Activator.EditableHosts);
        var example = Assert.IsType<DemoApplicationActionExample>(host.GetType().GetProperty("ActionExample", PropertiesUxFlags)!.GetValue(host));
        example.SetVerificationEnabled(true);
        example.UseTypedHandler = true;
        foreach (var registration in Enum.GetValues<DemoApplicationRegistration>())
        {
            example.SetRegistration(registration);
            // A public display-mode notification rerenders the actual parent without changing its modeler.
            await fixture.Renderer.Dispatcher.InvokeAsync(modeler.ToggleDisplayModeAsync);
            Assert.Same(modeler, Assert.Single(fixture.Activator.Modelers));
            Assert.Same(example.RegisteredProvider, modeler.ApplicationActionProvider);
            Assert.Same(example.RegisteredTypedHandler, modeler.ApplicationActionHandler);
            Assert.Equal(example.RegisterLegacyHandler, modeler.ApplicationActionInvoked.HasDelegate);
            Assert.Equal(example.Revision, modeler.ApplicationActionsRevision);
            Assert.Equal(mode, modeler.AccessMode);
        }
        example.SetRegistration(DemoApplicationRegistration.Normal);
        await fixture.Renderer.Dispatcher.InvokeAsync(modeler.ToggleDisplayModeAsync);
        var callback = modeler.OperationFailed;
        Assert.True(callback.HasDelegate);
        var failure = new BpmnModelerOperationFailedEventArgs(BpmnModelerOperation.ApplicationAction, BpmnModelerOperationStatus.Failed, []);
        await fixture.Renderer.Dispatcher.InvokeAsync(() => callback.InvokeAsync(failure));
        Assert.Equal(1, example.OperationFailureCount);
        var control = Assert.Single(fixture.Activator.VerificationControls);
        await fixture.Renderer.Dispatcher.InvokeAsync(() => control.UnmountRequested.InvokeAsync());
        var retiredCount = control.RetiredCallbackCount;
        await fixture.Renderer.Dispatcher.InvokeAsync(() => callback.InvokeAsync(failure));
        Assert.Equal(1, example.OperationFailureCount);
        Assert.Equal(retiredCount + 1, control.RetiredCallbackCount);
        await fixture.Renderer.Dispatcher.InvokeAsync(() => control.MountRequested.InvokeAsync());
        Assert.Equal(mode, fixture.Activator.Modelers.Last().AccessMode);
        await fixture.Renderer.Dispatcher.InvokeAsync(() => callback.InvokeAsync(failure));
        Assert.Equal(1, example.OperationFailureCount);
        Assert.Equal(retiredCount + 2, control.RetiredCallbackCount);
        Assert.Empty(fixture.Navigation.Navigations);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartDemoVerificationBindingsRetireInvalidRegistrationsAndRecoverWithoutNativeChanges(EditingSessionAccessMode mode)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var modeler = fixture.Renderer.Modeler;
        var example = new DemoApplicationActionExample(() => modeler) { ToolbarEnabled = true, UseTypedHandler = true };
        var component = await fixture.Renderer.RenderSmartVerificationAsync(example);
        Assert.False(example.VerificationEnabled);
        Assert.Equal(0, example.Revision);
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "enabled", true);
        Assert.True(example.VerificationEnabled);
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        await SmartToolbarAsync(fixture.Host);
        var source = modeler.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        var before = Session(fixture.Host).CaptureState();

        foreach (var registration in new[] { DemoApplicationRegistration.EmptyActions, DemoApplicationRegistration.NoExtension,
                     DemoApplicationRegistration.ProviderOnly, DemoApplicationRegistration.ConflictingHandlers })
        {
            await fixture.Renderer.ChangeSmartVerificationAsync(component, "registration", registration.ToString());
            if (registration == DemoApplicationRegistration.ConflictingHandlers)
            {
                await WaitForApplicationConditionAsync(() => example.OperationFeedback.Contains("BPMN_APPLICATION_HANDLER_CONFLICT", StringComparison.Ordinal));
                Assert.NotNull(example.RegisteredTypedHandler);
                Assert.True(example.RegisterLegacyHandler);
            }
            else if (registration == DemoApplicationRegistration.NoExtension)
            {
                Assert.Null(example.RegisteredProvider);
                Assert.Null(example.RegisteredTypedHandler);
                Assert.False(example.RegisterLegacyHandler);
            }
            else if (registration == DemoApplicationRegistration.ProviderOnly)
            {
                Assert.NotNull(example.RegisteredProvider);
                Assert.Null(example.RegisteredTypedHandler);
                Assert.False(example.RegisterLegacyHandler);
            }
            await WaitForApplicationConditionAsync(() => fixture.Host.ApplicationToolbar is null &&
                fixture.Host.CaptureState().ContextMenu?.IsApplicationDiscoveryPending != true);
            await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
            await WaitForApplicationConditionAsync(() => fixture.Host.CaptureState().ContextMenu?.IsApplicationDiscoveryPending != true);
            var surface = await fixture.Renderer.CaptureApplicationSurfaceAsync(fixture.Host);
            Assert.Empty(surface.Actions);
            Assert.DoesNotContain("application-action-group", surface.Classes);
            Assert.DoesNotContain(surface.Classes, value => value.Contains("application-action-heading", StringComparison.Ordinal));
            Assert.Equal(0, example.InvocationCount);
            if (mode == EditingSessionAccessMode.ReadOnly) Assert.DoesNotContain("menu", surface.Roles);
            fixture.Host.CloseContextMenu();
            await fixture.Renderer.ChangeSmartVerificationAsync(component, "registration", nameof(DemoApplicationRegistration.Normal));
            await SmartToolbarAsync(fixture.Host);
        }

        foreach (var action in new[] { "negative", "decreasing" })
        {
            var acceptedRevision = example.Revision;
            var previousFailures = example.OperationFailureCount;
            await fixture.Renderer.ClickSmartVerificationAsync(component, action);
            Assert.Equal(action == "negative" ? -1 : acceptedRevision - 1, example.Revision);
            await WaitForApplicationConditionAsync(() => example.OperationFailureCount > previousFailures);
            Assert.Contains("BPMN_APPLICATION_REVISION_INVALID", example.OperationFeedback, StringComparison.Ordinal);
            Assert.Null(fixture.Host.ApplicationToolbar);
            Assert.Equal(0, example.InvocationCount);
            if (action == "negative")
            {
                await example.ReactivateAsync();
                Assert.Contains("nonnegative", example.Feedback, StringComparison.Ordinal);
            }
            await fixture.Renderer.ClickSmartVerificationAsync(component, "recover");
            Assert.True(example.Revision > acceptedRevision);
            await SmartToolbarAsync(fixture.Host);
        }

        await fixture.Renderer.ChangeSmartVerificationAsync(component, "enabled", false);
        Assert.False(example.VerificationEnabled);
        Assert.Equal(DemoApplicationRegistration.Normal, example.Registration);
        Assert.Equal(DemoApplicationOutcome.OpenInspector, example.Outcome);
        Assert.Same(modeler, fixture.Renderer.Modeler);
        Assert.Equal(mode, modeler.AccessMode);
        Assert.Same(source, modeler.GetDocument().Snapshot);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(source).AsSpan()));
        var after = Session(fixture.Host).CaptureState();
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Empty(fixture.DocumentChanges);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false)]
    [InlineData(EditingSessionAccessMode.Editable, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true)]
    public async Task SmartDemoVerificationTypedOutcomesUseActualDispatcherAndKeepLegacyCompatibility(EditingSessionAccessMode mode, bool popup)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var modeler = fixture.Renderer.Modeler;
        var example = new DemoApplicationActionExample(() => modeler) { ToolbarEnabled = true, UseTypedHandler = true };
        var component = await fixture.Renderer.RenderSmartVerificationAsync(example);
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "enabled", true);
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        var source = modeler.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        var history = Session(fixture.Host).CaptureState().HistoryStatus;

        foreach (var outcome in Enum.GetValues<DemoApplicationOutcome>())
        {
            example.CloseDialog();
            await fixture.Renderer.ChangeSmartVerificationAsync(component, "outcome", outcome.ToString());
            var invocations = example.InvocationCount;
            var failures = example.OperationFailureCount;
            var surface = await CaptureSmartVerificationSurfaceAsync(fixture, popup);
            var requestVersion = popup ? fixture.Host.CaptureState().ContextMenu!.ApplicationContext!.RequestVersion :
                fixture.Host.ApplicationToolbar!.Context.RequestVersion;
            var action = Array.IndexOf(surface.Actions, "example:inspect");
            Assert.InRange(action, 0, surface.Handlers.Length - 1);
            await fixture.Renderer.DispatchApplicationSurfaceAsync(surface.ComponentId, surface.Handlers[action]);
            await WaitForApplicationConditionAsync(() => example.InvocationCount == invocations + 1);
            var reportsFailure = outcome is not (DemoApplicationOutcome.OpenInspector or DemoApplicationOutcome.Cancelled);
            if (reportsFailure)
            {
                await WaitForApplicationConditionAsync(() => example.OperationFailureCount == failures + 1);
                Assert.Contains(outcome == DemoApplicationOutcome.Unavailable ? "BPMN_APPLICATION_ACTION_UNAVAILABLE" :
                    "BPMN_APPLICATION_INVOCATION_FAILED", example.OperationFeedback, StringComparison.Ordinal);
                Assert.Contains(outcome == DemoApplicationOutcome.Unavailable ? "Unavailable" : "Failed", example.OperationFeedback, StringComparison.Ordinal);
            }
            else
            {
                await SmartToolbarAsync(fixture.Host);
                Assert.Equal(failures, example.OperationFailureCount);
            }
            Assert.Equal(outcome == DemoApplicationOutcome.OpenInspector, example.DialogOpen);
            Assert.NotEqual("No application handling attempt recorded.", example.ConsumerOutcome);
            // The compiled control was consumed; its old request cannot enter the handler a second time.
            Assert.False((await fixture.Host.ExecuteApplicationActionAsync(requestVersion, "example:inspect")).Succeeded);
            Assert.Equal(invocations + 1, example.InvocationCount);
            Assert.Same(source, modeler.GetDocument().Snapshot);
            Assert.Equal(history, Session(fixture.Host).CaptureState().HistoryStatus);
            Assert.Equal(mode, modeler.AccessMode);
        }

        example.CloseDialog();
        example.UseTypedHandler = false;
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "outcome", nameof(DemoApplicationOutcome.Failed));
        var legacyFailures = example.OperationFailureCount;
        var legacySurface = await CaptureSmartVerificationSurfaceAsync(fixture, popup);
        var legacyAction = Array.IndexOf(legacySurface.Actions, "example:inspect");
        await fixture.Renderer.DispatchApplicationSurfaceAsync(legacySurface.ComponentId, legacySurface.Handlers[legacyAction]);
        await WaitForApplicationConditionAsync(() => example.DialogOpen);
        Assert.Contains("Legacy callback completed", example.ConsumerOutcome, StringComparison.Ordinal);
        Assert.Equal(legacyFailures, example.OperationFailureCount);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(history, Session(fixture.Host).CaptureState().HistoryStatus);
        Assert.Empty(fixture.DocumentChanges);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, "selection")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "selection")]
    [InlineData(EditingSessionAccessMode.Editable, "policy")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "policy")]
    [InlineData(EditingSessionAccessMode.Editable, "replacement")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "replacement")]
    [InlineData(EditingSessionAccessMode.Editable, "scope")]
    [InlineData(EditingSessionAccessMode.ReadOnly, "scope")]
    public async Task SmartDemoVerificationReplaysImmutableCapturedContextAndPreCancelledRequest(EditingSessionAccessMode mode, string change)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var modeler = fixture.Renderer.Modeler;
        var example = new DemoApplicationActionExample(() => modeler) { ToolbarEnabled = true, UseTypedHandler = true };
        var component = await fixture.Renderer.RenderSmartVerificationAsync(example);
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "enabled", true);
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        await fixture.Renderer.ClickSmartVerificationAsync(component, "capture");
        var saved = Assert.IsType<BpmnPresentationContextStamp>(example.CapturedRequestStamp);
        var savedRevision = example.CapturedRequestRevision;
        var source = modeler.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        await fixture.Renderer.ClickSmartVerificationAsync(component, "replay");
        Assert.Equal(BpmnModelerOperationStatus.Succeeded, example.ReplayResult!.Status);
        await fixture.Renderer.ClickSmartVerificationAsync(component, "cancelled");
        Assert.Equal(BpmnModelerOperationStatus.Cancelled, example.ReplayResult!.Status);
        Assert.Empty(example.ReplayResult.Diagnostics);

        switch (change)
        {
            case "selection":
                Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.ApprovedTaskVisualId)).Succeeded);
                break;
            case "policy":
                await fixture.Renderer.ChangeSmartVerificationAsync(component, "outcome", nameof(DemoApplicationOutcome.Cancelled));
                break;
            case "replacement":
                Assert.True((await modeler.SetDocumentAsync(source, new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true })).Succeeded);
                break;
            case "scope":
                Assert.True((await modeler.NavigateToScopeAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
                break;
        }
        var beforeReplay = Session(fixture.Host).CaptureState();
        var presentation = modeler.CapturePresentation();
        await fixture.Renderer.ClickSmartVerificationAsync(component, "replay");
        Assert.Equal(BpmnModelerOperationStatus.Rejected, example.ReplayResult!.Status);
        Assert.Equal("BPMN_APPLICATION_CONTEXT_STALE", Assert.Single(example.ReplayResult.Diagnostics).Code);
        Assert.Same(saved, example.CapturedRequestStamp);
        Assert.Equal(savedRevision, example.CapturedRequestRevision);
        Assert.Equal(presentation.Stamp, modeler.CapturePresentation().Stamp);
        Assert.Equal(0, example.InvocationCount);
        Assert.Equal(beforeReplay.HistoryStatus, Session(fixture.Host).CaptureState().HistoryStatus);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(mode, modeler.AccessMode);
        Assert.Same(modeler, fixture.Renderer.Modeler);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    private static async Task<ApplicationSurfaceCapture> CaptureSmartVerificationSurfaceAsync(ReadOnlyDemoFixture fixture, bool popup)
    {
        fixture.Host.CloseContextMenu();
        if (popup)
        {
            Assert.True((await fixture.Host.OpenApplicationContextMenuFromSelectionAsync()).Succeeded);
            await ApplicationMenuAsync(fixture.Host);
        }
        else await SmartToolbarAsync(fixture.Host);
        return await fixture.Renderer.CaptureApplicationSurfaceAsync(fixture.Host);
    }

#pragma warning disable BL0006 // Exercise real compiled Demo bindings and public modeler registration in the canonical renderer.
    private sealed partial class ApplicationComponentRenderer
    {
        internal Task<int> RenderSmartVerificationAsync(DemoApplicationActionExample example) => Dispatcher.InvokeAsync(async () =>
        {
            var component = (DemoApplicationVerificationControls)InstantiateComponent(typeof(DemoApplicationVerificationControls));
            var id = AssignRootComponentId(component);
            await RenderRootComponentAsync(id, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DemoApplicationVerificationControls.Example)] = example,
                [nameof(DemoApplicationVerificationControls.Changed)] = EventCallback.Factory.Create(example, () => ApplySmartVerificationAsync(example)),
            }));
            var frames = GetCurrentRenderTreeFrames(id);
            Assert.DoesNotContain(frames.Array.Take(frames.Count), frame =>
                frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName.StartsWith('@'));
            Assert.DoesNotContain(frames.Array.Take(frames.Count), frame =>
                frame.FrameType == RenderTreeFrameType.Element && frame.ElementName is "button" or "select");
            await ApplySmartVerificationAsync(example);
            return id;
        });

        private Task ApplySmartVerificationAsync(DemoApplicationActionExample example) =>
            RenderRootComponentAsync(_modelerComponentId, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InceptusBpmnModeler.ApplicationActionProvider)] = example.RegisteredProvider,
                [nameof(InceptusBpmnModeler.ApplicationContextualActionsEnabled)] = example.ToolbarEnabled,
                [nameof(InceptusBpmnModeler.ApplicationActionsRevision)] = example.Revision,
                [nameof(InceptusBpmnModeler.ApplicationActionHandler)] = example.RegisteredTypedHandler,
                [nameof(InceptusBpmnModeler.ApplicationActionInvoked)] = example.RegisterLegacyHandler
                    ? EventCallback.Factory.Create(example, example.LegacyHandler) : default(EventCallback<BpmnApplicationActionInvocationEventArgs>),
                [nameof(InceptusBpmnModeler.OperationFailed)] = EventCallback.Factory.Create<BpmnModelerOperationFailedEventArgs>(example, example.RecordOperationFailure),
            }));

        internal Task ChangeSmartVerificationAsync(int componentId, string control, object value) => Dispatcher.InvokeAsync(async () =>
        {
            var attributes = AnnotationAttributes(componentId, control is "enabled" or "catalog" ? "input" : "select", "data-application-verification", control);
            var handler = Assert.Single(attributes, attribute => attribute.AttributeName == "onchange").AttributeEventHandlerId;
            Assert.NotEqual(0UL, handler);
            await DispatchEventAsync(handler, null, new ChangeEventArgs { Value = value });
            if (control == "enabled" && Equals(value, true))
            {
                var frames = GetCurrentRenderTreeFrames(componentId);
                Assert.Contains(frames.Array.Take(frames.Count), frame => frame.FrameType == RenderTreeFrameType.Markup &&
                    frame.MarkupContent.Contains("href=\"simulation/?sample=message\"", StringComparison.Ordinal));
            }
        });

        internal Task ClickSmartVerificationAsync(int componentId, string action) => Dispatcher.InvokeAsync(async () =>
        {
            var attributes = AnnotationAttributes(componentId, "button", "data-application-verification", action);
            Assert.DoesNotContain(attributes, attribute => attribute.AttributeName == "disabled" && Equals(attribute.AttributeValue, true));
            var handler = Assert.Single(attributes, attribute => attribute.AttributeName == "onclick").AttributeEventHandlerId;
            Assert.NotEqual(0UL, handler);
            await DispatchEventAsync(handler, null, new MouseEventArgs());
        });
    }
#pragma warning restore BL0006
}
