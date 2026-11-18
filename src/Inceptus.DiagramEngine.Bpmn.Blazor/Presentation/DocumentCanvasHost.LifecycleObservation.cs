namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private BpmnLifecycleObservation? _lifecycleObservation;

    internal BpmnLifecycleObservation? BeginLifecycleObservation()
    {
        lock (_sync)
        {
            if (CaptureDecorationAttachment() is not { } attachment || _session is null) return null;
            var receipt = new BpmnLifecycleObservation(attachment);
            _lifecycleObservation = receipt;
            _session.LifecycleObservation = receipt.Evidence;
            if (_pointerObserver is CanvasPresentationPointerObserver pointer) pointer.LifecycleObservation = receipt.Evidence;
            if (_observer is CanvasPresentationSurfaceObserver surface) surface.LifecycleObservation = receipt.Evidence;
            receipt.Evidence.Record("ObservationArmed");
            return receipt;
        }
    }
}
