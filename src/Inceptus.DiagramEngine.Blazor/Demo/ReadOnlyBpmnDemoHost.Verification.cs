using Inceptus.DiagramEngine.Bpmn.Blazor;

namespace Inceptus.DiagramEngine.Blazor.Demo;

public partial class ReadOnlyBpmnDemoHost
{
    private bool _verificationMounted = true;
    private long _verificationGeneration = 1;
    private int _verificationRetiredCallbacks;
    private Func<BpmnApplicationActionInvocationEventArgs, Task>? _verificationInspectionCallback;
    private string VerificationMountId => $"{_identity}-verification-mount-{_verificationGeneration}";
    private Func<BpmnApplicationActionInvocationEventArgs, Task> VerificationInspectionCallback
    {
        get
        {
            if (_verificationInspectionCallback is not null) return _verificationInspectionCallback;
            var generation = _verificationGeneration;
            return _verificationInspectionCallback = args => VerificationInspectAsync(generation, args);
        }
    }

    private void UnmountForVerification()
    {
        if (_disposed || !_verificationMounted) return;
        _source = Modeler?.GetDocument().Snapshot ?? _source;
        _verificationMounted = false;
        Modeler = null;
        Presentation = null;
        _inspectedTarget = null;
        _inspectionStamp = null;
        _displayMode = BpmnModelerDisplayMode.Normal;
    }

    private void MountForVerification()
    {
        if (_disposed || _verificationMounted) return;
        _verificationGeneration++;
        _verificationInspectionCallback = null;
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
        if (AcceptVerificationCallback(generation)) OnReady(args);
    }

    private void VerificationDocumentChanged(long generation, BpmnModelerDocumentChangedEventArgs args)
    {
        if (AcceptVerificationCallback(generation)) OnDocumentChanged(args);
    }

    private void VerificationSelectionChanged(long generation, BpmnSelectionChangedEventArgs args)
    {
        if (AcceptVerificationCallback(generation)) OnSelectionChanged(args);
    }

    private void VerificationViewChanged(long generation)
    {
        if (AcceptVerificationCallback(generation)) RefreshPresentation();
    }

    private void VerificationFailure(long generation, BpmnModelerOperationFailedEventArgs args)
    {
        if (!AcceptVerificationCallback(generation)) return;
        _actionExample?.RecordOperationFailure(args);
        OnFailure(args);
    }

    private Task VerificationInspectAsync(long generation, BpmnApplicationActionInvocationEventArgs args) =>
        AcceptVerificationCallback(generation) ? InspectAsync(args) : Task.CompletedTask;
}
