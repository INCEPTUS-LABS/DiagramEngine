using Inceptus.DiagramEngine.Contracts.Creation;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Application-boundary identity generation for persistent creation. The provider is stateless;
/// undo, redo, and later creation attempts cannot rewind or reuse an in-memory sequence.
/// </summary>
internal sealed class GuidDocumentCreationIdentityProvider : IDocumentCreationIdentityProvider
{
    internal static GuidDocumentCreationIdentityProvider Instance { get; } = new();

    private GuidDocumentCreationIdentityProvider()
    {
    }

    public DocumentCreationIdentity CreateIdentity() => new(
        new SemanticElementId($"element:{Guid.NewGuid():N}"),
        new VisualStateId($"visual:{Guid.NewGuid():N}"));

    public ConnectorAnchorId CreateConnectorAnchorId() =>
        new($"connector-anchor:{Guid.NewGuid():N}");

    public DocumentScopeId CreateDocumentScopeId() =>
        new($"scope:{Guid.NewGuid():N}");
}
