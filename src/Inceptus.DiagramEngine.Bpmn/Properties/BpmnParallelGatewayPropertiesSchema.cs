using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.Bpmn.Properties;

internal static class BpmnParallelGatewayPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.ParallelGateway,
        BpmnGatewayPropertiesSchemaFields.Create(SemanticPropertyMutationKind.Name));
}

internal static class BpmnGatewayPropertiesSchemaFields
{
    internal static ElementPropertyFieldDefinition[] Create(
        SemanticPropertyMutationKind nameMutationKind) =>
        [
            new ElementPropertyFieldDefinition(
                new ElementPropertyFieldId("code"),
                "Code",
                BpmnSemanticProperties.Code,
                ElementPropertyEditorKind.SingleLineText,
                SemanticPropertyMutationKind.Property,
                isEditable: true,
                order: 0),
            new ElementPropertyFieldDefinition(
                new ElementPropertyFieldId("name"),
                "Name",
                BpmnSemanticProperties.Name,
                ElementPropertyEditorKind.SingleLineText,
                nameMutationKind,
                isEditable: true,
                order: 1),
            new ElementPropertyFieldDefinition(
                new ElementPropertyFieldId("description"),
                "Description",
                BpmnSemanticProperties.Description,
                ElementPropertyEditorKind.MultilineText,
                SemanticPropertyMutationKind.Property,
                isEditable: true,
                order: 2),
        ];
}
