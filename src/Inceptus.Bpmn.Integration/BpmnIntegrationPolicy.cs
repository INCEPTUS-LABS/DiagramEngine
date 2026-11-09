using Inceptus.DiagramEngine.Bpmn;
using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Contracts.Validation;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Organizational;
using Inceptus.DiagramEngine.Organizational.Scene;
using Inceptus.DiagramEngine.Organizational.Semantics;
using Inceptus.DiagramEngine.Runtime.Projection;

namespace Inceptus.Bpmn.Integration;

internal static class BpmnIntegrationPolicy
{
    internal static IElementConnectorAnchorPolicyProvider ConnectorAnchors { get; } =
        new ElementConnectorAnchorPolicyRegistry(BpmnPluginRegistration.N100.ConnectorAnchorPolicies);

    internal static ModelValidationCatalog ValidationRules { get; } = CreateRules();

    internal static ProjectionEngine Projection { get; } = new(BpmnPluginRegistration.N100.ProjectionRules
        .AddRange(CreateOrganizational().ProjectionRules), BpmnPluginRegistration.N100.PresentationViewProviders);

    private static ModelValidationCatalog CreateRules()
    {
        var organizational = CreateOrganizational();
        return new ModelValidationCatalog(BpmnPluginRegistration.N100.ModelValidationRules
            .AddRange(organizational.ModelValidationRules));
    }

    private static OrganizationalPluginRegistration CreateOrganizational()
    {
        var eligibility = new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode);
        return OrganizationalPluginRegistration.Create(eligibility,
            OrganizationalPoolSceneContributor.CreateRegistration(eligibility));
    }
}
