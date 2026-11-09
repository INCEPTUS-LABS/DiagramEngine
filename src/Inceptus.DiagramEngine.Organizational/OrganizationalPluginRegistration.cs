using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.ContextMenus;
using Inceptus.DiagramEngine.Contracts.Deletion;
using Inceptus.DiagramEngine.Contracts.History;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Profiles;
using Inceptus.DiagramEngine.Contracts.Projection;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Contracts.Validation;
using Inceptus.DiagramEngine.Organizational.Commands;
using Inceptus.DiagramEngine.Organizational.ContextMenus;
using Inceptus.DiagramEngine.Organizational.Deletion;
using Inceptus.DiagramEngine.Organizational.History;
using Inceptus.DiagramEngine.Organizational.Profiles;
using Inceptus.DiagramEngine.Organizational.Projection;
using Inceptus.DiagramEngine.Organizational.Properties;
using Inceptus.DiagramEngine.Organizational.Semantics;
using Inceptus.DiagramEngine.Organizational.Spatial;
using Inceptus.DiagramEngine.Organizational.Validation;

namespace Inceptus.DiagramEngine.Organizational;

/// <summary>
/// Complete leaf-plugin registrations. The host supplies only notation eligibility and
/// the optional Scene implementation; all Organizational mutation policy stays here.
/// </summary>
public sealed class OrganizationalPluginRegistration
{
    private OrganizationalPluginRegistration(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy,
        Canvas2DSceneContributorRegistration? sceneContributor)
    {
        ArgumentNullException.ThrowIfNull(eligibilityPolicy);
        var restorationHandler = new RestoreOrganizationalSnapshotCommandHandler();
        CommandHandlers =
        [
            Handler<SetOrganizationalScopeWidthCommand>(SetOrganizationalScopeWidthCommand.KnownTypeId,
                new SetOrganizationalScopeWidthCommandHandler()),
            Handler<SetOrganizationalRegionExpandedHeightCommand>(
                SetOrganizationalRegionExpandedHeightCommand.KnownTypeId,
                new SetOrganizationalRegionExpandedHeightCommandHandler()),
            Handler<CreateOrganizationalPoolCommand>(
                CreateOrganizationalPoolCommand.KnownTypeId,
                new OrganizationalCommandHandler<CreateOrganizationalPoolCommand>(
                    eligibilityPolicy)),
            Handler<AssignOrganizationalElementCommand>(
                AssignOrganizationalElementCommand.KnownTypeId,
                new OrganizationalCommandHandler<AssignOrganizationalElementCommand>(
                    eligibilityPolicy)),
            Handler<UnassignOrganizationalElementCommand>(
                UnassignOrganizationalElementCommand.KnownTypeId,
                new OrganizationalCommandHandler<UnassignOrganizationalElementCommand>(
                    eligibilityPolicy)),
            Handler<MoveOrganizationalPoolCommand>(
                MoveOrganizationalPoolCommand.KnownTypeId,
                new OrganizationalCommandHandler<MoveOrganizationalPoolCommand>(
                    eligibilityPolicy)),
            Handler<MoveOrganizationalLaneCommand>(MoveOrganizationalLaneCommand.KnownTypeId,
                new OrganizationalCommandHandler<MoveOrganizationalLaneCommand>(eligibilityPolicy)),
            Handler<RestoreOrganizationalPoolOrderCommand>(RestoreOrganizationalPoolOrderCommand.KnownTypeId,
                new RestoreOrganizationalPoolOrderCommandHandler()),
            Handler<DeleteOrganizationalPoolCommand>(
                DeleteOrganizationalPoolCommand.KnownTypeId,
                new OrganizationalCommandHandler<DeleteOrganizationalPoolCommand>(
                    eligibilityPolicy)),
            new CommandHandlerRegistration(
                RestoreOrganizationalSnapshotCommand.KnownTypeId,
                restorationHandler,
                restorationHandler),
        ];
        CommandValidators =
        [
            Validator<CreateOrganizationalPoolCommand>(
                CreateOrganizationalPoolCommand.KnownTypeId,
                "inceptus:organizational/validator/create-pool",
                eligibilityPolicy),
            Validator<AssignOrganizationalElementCommand>(
                AssignOrganizationalElementCommand.KnownTypeId,
                "inceptus:organizational/validator/assign-element",
                eligibilityPolicy),
            Validator<UnassignOrganizationalElementCommand>(
                UnassignOrganizationalElementCommand.KnownTypeId,
                "inceptus:organizational/validator/unassign-element",
                eligibilityPolicy),
            Validator<MoveOrganizationalPoolCommand>(
                MoveOrganizationalPoolCommand.KnownTypeId,
                "inceptus:organizational/validator/move-pool",
                eligibilityPolicy),
            Validator<MoveOrganizationalLaneCommand>(MoveOrganizationalLaneCommand.KnownTypeId,
                "inceptus:organizational/validator/move-lane", eligibilityPolicy),
            Validator<DeleteOrganizationalPoolCommand>(
                DeleteOrganizationalPoolCommand.KnownTypeId,
                "inceptus:organizational/validator/delete-pool",
                eligibilityPolicy),
            new CommandValidatorRegistration(
                UpdateSemanticElementNameCommand.KnownTypeId,
                new CommandValidatorId(
                    "inceptus:organizational/validator/pool-name-update"),
                new OrganizationalPoolGenericUpdateValidator()),
            new CommandValidatorRegistration(
                UpdateSemanticElementPropertyCommand.KnownTypeId,
                new CommandValidatorId(
                    "inceptus:organizational/validator/pool-property-update"),
                new OrganizationalPoolGenericUpdateValidator()),
        ];
        HistoryPolicies =
        [
            new CommandHistoryPolicyRegistration(SetOrganizationalScopeWidthCommand.KnownTypeId,
                new OrganizationalScopeWidthHistoryPolicy()),
            new CommandHistoryPolicyRegistration(SetOrganizationalRegionExpandedHeightCommand.KnownTypeId,
                new OrganizationalRegionHeightHistoryPolicy()),
            History<CreateOrganizationalPoolCommand>(
                CreateOrganizationalPoolCommand.KnownTypeId,
                eligibilityPolicy),
            History<AssignOrganizationalElementCommand>(
                AssignOrganizationalElementCommand.KnownTypeId,
                eligibilityPolicy),
            History<UnassignOrganizationalElementCommand>(
                UnassignOrganizationalElementCommand.KnownTypeId,
                eligibilityPolicy),
            new CommandHistoryPolicyRegistration(MoveOrganizationalPoolCommand.KnownTypeId, new OrganizationalPoolOrderHistoryPolicy()),
            new CommandHistoryPolicyRegistration(MoveOrganizationalLaneCommand.KnownTypeId, new OrganizationalPoolOrderHistoryPolicy()),
            History<DeleteOrganizationalPoolCommand>(
                DeleteOrganizationalPoolCommand.KnownTypeId,
                eligibilityPolicy),
        ];
        ProjectionRules = [OrganizationalPoolProjectionRule.Registration];
        RoutingInputPreparer = new OrganizationalRoutingInputPreparer(eligibilityPolicy);
        PropertiesSchemas = [OrganizationalPoolPropertiesSchema.Definition];
        DiagramDeletionRegistrations =
        [
            OrganizationalPoolDeletionContribution.CreateRegistration(
                eligibilityPolicy),
        ];
        ModelValidationRules =
        [
            new OrganizationalStructuralValidationRule(eligibilityPolicy),
        ];
        SceneContributors = sceneContributor is null ? [] : [sceneContributor];
        SpatialEditPlannerRegistrations =
        [
            new Canvas2DSpatialEditPlannerRegistration(
                OrganizationalModelProfile.Id,
                new OrganizationalSpatialEditPlanner(eligibilityPolicy)),
        ];
        ModelProfileDefinitions = [OrganizationalModelProfile.Definition];
        BackgroundActions = OrganizationalCanvasBackgroundActions.Definitions;
        SemanticSceneViewActions = OrganizationalPoolActions.ViewDefinitions;
        SemanticSceneCommandActions = OrganizationalPoolActions.CommandDefinitions;
    }

    public ImmutableArray<CommandHandlerRegistration> CommandHandlers { get; }

    public ImmutableArray<CommandValidatorRegistration> CommandValidators { get; }

    public ImmutableArray<CommandHistoryPolicyRegistration> HistoryPolicies { get; }

    public ImmutableArray<ProjectionRuleRegistration> ProjectionRules { get; }

    public IRoutingInputPreparer RoutingInputPreparer { get; }

    public ImmutableArray<ElementPropertiesSchema> PropertiesSchemas { get; }

    public ImmutableArray<DiagramDeletionRegistration> DiagramDeletionRegistrations
    { get; }

    public ImmutableArray<IModelValidationRule> ModelValidationRules { get; }

    public ImmutableArray<Canvas2DSceneContributorRegistration> SceneContributors { get; }

    public ImmutableArray<Canvas2DSpatialEditPlannerRegistration>
        SpatialEditPlannerRegistrations
    { get; }

    public ImmutableArray<ModelProfileDefinition> ModelProfileDefinitions { get; }

    public ImmutableArray<CanvasBackgroundActionDefinition> BackgroundActions { get; }

    public ImmutableArray<SemanticSceneViewActionDefinition> SemanticSceneViewActions
    { get; }

    public ImmutableArray<SemanticSceneCommandActionDefinition> SemanticSceneCommandActions
    { get; }

    public static OrganizationalPluginRegistration Create(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy,
        Canvas2DSceneContributorRegistration? sceneContributor = null) =>
        new(eligibilityPolicy, sceneContributor);

    private static CommandHandlerRegistration Handler<TCommand>(
        CommandTypeId typeId,
        ICommandHandler handler)
        where TCommand : class, ICommand =>
        new(
            typeId,
            new OrganizationalCommandEnvelopeValidator<TCommand>(),
            handler);

    private static CommandValidatorRegistration Validator<TCommand>(
        CommandTypeId typeId,
        string validatorId,
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
        where TCommand : OrganizationalCommand =>
        new(
            typeId,
            new CommandValidatorId(validatorId),
            new OrganizationalCommandValidator<TCommand>(eligibilityPolicy));

    private static CommandHistoryPolicyRegistration History<TCommand>(
        CommandTypeId typeId,
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
        where TCommand : OrganizationalCommand =>
        new(
            typeId,
            new OrganizationalHistoryPolicy<TCommand>(eligibilityPolicy));
}
