using Inceptus.DiagramEngine.Contracts.Projection;

namespace Inceptus.DiagramEngine.Contracts.Layout;

/// <summary>
/// Defines a replaceable, read-only layout policy invoked by the framework-owned Layout Engine.
/// </summary>
public interface ILayoutAlgorithm
{
    LayoutAlgorithmResult Compute(
        ProjectedGraph graph,
        LayoutContext context,
        CancellationToken cancellationToken);
}
