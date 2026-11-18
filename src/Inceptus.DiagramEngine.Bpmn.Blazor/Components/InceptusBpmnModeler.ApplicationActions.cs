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

    /// <summary>Enables the application Smart toolbar only; popup actions remain independently available.</summary>
    [Parameter]
    public bool ApplicationContextualActionsEnabled { get; set; }

    /// <summary>Nonnegative, monotonic application policy revision for this component lifetime.</summary>
    [Parameter]
    public long ApplicationActionsRevision { get; set; }

    /// <summary>Optional typed handling authority. Do not register together with ApplicationActionInvoked.</summary>
    [Parameter]
    public BpmnApplicationActionHandler? ApplicationActionHandler { get; set; }

    /// <summary>Requests rediscovery for the same selection after explicit dismissal, without changing selection or focus.</summary>
    public ValueTask<BpmnPresentationOperationResult> RequestApplicationContextualActionsAsync(
        BpmnPresentationContextStamp expectedContext, long expectedApplicationActionsRevision,
        CancellationToken cancellationToken = default) =>
        Facade.RequestApplicationContextualActionsAsync(expectedContext, expectedApplicationActionsRevision, cancellationToken);

    private BpmnApplicationActionProvider? _configuredActionProvider;
    private BpmnApplicationActionProvider? _dispatchedActionProvider;
    private EventCallback<BpmnApplicationActionInvocationEventArgs> _configuredActionCallback;
    private BpmnApplicationActionHandler? _configuredActionHandler;
    private bool _configuredContextualActionsEnabled;
    private long _configuredApplicationActionsRevision;
    private bool _applicationActionsConfigured;

    private void ConfigureApplicationActions()
    {
        if (_disposed || _applicationActionsConfigured && ApplicationActionProvider == _configuredActionProvider &&
            ApplicationActionInvoked.Equals(_configuredActionCallback) && ApplicationActionHandler == _configuredActionHandler &&
            ApplicationContextualActionsEnabled == _configuredContextualActionsEnabled &&
            ApplicationActionsRevision == _configuredApplicationActionsRevision) return;
        if (ApplicationActionProvider != _configuredActionProvider)
        {
            var provider = ApplicationActionProvider;
            _dispatchedActionProvider = provider is null ? null :
                (context, token) => DiscoverApplicationActionsAsync(provider, context, token);
        }
        _configuredActionProvider = ApplicationActionProvider;
        _configuredActionCallback = ApplicationActionInvoked;
        _configuredActionHandler = ApplicationActionHandler;
        _configuredContextualActionsEnabled = ApplicationContextualActionsEnabled;
        _configuredApplicationActionsRevision = ApplicationActionsRevision;
        _applicationActionsConfigured = true;
        Facade.SetApplicationActions(_dispatchedActionProvider, ApplicationActionInvoked.HasDelegate,
            ApplicationActionHandler is not null, ApplicationContextualActionsEnabled, ApplicationActionsRevision,
            (object?)ApplicationActionHandler ?? ApplicationActionInvoked);
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
            if (dispatch.TryBeginInvocation(out var args) && args is not null)
            {
                if (dispatch.IsTyped)
                    _ = InvokeTypedApplicationActionAsync(ApplicationActionHandler, args, dispatch);
                else
                {
                    dispatch.Observe(BpmnActionObservationStage.ConsumerCallAttempt);
                    _ = ObserveApplicationActionAsync(ApplicationActionInvoked.InvokeAsync(args), dispatch);
                }
            }
        }
        catch (Exception exception) { dispatch.Fail(exception); }
#pragma warning restore CA1031
    }

    private static async Task InvokeTypedApplicationActionAsync(BpmnApplicationActionHandler? handler,
        BpmnApplicationActionInvocationEventArgs args, BpmnApplicationActionDispatch dispatch)
    {
#pragma warning disable CA1031 // Typed application failures are bounded invocation outcomes.
        try
        {
            if (handler is null) throw new InvalidOperationException("The application handling authority is unavailable.");
            dispatch.Observe(BpmnActionObservationStage.ConsumerCallAttempt);
            dispatch.Complete(await handler(args).ConfigureAwait(false));
        }
        catch (Exception exception) { dispatch.FailTyped(exception, args.CancellationToken); }
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
