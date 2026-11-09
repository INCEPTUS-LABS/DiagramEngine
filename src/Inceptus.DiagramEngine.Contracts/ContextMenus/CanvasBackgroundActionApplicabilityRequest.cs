using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.ContextMenus;

/// <summary>
/// Immutable read-only model context used to discover applicable empty-Canvas actions.
/// Command-planning capabilities such as identity allocation are intentionally excluded.
/// </summary>
public sealed record CanvasBackgroundActionApplicabilityRequest
{
    public CanvasBackgroundActionApplicabilityRequest(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        Document = document;
        ActiveScopeId = activeScopeId;
    }

    public DocumentSnapshot Document { get; }

    public DocumentScopeId ActiveScopeId { get; }
}
