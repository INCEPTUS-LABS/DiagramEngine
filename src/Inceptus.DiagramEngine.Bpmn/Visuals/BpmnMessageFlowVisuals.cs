using Inceptus.DiagramEngine.Contracts.Visuals;

namespace Inceptus.DiagramEngine.Bpmn.Visuals;

internal static class BpmnMessageFlowVisuals
{
    internal static ConnectorVisualDescriptor Descriptor { get; } = new([6d, 4d],
        ConnectorEndpointDecoration.OpenCircle, ConnectorEndpointDecoration.OpenArrow);
}
