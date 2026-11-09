using Inceptus.DiagramEngine.Bpmn.Projection;
using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Presentation;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Projection;
using Inceptus.DiagramEngine.Contracts.Visuals;

namespace Inceptus.DiagramEngine.Bpmn.Presentation;

internal static class BpmnParticipantPresentationContribution
{
    internal static ProjectionRuleId ProjectionRuleId { get; } = new("bpmn:projection/participant");
    internal static ProjectionRuleRegistration ProjectionRegistration { get; } = new(ProjectionRuleId,
        ProjectionSourceKind.SemanticElement, BpmnSemanticTypes.Participant,
        new BpmnParticipantGroupProjectionRule());
    internal static PresentationViewProviderRegistration ViewRegistration { get; } =
        new(BpmnSemanticTypes.Collaboration, new BpmnCollaborationViewProvider());
    internal static ElementConnectorAnchorPolicyRegistration AnchorRegistration { get; } =
        new(BpmnSemanticTypes.Participant, new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.DynamicUnlimited(ConnectorAnchorRoleCapability.SourceOrTarget),
            EdgeConnectorAnchorPolicy.DynamicUnlimited(ConnectorAnchorRoleCapability.SourceOrTarget),
            EdgeConnectorAnchorPolicy.DynamicUnlimited(ConnectorAnchorRoleCapability.SourceOrTarget),
            EdgeConnectorAnchorPolicy.DynamicUnlimited(ConnectorAnchorRoleCapability.SourceOrTarget)));
}
