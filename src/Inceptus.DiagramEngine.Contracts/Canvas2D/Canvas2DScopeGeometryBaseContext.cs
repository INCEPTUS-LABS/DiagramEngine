using Inceptus.DiagramEngine.Contracts.Layout;
using Inceptus.DiagramEngine.Contracts.Projection;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Contracts.Text;

namespace Inceptus.DiagramEngine.Contracts.Canvas2D;

/// <summary>Immutable inputs for notation-owned node appearance and pre-route text requests.</summary>
public sealed class Canvas2DScopeGeometryBaseContext
{
    public Canvas2DScopeGeometryBaseContext(
        ScopeGeometryInputs inputs,
        ProjectedGraph projectedGraph,
        LayoutResult localLayout,
        Canvas2DSceneConfiguration configuration,
        Canvas2DSceneContributorDescriptor contributor,
        TextMeasurementRequest textConfiguration)
        : this(inputs, projectedGraph, localLayout, configuration, contributor, textConfiguration, null)
    {
    }

    public Canvas2DScopeGeometryBaseContext(
        ScopeGeometryInputs inputs,
        ProjectedGraph projectedGraph,
        LayoutResult localLayout,
        Canvas2DSceneConfiguration configuration,
        Canvas2DSceneContributorDescriptor contributor,
        TextMeasurementRequest textConfiguration,
        ScopeGeometryInputs? previousInputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(projectedGraph);
        ArgumentNullException.ThrowIfNull(localLayout);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(contributor);
        ArgumentNullException.ThrowIfNull(textConfiguration);
        Inputs = inputs;
        ProjectedGraph = projectedGraph;
        LocalLayout = localLayout;
        Configuration = configuration;
        Contributor = contributor;
        TextConfiguration = textConfiguration;
        PreviousInputs = previousInputs;
    }

    public ScopeGeometryInputs Inputs { get; }
    /// <summary>Prior authoritative inputs, when preparing a committed edit.</summary>
    public ScopeGeometryInputs? PreviousInputs { get; }
    public ProjectedGraph ProjectedGraph { get; }
    public LayoutResult LocalLayout { get; }
    public Canvas2DSceneConfiguration Configuration { get; }
    public Canvas2DSceneContributorDescriptor Contributor { get; }
    public TextMeasurementRequest TextConfiguration { get; }
}
