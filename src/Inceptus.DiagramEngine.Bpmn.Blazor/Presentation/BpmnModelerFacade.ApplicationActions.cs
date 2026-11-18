namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class BpmnModelerFacade
{
    private BpmnApplicationActionProvider? _applicationActionProvider;
    private bool _applicationActionCallbackAvailable;
    private bool _applicationTypedHandlerAvailable;
    private bool _applicationContextualActionsEnabled;
    private long _applicationActionsRevision;
    private object? _applicationActionAuthority;

    internal void SetApplicationActions(BpmnApplicationActionProvider? provider, bool callbackAvailable) =>
        SetApplicationActions(provider, callbackAvailable, false, false, 0);

    internal void SetApplicationActions(BpmnApplicationActionProvider? provider, bool callbackAvailable,
        bool typedHandlerAvailable, bool toolbarEnabled, long revision, object? authorityIdentity = null)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _applicationActionProvider = provider;
            _applicationActionCallbackAvailable = callbackAvailable;
            _applicationTypedHandlerAvailable = typedHandlerAvailable;
            _applicationContextualActionsEnabled = toolbarEnabled;
            _applicationActionsRevision = revision;
            _applicationActionAuthority = authorityIdentity;
            _host?.SetApplicationActions(provider, callbackAvailable, typedHandlerAvailable, toolbarEnabled, revision, authorityIdentity);
        }
    }

    internal ValueTask<BpmnPresentationOperationResult> RequestApplicationContextualActionsAsync(
        BpmnPresentationContextStamp expectedContext, long expectedApplicationActionsRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedContext);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedApplicationActionsRevision);
        return CaptureHost()?.RequestApplicationContextualActionsAsync(expectedContext, expectedApplicationActionsRevision, cancellationToken)
            ?? ValueTask.FromResult(new BpmnPresentationOperationResult(cancellationToken.IsCancellationRequested
                    ? BpmnModelerOperationStatus.Cancelled : BpmnModelerOperationStatus.Unavailable,
                BpmnPresentationSnapshot.Unavailable, cancellationToken.IsCancellationRequested ? [] :
                [new("BPMN_PRESENTATION_UNAVAILABLE", Contracts.Diagnostics.DiagnosticSeverity.Error,
                    "The modeler presentation is unavailable.")]));
    }
}
