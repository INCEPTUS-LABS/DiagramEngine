using Inceptus.DiagramEngine.Contracts.Layout;
using Inceptus.DiagramEngine.Contracts.Projection;

namespace Inceptus.DiagramEngine.Contracts.Routing;

/// <summary>
/// Defines a replaceable, read-only routing policy invoked by the framework-owned Routing Engine.
/// </summary>
public interface IRoutingAlgorithm
{
    RoutingAlgorithmResult Route(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingContext context,
        CancellationToken cancellationToken);
}
