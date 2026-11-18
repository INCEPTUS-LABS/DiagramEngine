using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Contracts.Documents;

namespace Inceptus.DiagramEngine.Blazor.Demo;

public partial class BpmnDemoHost
{
    private bool _verificationMounted = true;
    private long _verificationGeneration = 1;
    private int _verificationRetiredCallbacks;
    private DocumentSnapshot? _verificationSource;
    private string VerificationMountId => DomId($"verification-mount-{_verificationGeneration}");

    private void UnmountForVerification()
    {
        if (_disposed || Editor is not null || !_verificationMounted) return;
        _verificationSource = _modeler?.GetDocument().Snapshot ?? _verificationSource;
        _verificationMounted = false;
        _modeler = null;
        _host = null;
        _operations = null;
        _state = null;
        _displayMode = BpmnModelerDisplayMode.Normal;
        InvalidateToolbarPresentation();
    }

    private void MountForVerification()
    {
        if (_disposed || Editor is not null || _verificationMounted) return;
        _verificationGeneration++;
        _verificationMounted = true;
    }

    private bool AcceptVerificationCallback(long generation)
    {
        if (_disposed || !_verificationMounted || generation != _verificationGeneration)
        {
            _verificationRetiredCallbacks++;
            return false;
        }
        return true;
    }

    private void VerificationReady(long generation, BpmnModelerReadyEventArgs args)
    {
        if (!AcceptVerificationCallback(generation)) return;
        _verificationSource = args.Snapshot;
        OnReady(args);
    }

    private void VerificationDocumentChanged(long generation, BpmnModelerDocumentChangedEventArgs args)
    {
        if (!AcceptVerificationCallback(generation)) return;
        _verificationSource = args.Snapshot;
        OnDocumentChanged(args);
    }

    private void VerificationViewChanged(long generation)
    {
        if (AcceptVerificationCallback(generation)) OnViewChanged();
    }

    private void VerificationActionFailure(long generation, BpmnModelerOperationFailedEventArgs args)
    {
        if (AcceptVerificationCallback(generation)) _actionExample?.RecordOperationFailure(args);
    }
}
