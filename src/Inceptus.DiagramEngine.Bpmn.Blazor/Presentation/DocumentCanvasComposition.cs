using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.ConnectionCreation;
using Inceptus.DiagramEngine.Contracts.ContextMenus;
using Inceptus.DiagramEngine.Contracts.Creation;
using Inceptus.DiagramEngine.Contracts.Deletion;
using Inceptus.DiagramEngine.Contracts.EndpointReconnection;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Contracts.ScopeNavigation;
using Inceptus.DiagramEngine.Contracts.Toolbox;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Creates the downstream application composition attached by a document-canvas host.
/// </summary>
internal interface IDocumentCanvasCompositionFactory
{
    ValueTask<DocumentCanvasComposition> CreateAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable application-owned inputs for one document-canvas attachment.
/// </summary>
internal sealed record DocumentCanvasComposition
{
    internal DocumentCanvasComposition(
        Document document,
        EditingSessionConfiguration configuration,
        ElementPropertiesSchemaCatalog propertiesSchemaCatalog,
        IDocumentCanvasPipelineCounters? counters = null,
        ToolboxPlacementCatalog? toolboxPlacementCatalog = null,
        AnchorConnectionCreationCatalog? anchorConnectionCreationCatalog = null,
        IDocumentCreationIdentityProvider? documentCreationIdentityProvider = null,
        ConnectorEndpointReconnectionCatalog? endpointReconnectionCatalog = null,
        DiagramDeletionCatalog? deletionCatalog = null,
        ScopeNavigationCatalog? scopeNavigationCatalog = null,
        CanvasBackgroundActionCatalog? backgroundActionCatalog = null,
        SemanticSceneViewActionCatalog? semanticSceneViewActionCatalog = null,
        SemanticSceneCommandActionCatalog? semanticSceneCommandActionCatalog = null,
        Canvas2DSpatialEditPlannerCatalog? spatialEditPlanners = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(propertiesSchemaCatalog);
        Document = document;
        Configuration = configuration;
        PropertiesSchemaCatalog = propertiesSchemaCatalog;
        Counters = counters;
        ToolboxPlacementCatalog = toolboxPlacementCatalog ?? ToolboxPlacementCatalog.Empty;
        AnchorConnectionCreationCatalog = anchorConnectionCreationCatalog ??
            AnchorConnectionCreationCatalog.Empty;
        DocumentCreationIdentityProvider = documentCreationIdentityProvider ??
            GuidDocumentCreationIdentityProvider.Instance;
        EndpointReconnectionCatalog = endpointReconnectionCatalog ??
            ConnectorEndpointReconnectionCatalog.Empty;
        DeletionCatalog = deletionCatalog ?? DiagramDeletionCatalog.Empty;
        ScopeNavigationCatalog = scopeNavigationCatalog ?? ScopeNavigationCatalog.Empty;
        BackgroundActionCatalog = backgroundActionCatalog ??
            CanvasBackgroundActionCatalog.Empty;
        SemanticSceneViewActionCatalog = semanticSceneViewActionCatalog ??
            SemanticSceneViewActionCatalog.Empty;
        SemanticSceneCommandActionCatalog = semanticSceneCommandActionCatalog ??
            SemanticSceneCommandActionCatalog.Empty;
        SpatialEditPlanners = spatialEditPlanners ?? Canvas2DSpatialEditPlannerCatalog.Empty;
    }

    internal Document Document { get; }

    internal EditingSessionConfiguration Configuration { get; }

    internal ElementPropertiesSchemaCatalog PropertiesSchemaCatalog { get; }

    internal IDocumentCanvasPipelineCounters? Counters { get; }

    internal ToolboxPlacementCatalog ToolboxPlacementCatalog { get; }

    internal AnchorConnectionCreationCatalog AnchorConnectionCreationCatalog { get; }

    internal IDocumentCreationIdentityProvider DocumentCreationIdentityProvider { get; }

    internal ConnectorEndpointReconnectionCatalog EndpointReconnectionCatalog { get; }

    internal DiagramDeletionCatalog DeletionCatalog { get; }


    internal ScopeNavigationCatalog ScopeNavigationCatalog { get; }

    internal CanvasBackgroundActionCatalog BackgroundActionCatalog { get; }

    internal SemanticSceneViewActionCatalog SemanticSceneViewActionCatalog { get; }

    internal SemanticSceneCommandActionCatalog SemanticSceneCommandActionCatalog { get; }

    internal Canvas2DSpatialEditPlannerCatalog SpatialEditPlanners { get; }
}

/// <summary>
/// Optional read-only pipeline instrumentation displayed by the presentation host.
/// </summary>
internal interface IDocumentCanvasPipelineCounters
{
    int ProjectionRuleInvocationCount { get; }

    int LayoutInvocationCount { get; }

    int RoutingInvocationCount { get; }

    int SceneContributionInvocationCount { get; }
}
