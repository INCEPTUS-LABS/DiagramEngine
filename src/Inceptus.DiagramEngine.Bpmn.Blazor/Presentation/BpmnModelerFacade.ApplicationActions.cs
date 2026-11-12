namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class BpmnModelerFacade
{
    private BpmnApplicationActionProvider? _applicationActionProvider;
    private bool _applicationActionCallbackAvailable;

    internal void SetApplicationActions(BpmnApplicationActionProvider? provider, bool callbackAvailable)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _applicationActionProvider = provider;
            _applicationActionCallbackAvailable = callbackAvailable;
            _host?.SetApplicationActions(provider, callbackAvailable);
        }
    }
}
