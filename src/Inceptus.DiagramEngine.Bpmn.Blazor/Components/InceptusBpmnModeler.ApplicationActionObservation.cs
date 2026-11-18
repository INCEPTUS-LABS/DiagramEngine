using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Components;

public partial class InceptusBpmnModeler
{
    internal BpmnApplicationActionObservation? BeginApplicationActionObservation() =>
        _disposed ? null : _canvas?.BeginApplicationActionObservation();
}
