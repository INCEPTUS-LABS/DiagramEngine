using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Components;

public partial class DocumentCanvas
{
    private Task<IJSObjectReference>? _applicationMenuModule;
    private long? _focusedApplicationRequest;
    private DocumentCanvasApplicationToolbarState? _applicationToolbar;
    private bool _applicationToolbarWasVisible;
    private bool _applicationOverflowOpen;
    private bool _applicationOverflowFocusPending;
    private bool _restoreApplicationFocus;
    private bool _restoreToolbarFocus;
    private long? _boundApplicationToolbarRequest;
    private static readonly BpmnApplicationActionIcon DefaultApplicationToolbarIcon = new(
        "M5 5 L19 5 L19 8 L5 8 Z M5 10 L19 10 L19 13 L5 13 Z M5 15 L19 15 L19 18 L5 18 Z", new(0, 0, 24, 24));
    private EventCallback ApplicationToolbarDismissCallback => EventCallback.Factory.Create(this, DismissApplicationToolbarAsync);
    private string ApplicationActionsEnabled => _host?.HasApplicationActions == true ? "true" : "false";
    private string ApplicationMenuId => DomId(_state?.ContextMenu?.Kind == DocumentCanvasContextMenuKind.Background
        ? "model-context-menu" : "object-context-menu");

    private async Task HandleCanvasContextKeyAsync(KeyboardEventArgs args)
    {
        if (_host is { HasApplicationActions: true } host &&
            (args.Key == "ContextMenu" || args.Key == "F10" && args.ShiftKey) &&
            !args.CtrlKey && !args.AltKey && !args.MetaKey)
        {
            _ = await host.OpenApplicationContextMenuFromSelectionAsync(_componentLifetime.Token);
            RefreshHostState(host);
            return;
        }
        // Other keys retain the existing root/ancestor bubbling path, including Escape.
    }

    private async Task HandleApplicationMenuKeyAsync(KeyboardEventArgs args)
    {
        if (_state?.ContextMenu?.ApplicationContext is null) return;
        if (args.Key is "Escape" or "Tab")
        {
            _restoreApplicationFocus = args.Key == "Escape";
            CloseContextMenu();
            return;
        }
        await UseApplicationMenuModuleAsync(module => module.InvokeVoidAsync(
            "handleContextMenuKey", _componentLifetime.Token, ApplicationMenuId, args.Key));
    }

    private async Task ExecuteApplicationMenuActionAsync(long requestVersion, string actionId)
    {
        if (_disposed || _host is not { } host) return;
        _ = await host.ExecuteApplicationActionAsync(requestVersion, actionId, _componentLifetime.Token);
        RefreshHostState(host);
    }

    private async Task UpdateApplicationMenuFocusAsync()
    {
        if (_disposed) return;
        var request = _state?.ContextMenu?.ApplicationContext?.RequestVersion;
        if (request is not null)
        {
            var first = _focusedApplicationRequest != request;
            var menuRendered = false;
            await UseApplicationMenuModuleAsync(async module => menuRendered = await module.InvokeAsync<bool>(
                "focusContextMenu", _componentLifetime.Token, ApplicationMenuId, first));
            // An application-only pending request has no empty shell to focus. Keep its first
            // focus pending until the actual menu exists, without reviving a retired request.
            if (menuRendered && _state?.ContextMenu?.ApplicationContext?.RequestVersion == request)
                _focusedApplicationRequest = request;
        }
        else if (_focusedApplicationRequest is not null)
        {
            _focusedApplicationRequest = null;
            // Retirement/handler completion must never steal an application dialog's focus.
            // Only explicit keyboard dismissal requests a return to the native surface.
            if (_restoreApplicationFocus)
            {
                _restoreApplicationFocus = false;
                await UseApplicationMenuModuleAsync(module => module.InvokeVoidAsync(
                    "restoreCanvasFocus", _componentLifetime.Token, ActiveCanvasElementId));
            }
        }
    }

    private void CaptureApplicationToolbar(DocumentCanvasHost host)
    {
        _applicationToolbarWasVisible = _applicationToolbar is { Actions.IsEmpty: false };
        var next = host.ApplicationToolbar;
        if (_applicationToolbar?.Context.RequestVersion != next?.Context.RequestVersion)
        {
            _applicationOverflowOpen = false;
            _applicationOverflowFocusPending = false;
            _boundApplicationToolbarRequest = null;
        }
        _applicationToolbar = next;
    }

    private static string ApplicationToolbarStyle(ApplicationToolbarPlacement placement) =>
        FormattableString.Invariant($"left:{placement.Position.X}px;top:{placement.Position.Y}px;width:{placement.Width}px;");

    private static RenderFragment RenderApplicationActionIcon(BpmnApplicationActionIcon? icon) => builder =>
    {
        if (icon is null) return;
        builder.OpenElement(0, "svg");
        builder.AddAttribute(1, "class", "application-action-icon");
        builder.AddAttribute(2, "width", "20");
        builder.AddAttribute(3, "height", "20");
        builder.AddAttribute(4, "viewBox", FormattableString.Invariant($"{icon.ViewBox.X} {icon.ViewBox.Y} {icon.ViewBox.Width} {icon.ViewBox.Height}"));
        builder.AddAttribute(5, "preserveAspectRatio", "xMidYMid meet");
        builder.AddAttribute(6, "aria-hidden", "true");
        builder.AddAttribute(7, "focusable", "false");
        builder.AddAttribute(8, "style", "flex:0 0 20px;pointer-events:none");
        builder.OpenElement(9, "path");
        builder.AddAttribute(10, "d", icon.PathData);
        builder.AddAttribute(11, "fill", "currentColor");
        builder.AddAttribute(12, "fill-rule", "nonzero");
        builder.CloseElement();
        builder.CloseElement();
    };

    private void ToggleApplicationOverflow()
    {
        _applicationOverflowOpen = !_applicationOverflowOpen;
        _applicationOverflowFocusPending = _applicationOverflowOpen;
    }

    private async Task HandleApplicationToolbarKeyAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
            await DismissApplicationToolbarAsync();
        else if (args.Key is "ArrowLeft" or "ArrowRight" or "Home" or "End")
            await UseApplicationMenuModuleAsync(module => module.InvokeVoidAsync(
                "handleToolbarKey", _componentLifetime.Token, DomId("application-toolbar"), args.Key));
    }

    private Task DismissApplicationToolbarAsync()
    {
        if (_host is not { } host) return Task.CompletedTask;
        host.DismissApplicationContextualActions();
        RefreshHostState(host);
        _restoreToolbarFocus = true;
        return Task.CompletedTask;
    }

    private async Task HandleApplicationOverflowKeyAsync(KeyboardEventArgs args)
    {
        if (args.Key is "Escape" or "Tab")
        {
            _applicationOverflowOpen = false;
            await UseApplicationMenuModuleAsync(module => module.InvokeVoidAsync(
                "focusToolbarMore", _componentLifetime.Token, DomId("application-toolbar")));
            return;
        }
        await UseApplicationMenuModuleAsync(module => module.InvokeVoidAsync(
            "handleContextMenuKey", _componentLifetime.Token, DomId("application-toolbar-overflow"), args.Key));
    }

    private async Task UpdateApplicationToolbarPresentationAsync()
    {
        if (_restoreToolbarFocus && !_disposed)
        {
            _restoreToolbarFocus = false;
            await UseApplicationMenuModuleAsync(module => module.InvokeVoidAsync(
                "restoreCanvasFocus", _componentLifetime.Token, ActiveCanvasElementId));
        }
        if (_disposed || _applicationToolbar is not { IsDiscoveryPending: false, Actions.IsEmpty: false } toolbar) return;
        if (_boundApplicationToolbarRequest != toolbar.Context.RequestVersion)
        {
            await UseApplicationMenuModuleAsync(module => module.InvokeVoidAsync(
                "prepareApplicationToolbar", _componentLifetime.Token, DomId("application-toolbar")));
            _boundApplicationToolbarRequest = toolbar.Context.RequestVersion;
        }
        if (_applicationOverflowOpen)
        {
            var takeFocus = _applicationOverflowFocusPending;
            _applicationOverflowFocusPending = false;
            await UseApplicationMenuModuleAsync(module => module.InvokeVoidAsync(
                "focusToolbarOverflow", _componentLifetime.Token, DomId("application-toolbar-overflow"), ContainerElementId, takeFocus));
        }
    }

    private async Task UseApplicationMenuModuleAsync(Func<IJSObjectReference, ValueTask> action)
    {
        if (_disposed) return;
        try
        {
            _applicationMenuModule ??= JavaScript.InvokeAsync<IJSObjectReference>("import",
                _componentLifetime.Token, "./_content/Inceptus.DiagramEngine.Bpmn.Blazor/inceptus.presentation.js").AsTask();
            var module = await _applicationMenuModule;
            if (!_disposed) await action(module);
        }
        catch (OperationCanceledException) when (_componentLifetime.IsCancellationRequested) { }
        catch (JSDisconnectedException) { }
    }

    private async ValueTask DisposeApplicationMenuModuleAsync()
    {
        if (_applicationMenuModule is not { } pending) return;
        _applicationMenuModule = null;
        try { await (await pending).DisposeAsync(); }
        catch (OperationCanceledException) when (_componentLifetime.IsCancellationRequested)
        { _lifecycleObservation?.Evidence.Record("MenuCleanupCancelled", "Component lifetime cancelled."); }
        catch (JSDisconnectedException)
        { _lifecycleObservation?.Evidence.Record("MenuCleanupDisconnected", "Browser connection unavailable."); }
    }
}
