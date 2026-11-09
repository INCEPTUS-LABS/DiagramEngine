using System.Collections.Immutable;
using Inceptus.DiagramEngine.Bpmn.Commands;
using Inceptus.DiagramEngine.Bpmn.History;
using Inceptus.DiagramEngine.Bpmn.Properties;
using Inceptus.DiagramEngine.Bpmn.Validation;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.History;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Contracts.Validation;

namespace Inceptus.DiagramEngine.Bpmn;

internal static class BpmnMessageFlowContribution
{
    internal static ImmutableArray<CommandHandlerRegistration> CommandHandlers { get; } =
    [
        Handler<SetBpmnPoolParticipantCommand>(SetBpmnPoolParticipantCommand.KnownTypeId, new BpmnPoolParticipantCommandHandler()),
        Handler<CreateBpmnElementFlowCommand>(CreateBpmnElementFlowCommand.KnownTypeId, new BpmnElementFlowCommandHandler()),
        new(RestoreBpmnElementFlowCommand.KnownTypeId, new RestoreBpmnElementFlowCommandHandler(), new RestoreBpmnElementFlowCommandHandler()),
        Handler<CreateBpmnMessageFlowCommand>(CreateBpmnMessageFlowCommand.KnownTypeId, new BpmnMessageFlowCreationCommandHandler()),
        Handler<CreateBpmnMessageFlowWithTargetAnchorCommand>(CreateBpmnMessageFlowWithTargetAnchorCommand.KnownTypeId, new BpmnMessageFlowWithTargetAnchorCreationCommandHandler()),
        Handler<ReconnectBpmnMessageFlowEndpointCommand>(ReconnectBpmnMessageFlowEndpointCommand.KnownTypeId, new BpmnMessageFlowEndpointReconnectionCommandHandler()),
        Handler<UpdateBpmnMessageFlowNameCommand>(UpdateBpmnMessageFlowNameCommand.KnownTypeId, new BpmnMessageFlowNameCommandHandler()),
        Handler<DeleteBpmnMessageFlowCommand>(DeleteBpmnMessageFlowCommand.KnownTypeId, new BpmnMessageFlowDeletionCommandHandler()),
        new(RestoreBpmnMessageFlowCommand.KnownTypeId, new RestoreBpmnMessageFlowCommandHandler(), new RestoreBpmnMessageFlowCommandHandler()),
    ];
    internal static ImmutableArray<CommandValidatorRegistration> CommandValidators { get; } =
    [
        Validator(SetBpmnPoolParticipantCommand.KnownTypeId, new BpmnPoolParticipantCommandHandler()),
        Validator(Inceptus.DiagramEngine.Organizational.Commands.AssignOrganizationalElementCommand.KnownTypeId, new BpmnPoolMembershipFlowValidator()),
        Validator(Inceptus.DiagramEngine.Organizational.Commands.UnassignOrganizationalElementCommand.KnownTypeId, new BpmnPoolMembershipFlowValidator()),
        Validator(Inceptus.DiagramEngine.Organizational.Commands.DeleteOrganizationalPoolCommand.KnownTypeId, new BpmnPoolMembershipFlowValidator()),
        Validator(SetModelProfileAvailabilityCommand.KnownTypeId, new BpmnPoolProfileAvailabilityValidator()),
        Validator(CreateBpmnElementFlowCommand.KnownTypeId, new BpmnElementFlowCommandHandler()),
        Validator(CreateBpmnMessageFlowCommand.KnownTypeId, new BpmnMessageFlowCreationCommandHandler()),
        Validator(CreateBpmnMessageFlowWithTargetAnchorCommand.KnownTypeId, new BpmnMessageFlowWithTargetAnchorCreationCommandHandler()),
        Validator(ReconnectBpmnMessageFlowEndpointCommand.KnownTypeId, new BpmnMessageFlowEndpointReconnectionValidator()),
        Validator(UpdateBpmnMessageFlowNameCommand.KnownTypeId, new BpmnMessageFlowNameCommandHandler()),
        Validator(DeleteBpmnMessageFlowCommand.KnownTypeId, new BpmnMessageFlowDeletionCommandHandler()),
    ];
    internal static ImmutableArray<CommandHistoryPolicyRegistration> HistoryPolicies { get; } =
    [
        new(SetBpmnPoolParticipantCommand.KnownTypeId, new BpmnPoolParticipantHistoryPolicy()),
        new(CreateBpmnElementFlowCommand.KnownTypeId, new BpmnElementFlowHistoryPolicy()),
        new(CreateBpmnMessageFlowCommand.KnownTypeId, new BpmnMessageFlowLifetimeHistoryPolicy()),
        new(CreateBpmnMessageFlowWithTargetAnchorCommand.KnownTypeId, new BpmnMessageFlowLifetimeHistoryPolicy()),
        new(DeleteBpmnMessageFlowCommand.KnownTypeId, new BpmnMessageFlowLifetimeHistoryPolicy()),
        new(ReconnectBpmnMessageFlowEndpointCommand.KnownTypeId, new BpmnMessageFlowEndpointReconnectionHistoryPolicy()),
        new(UpdateBpmnMessageFlowNameCommand.KnownTypeId, new BpmnMessageFlowNameHistoryPolicy()),
    ];
    internal static ImmutableArray<ElementPropertiesSchema> PropertiesSchemas { get; } = [BpmnMessageFlowPropertiesSchema.Definition, BpmnParticipantPropertiesSchema.Definition];
    internal static ImmutableArray<IModelValidationRule> ModelValidationRules { get; } = [new BpmnMessageFlowValidationRule(), new BpmnPoolFlowValidationRule()];

    private static CommandHandlerRegistration Handler<T>(CommandTypeId id, ICommandHandler handler) where T : class, ICommand =>
        new(id, new BpmnCommandEnvelopeValidator<T>(), handler);
    private static CommandValidatorRegistration Validator(CommandTypeId id, ICommandValidator validator) =>
        new(id, new CommandValidatorId(id.Value + ":validator"), validator);
}
