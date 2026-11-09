using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Layout;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Projection;

namespace Inceptus.DiagramEngine.Contracts.Routing;

/// <summary>
/// Pure, synchronous preparation of notation-neutral obstacle domains before routing.
/// Returning null retains the complete graph domain for every edge. Implementations must
/// not mutate the Document, execute commands, or depend on derived Scene presentation.
/// </summary>
public interface IRoutingInputPreparer
{
    PreparedRoutingInput? Prepare(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ProjectedGraph graph,
        LayoutResult layout,
        CancellationToken cancellationToken);
}
