using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Bpmn.Visuals;

public static class BpmnGridPolicy
{
    public static bool Supports(SemanticTypeId type) =>
        BpmnSemanticTypes.IsFlowNode(type) && !BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(type);
}
