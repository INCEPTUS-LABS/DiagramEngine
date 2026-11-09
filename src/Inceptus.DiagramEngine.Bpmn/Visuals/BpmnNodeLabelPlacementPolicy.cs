using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Projection;

namespace Inceptus.DiagramEngine.Bpmn.Visuals;

internal static class BpmnNodeLabelPlacementPolicy
{
    internal static NodeLabelPlacement OutsideBelow { get; } =
        new(NodeLabelPlacementKind.OutsideBelow, gap: 8d, maximumWidth: 160d);

    internal static NodeLabelPlacement Resolve(SemanticTypeId type) =>
        BpmnSemanticTypes.IsGateway(type) ||
        BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(type) ||
        BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(type)
            ? OutsideBelow
            : NodeLabelPlacement.InsideCentered;
}
