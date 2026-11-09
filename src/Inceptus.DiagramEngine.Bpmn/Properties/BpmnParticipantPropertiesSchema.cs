using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.Bpmn.Properties;

internal static class BpmnParticipantPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(BpmnSemanticTypes.Participant,
        [new ElementPropertyFieldDefinition(new ElementPropertyFieldId("name"), "Name", BpmnSemanticProperties.Name,
            ElementPropertyEditorKind.SingleLineText, SemanticPropertyMutationKind.Name, isEditable: true, order: 0)]);
}
