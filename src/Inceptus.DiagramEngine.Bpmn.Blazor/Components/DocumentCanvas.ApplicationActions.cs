using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Components;

public partial class DocumentCanvas
{
    private Task<IJSObjectReference>? _applicationMenuModule;
    private long? _focusedApplicationRequest;
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
            _focusedApplicationRequest = request;
            await UseApplicationMenuModuleAsync(module => module.InvokeVoidAsync(
                "focusContextMenu", _componentLifetime.Token, ApplicationMenuId, first));
        }
        else if (_focusedApplicationRequest is not null)
        {
            _focusedApplicationRequest = null;
            await UseApplicationMenuModuleAsync(module => module.InvokeVoidAsync(
                "restoreCanvasFocus", _componentLifetime.Token, ActiveCanvasElementId));
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
        catch (OperationCanceledException) when (_componentLifetime.IsCancellationRequested) { }
        catch (JSDisconnectedException) { }
    }
}
