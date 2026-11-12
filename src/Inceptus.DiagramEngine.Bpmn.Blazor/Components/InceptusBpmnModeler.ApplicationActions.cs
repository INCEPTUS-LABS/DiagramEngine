using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Components;

public partial class InceptusBpmnModeler
{
    /// <summary>Optional per-component discovery. Omitting it preserves the native editor menu.</summary>
    [Parameter]
    public BpmnApplicationActionProvider? ApplicationActionProvider { get; set; }

    /// <summary>
    /// Invoked outside engine locks with a revalidated semantic context. Invocation is ordered;
    /// asynchronous handlers may complete out of order and may await subsequent modeler operations.
    /// </summary>
    [Parameter]
    public EventCallback<BpmnApplicationActionInvocationEventArgs> ApplicationActionInvoked { get; set; }

    private BpmnApplicationActionProvider? _configuredActionProvider;
    private EventCallback<BpmnApplicationActionInvocationEventArgs> _configuredActionCallback;

    private void ConfigureApplicationActions()
    {
        if (_disposed || ApplicationActionProvider == _configuredActionProvider &&
            ApplicationActionInvoked.Equals(_configuredActionCallback)) return;
        _configuredActionProvider = ApplicationActionProvider;
        _configuredActionCallback = ApplicationActionInvoked;
        var provider = ApplicationActionProvider;
        Facade.SetApplicationActions(provider is null ? null :
            (context, token) => DiscoverApplicationActionsAsync(provider, context, token),
            ApplicationActionInvoked.HasDelegate);
    }

    private async ValueTask<IEnumerable<BpmnApplicationAction>> DiscoverApplicationActionsAsync(
        BpmnApplicationActionProvider provider, BpmnApplicationActionContext context, CancellationToken token)
    {
        Task<IEnumerable<BpmnApplicationAction>> work = Task.FromResult<IEnumerable<BpmnApplicationAction>>([]);
        await InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (_disposed) throw new OperationCanceledException(token);
            work = provider(context, token).AsTask();
        });
        return await work.ConfigureAwait(false);
    }

    private void InvokeApplicationAction(BpmnApplicationActionDispatch dispatch)
    {
#pragma warning disable CA1031 // Application failures are bounded feedback, never native command rollback.
        try
        {
            if (dispatch.TryBeginInvocation(out var args))
                _ = ObserveApplicationActionAsync(ApplicationActionInvoked.InvokeAsync(args), dispatch);
        }
        catch (Exception exception) { dispatch.Fail(exception); }
#pragma warning restore CA1031
    }

    private static async Task ObserveApplicationActionAsync(Task callback, BpmnApplicationActionDispatch dispatch)
    {
#pragma warning disable CA1031 // Observe asynchronous application failures without blocking callback invocation order.
        try { await callback.ConfigureAwait(false); dispatch.Complete(); }
        catch (Exception exception) { dispatch.Fail(exception); }
#pragma warning restore CA1031
    }
}
