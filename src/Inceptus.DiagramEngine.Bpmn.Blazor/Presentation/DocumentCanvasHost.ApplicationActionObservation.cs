namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private BpmnApplicationActionObservation? _applicationActionObservation;

    // Idempotent. Only subsequent requests are observed; arming never restarts discovery.
    internal BpmnApplicationActionObservation? BeginApplicationActionObservation()
    {
        lock (_sync) return _disposed ? null : _applicationActionObservation ??= new();
    }
}
