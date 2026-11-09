using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.Bpmn.Properties;

internal static class BpmnEventBasedGatewayPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.EventBasedGateway,
        BpmnGatewayPropertiesSchemaFields.Create(SemanticPropertyMutationKind.Property));
}
