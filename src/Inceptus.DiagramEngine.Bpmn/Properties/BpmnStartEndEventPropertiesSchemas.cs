using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.Bpmn.Properties;

internal static class BpmnStartEndEventPropertiesSchemas
{
    internal static ElementPropertiesSchema Start { get; } = new(BpmnSemanticTypes.StartEvent, [Name()]);
    internal static ElementPropertiesSchema End { get; } = new(BpmnSemanticTypes.EndEvent, [Name()]);

    private static ElementPropertyFieldDefinition Name() => new(
        new ElementPropertyFieldId("name"), "Name", BpmnSemanticProperties.Name,
        ElementPropertyEditorKind.SingleLineText, SemanticPropertyMutationKind.Property,
        isEditable: true, order: 0);
}
