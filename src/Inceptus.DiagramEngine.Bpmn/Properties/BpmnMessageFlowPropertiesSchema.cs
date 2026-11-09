using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.Bpmn.Properties;

internal static class BpmnMessageFlowPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.MessageFlow,
        [new ElementPropertyFieldDefinition(new ElementPropertyFieldId("name"), "Name",
            BpmnSemanticProperties.Name, ElementPropertyEditorKind.SingleLineText,
            SemanticPropertyMutationKind.Property, isEditable: true, order: 0)]);
}
