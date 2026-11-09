using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.Bpmn.Properties;

internal static class BpmnInclusiveGatewayPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.InclusiveGateway,
        BpmnGatewayPropertiesSchemaFields.Create(SemanticPropertyMutationKind.Name));
}
