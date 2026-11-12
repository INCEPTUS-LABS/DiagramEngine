using Inceptus.DiagramEngine.Bpmn;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.ConnectionCreation;
using Inceptus.DiagramEngine.Contracts.ContextMenus;
using Inceptus.DiagramEngine.Contracts.Deletion;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.EndpointReconnection;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Contracts.Profiles;
using Inceptus.DiagramEngine.Contracts.ScopeNavigation;
using Inceptus.DiagramEngine.Contracts.Toolbox;
using Inceptus.DiagramEngine.Contracts.Validation;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Organizational;
using Inceptus.DiagramEngine.Organizational.Scene;
using Inceptus.DiagramEngine.Organizational.Semantics;
using Inceptus.DiagramEngine.Runtime.Documents;
using Inceptus.DiagramEngine.Runtime.Layout;
using Inceptus.DiagramEngine.Runtime.Projection;
using Inceptus.DiagramEngine.Runtime.Routing;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Composition;

/// <summary>
/// Single production authority for the BPMN and Organizational modeler composition.
/// </summary>
internal static class BpmnModelerComposition
{
    private static readonly BpmnPluginRegistration BpmnRegistration =
        BpmnPluginRegistration.N100;

    private static readonly OrganizationalPluginRegistration OrganizationalRegistration =
        CreateOrganizationalRegistration();

    private static readonly IElementConnectorAnchorPolicyProvider ConnectorAnchorPolicyProvider =
        new ElementConnectorAnchorPolicyRegistry(BpmnRegistration.ConnectorAnchorPolicies);

    internal static ToolboxCatalog ToolboxCatalog { get; } =
        new(BpmnRegistration.ToolboxContributions);

    internal static DocumentCanvasComposition Create(Document document,
        EditingSessionAccessMode accessMode = EditingSessionAccessMode.Editable)
    {
        ArgumentNullException.ThrowIfNull(document);

        // A startup provider deliberately knows nothing about the RCL's milestone-specific
        // composition. Reconstructing the supplied persistent snapshot here proves that it is
        // valid under the same canonical BPMN connector-anchor policy used by this modeler,
        // while the originally supplied Document remains the instance attached and owned by
        // the component.
        var canonicalReconstruction = ReconstructDocument(document.CaptureSnapshot());
        if (!canonicalReconstruction.Succeeded)
        {
            throw new InvalidOperationException(
                "The initial Document is not valid for the BPMN modeler connector-anchor policy.");
        }

        var configuration = new EditingSessionConfiguration(
            new ProjectionEngine(
                BpmnRegistration.ProjectionRules.AddRange(
                    OrganizationalRegistration.ProjectionRules),
                BpmnRegistration.PresentationViewProviders),
            new LayoutEngine(BpmnRegistration.LayoutAlgorithms),
            BpmnAlgorithmIds.DefaultLayout,
            new RoutingEngine(BpmnRegistration.RoutingAlgorithms),
            BpmnAlgorithmIds.DefaultRouting,
            new Canvas2DSceneBuilder(contributors:
                BpmnRegistration.SceneContributors.AddRange(
                    OrganizationalRegistration.SceneContributors)),
            initialEditorState: EditorStateSnapshot.Empty,
            commandHandlers: BpmnRegistration.CommandHandlers.AddRange(
                OrganizationalRegistration.CommandHandlers),
            commandValidators: BpmnRegistration.CommandValidators.AddRange(
                OrganizationalRegistration.CommandValidators),
            historyPolicies: BpmnRegistration.HistoryPolicies.AddRange(
                OrganizationalRegistration.HistoryPolicies),
            connectorAnchorPolicyProvider: ConnectorAnchorPolicyProvider,
            modelProfileCatalog: new ModelProfileCatalog(
                BpmnRegistration.ModelProfileDefinitions),
            routingInputPreparer: OrganizationalRegistration.RoutingInputPreparer).WithAccessMode(accessMode);

        return new DocumentCanvasComposition(
            document,
            configuration,
            new ElementPropertiesSchemaCatalog(
                BpmnRegistration.PropertiesSchemas.AddRange(
                    OrganizationalRegistration.PropertiesSchemas)),
            toolboxPlacementCatalog: new ToolboxPlacementCatalog(
                BpmnRegistration.ToolboxPlacementRegistrations,
                ToolboxCatalog),
            anchorConnectionCreationCatalog: new AnchorConnectionCreationCatalog(
                BpmnRegistration.AnchorConnectionCreationRegistrations),
            endpointReconnectionCatalog: new ConnectorEndpointReconnectionCatalog(
                BpmnRegistration.ConnectorEndpointReconnectionRegistrations),
            deletionCatalog: new DiagramDeletionCatalog(
                BpmnRegistration.DiagramDeletionRegistrations.AddRange(
                    OrganizationalRegistration.DiagramDeletionRegistrations)),
            scopeNavigationCatalog: new ScopeNavigationCatalog(
                BpmnRegistration.ScopeNavigationRegistrations),
            backgroundActionCatalog: new CanvasBackgroundActionCatalog(
                OrganizationalRegistration.BackgroundActions),
            semanticSceneViewActionCatalog: new SemanticSceneViewActionCatalog(
                OrganizationalRegistration.SemanticSceneViewActions),
            semanticSceneCommandActionCatalog: new SemanticSceneCommandActionCatalog(
                OrganizationalRegistration.SemanticSceneCommandActions),
            spatialEditPlanners: new Canvas2DSpatialEditPlannerCatalog(
                OrganizationalRegistration.SpatialEditPlannerRegistrations));
    }

    internal static Document CreateEmptyDocument()
    {
        var creation = DocumentFactory.CreateEmpty(
            new DocumentId($"document:{Guid.NewGuid():N}"),
            connectorAnchorPolicyProvider: ConnectorAnchorPolicyProvider);
        return creation.Document ?? throw new InvalidOperationException(
            "The initial empty BPMN Document could not be created.");
    }

    internal static DocumentConstructionResult ReconstructDocument(DocumentSnapshot snapshot) =>
        DocumentReconstructor.Reconstruct(snapshot, ConnectorAnchorPolicyProvider);

    private static OrganizationalPluginRegistration CreateOrganizationalRegistration()
    {
        var eligibilityPolicy = new OrganizationalElementEligibilityPolicy(
            BpmnSemanticTypes.IsFlowNode);
        return OrganizationalPluginRegistration.Create(
            eligibilityPolicy,
            OrganizationalPoolSceneContributor.CreateRegistration(eligibilityPolicy,
                new Inceptus.DiagramEngine.Bpmn.Appearance.BpmnPoolHeaderPaintProvider()));
    }
}
