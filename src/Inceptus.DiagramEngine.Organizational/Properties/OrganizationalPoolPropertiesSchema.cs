using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Organizational.Semantics;

namespace Inceptus.DiagramEngine.Organizational.Properties;

public static class OrganizationalPoolPropertiesSchema
{
    public static ElementPropertiesSchema Definition { get; } =
        new(
            OrganizationalSemanticTypes.Pool,
            [
                new ElementPropertyFieldDefinition(
                    new ElementPropertyFieldId("name"),
                    "Name",
                    OrganizationalSemanticProperties.Name,
                    ElementPropertyEditorKind.SingleLineText,
                    SemanticPropertyMutationKind.Name,
                    isEditable: true,
                    order: 0),
                new ElementPropertyFieldDefinition(
                    new ElementPropertyFieldId("participant"),
                    "Participant",
                    OrganizationalSemanticProperties.ParticipantId,
                    ElementPropertyEditorKind.SingleLineText,
                    SemanticPropertyMutationKind.Property,
                    isEditable: true,
                    order: 1),
                new ElementPropertyFieldDefinition(
                    new ElementPropertyFieldId("description"),
                    "Description",
                    OrganizationalSemanticProperties.Description,
                    ElementPropertyEditorKind.MultilineText,
                    SemanticPropertyMutationKind.Property,
                    isEditable: true,
                    order: 2),
            ]);
}
