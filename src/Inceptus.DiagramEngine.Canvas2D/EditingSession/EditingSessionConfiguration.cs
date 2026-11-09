using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.History;
using Inceptus.DiagramEngine.Contracts.Layout;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Profiles;
using Inceptus.DiagramEngine.Contracts.Projection;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Runtime.Layout;
using Inceptus.DiagramEngine.Runtime.Projection;
using Inceptus.DiagramEngine.Runtime.Routing;

namespace Inceptus.DiagramEngine.Canvas2D.EditingSession;

/// <summary>
/// Immutable composition for one Editing Session. The concrete engines remain framework-owned.
/// </summary>
public sealed class EditingSessionConfiguration
{
    public EditingSessionConfiguration(
        ProjectionEngine projectionEngine,
        LayoutEngine layoutEngine,
        AlgorithmId layoutAlgorithmId,
        RoutingEngine routingEngine,
        AlgorithmId routingAlgorithmId,
        Canvas2DSceneBuilder sceneBuilder,
        ProjectionContext? projectionContext = null,
        LayoutContext? layoutContext = null,
        RoutingContext? routingContext = null,
        EditorStateSnapshot? initialEditorState = null,
        IEnumerable<CommandHandlerRegistration>? commandHandlers = null,
        IEnumerable<CommandValidatorRegistration>? commandValidators = null,
        IEnumerable<CommandHistoryPolicyRegistration>? historyPolicies = null,
        IEnumerable<IDocumentChangedSubscriber>? documentChangedSubscribers = null,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null,
        ModelProfileCatalog? modelProfileCatalog = null,
        ModelProfileViewStateSnapshot? initialModelProfileViewState = null,
        IRoutingInputPreparer? routingInputPreparer = null,
        IGridSettingsSource? gridSettings = null)
    {
        ArgumentNullException.ThrowIfNull(projectionEngine);
        ArgumentNullException.ThrowIfNull(layoutEngine);
        ArgumentNullException.ThrowIfNull(layoutAlgorithmId);
        ArgumentNullException.ThrowIfNull(routingEngine);
        ArgumentNullException.ThrowIfNull(routingAlgorithmId);
        ArgumentNullException.ThrowIfNull(sceneBuilder);

        ProjectionEngine = projectionEngine;
        LayoutEngine = layoutEngine;
        LayoutAlgorithmId = layoutAlgorithmId;
        RoutingEngine = routingEngine;
        RoutingAlgorithmId = routingAlgorithmId;
        SceneBuilder = sceneBuilder;
        ProjectionContext = projectionContext ?? ProjectionContext.Empty;
        LayoutContext = layoutContext ?? LayoutContext.Empty;
        RoutingContext = routingContext ?? RoutingContext.Empty;
        RoutingInputPreparer = routingInputPreparer;
        GridSettings = gridSettings;
        InitialEditorState = initialEditorState ?? EditorStateSnapshot.Empty;
        CommandHandlers = Copy(commandHandlers, nameof(commandHandlers));
        CommandValidators = Copy(commandValidators, nameof(commandValidators));
        HistoryPolicies = Copy(historyPolicies, nameof(historyPolicies));
        DocumentChangedSubscribers = Copy(
            documentChangedSubscribers,
            nameof(documentChangedSubscribers));
        ConnectorAnchorPolicyProvider = connectorAnchorPolicyProvider ??
            ElementConnectorAnchorPolicyRegistry.Default;
        ModelProfileCatalog = modelProfileCatalog ?? ModelProfileCatalog.Empty;
        InitialModelProfileViewState = initialModelProfileViewState ??
            ModelProfileViewStateSnapshot.Empty;
    }

    public ProjectionEngine ProjectionEngine { get; }

    public LayoutEngine LayoutEngine { get; }

    public AlgorithmId LayoutAlgorithmId { get; }

    public RoutingEngine RoutingEngine { get; }

    public AlgorithmId RoutingAlgorithmId { get; }

    public Canvas2DSceneBuilder SceneBuilder { get; }

    public ProjectionContext ProjectionContext { get; }

    public LayoutContext LayoutContext { get; }

    public RoutingContext RoutingContext { get; }

    public IRoutingInputPreparer? RoutingInputPreparer { get; }

    public IGridSettingsSource? GridSettings { get; }

    public EditorStateSnapshot InitialEditorState { get; }

    public ImmutableArray<CommandHandlerRegistration> CommandHandlers { get; }

    public ImmutableArray<CommandValidatorRegistration> CommandValidators { get; }

    public ImmutableArray<CommandHistoryPolicyRegistration> HistoryPolicies { get; }

    public ImmutableArray<IDocumentChangedSubscriber> DocumentChangedSubscribers { get; }

    public IElementConnectorAnchorPolicyProvider ConnectorAnchorPolicyProvider { get; }

    /// <summary>
    /// Gets the immutable optional-profile definitions available to this view session.
    /// </summary>
    public ModelProfileCatalog ModelProfileCatalog { get; }

    /// <summary>
    /// Gets the transient visibility preference used for the initially active Main view.
    /// </summary>
    public ModelProfileViewStateSnapshot InitialModelProfileViewState { get; }

    private static ImmutableArray<T> Copy<T>(IEnumerable<T>? values, string parameterName)
        where T : class
    {
        if (values is null)
        {
            return [];
        }

        var copy = values.ToArray();
        if (Array.Exists(copy, static value => value is null))
        {
            throw new ArgumentException("Registrations cannot contain null values.", parameterName);
        }

        return [.. copy];
    }
}
