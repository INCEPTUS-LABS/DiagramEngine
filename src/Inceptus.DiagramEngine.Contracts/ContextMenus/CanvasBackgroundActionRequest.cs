using Inceptus.DiagramEngine.Contracts.Creation;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.ContextMenus;

/// <summary>
/// Exact model context captured before creating an empty-Canvas action plan.
/// </summary>
public sealed record CanvasBackgroundActionRequest
{
    public CanvasBackgroundActionRequest(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        IDocumentCreationIdentityProvider identityProvider)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        ArgumentNullException.ThrowIfNull(identityProvider);
        Document = document;
        ActiveScopeId = activeScopeId;
        IdentityProvider = identityProvider;
    }

    public DocumentSnapshot Document { get; }

    public DocumentScopeId ActiveScopeId { get; }

    public IDocumentCreationIdentityProvider IdentityProvider { get; }
}
