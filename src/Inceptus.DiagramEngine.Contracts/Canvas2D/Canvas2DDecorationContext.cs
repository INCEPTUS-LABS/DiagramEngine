using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Presentation;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.Canvas2D;

public enum Canvas2DDecorationOwnerKind { Element, Relationship }
public enum Canvas2DDecorationPartKind { NodeBody, NodeLabel, ConnectorPath, ConnectorLabel, SemanticRegion, Other }

/// <summary>
/// An exact visible native representation. Item geometry, bounds, transform and clip are in
/// their established final Scene coordinate conventions. Owner identity never comes from a label.
/// </summary>
public sealed record Canvas2DDecorationPart
{
    public Canvas2DDecorationPart(SemanticElementId ownerId, Canvas2DDecorationOwnerKind ownerKind,
        Canvas2DDecorationPartKind kind, Canvas2DSceneItem item)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(item);
        if (!Enum.IsDefined(ownerKind)) throw new ArgumentOutOfRangeException(nameof(ownerKind));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        OwnerId = ownerId;
        OwnerKind = ownerKind;
        Kind = kind;
        Item = item;
    }

    public SemanticElementId OwnerId { get; }
    public Canvas2DDecorationOwnerKind OwnerKind { get; }
    public Canvas2DDecorationPartKind Kind { get; }
    public Canvas2DSceneItem Item { get; }

    /// <summary>Traces a decoration to this exact native part without inventing native identities.</summary>
    public Canvas2DSceneOriginTrace CreateOrigin(string stableSourceKey) => new(
        Item.Origin.Categories | Canvas2DSceneOriginCategory.RegisteredExtension,
        Item.Origin.SemanticElementId, Item.Origin.VisualStateId, Item.Origin.ProjectedObjectId,
        stableSourceKey, Item.Origin.RelatedProjectedObjectIds, [Item.Id]);
}

/// <summary>
/// Borrowed immutable native presentation after final spatial/process placement. BaseItems never
/// contains previous decorations. Decorations may extend beyond a part's body; the caller owns
/// their document-coordinate clip. No engine-owned disposable object is exposed.
/// </summary>
public sealed class Canvas2DDecorationContext
{
    public Canvas2DDecorationContext(Guid attachmentId, DocumentId documentId,
        DocumentRevision sourceRevision, PresentationViewContext presentationContext,
        long baseSceneGeneration, long inputRevision, IEnumerable<Canvas2DSceneItem> baseItems,
        IEnumerable<Canvas2DDecorationPart> parts, string? defaultFontFamily)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(presentationContext);
        ArgumentNullException.ThrowIfNull(baseItems);
        ArgumentNullException.ThrowIfNull(parts);
        AttachmentId = attachmentId;
        DocumentId = documentId;
        SourceRevision = sourceRevision;
        PresentationContext = presentationContext;
        BaseSceneGeneration = baseSceneGeneration;
        InputRevision = inputRevision;
        BaseItems = baseItems.ToImmutableArray();
        Parts = parts.ToImmutableArray();
        DefaultFontFamily = defaultFontFamily;
    }

    public Guid AttachmentId { get; }
    public DocumentId DocumentId { get; }
    public DocumentRevision SourceRevision { get; }
    public PresentationViewContext PresentationContext { get; }
    public long BaseSceneGeneration { get; }
    public long InputRevision { get; }
    public ImmutableArray<Canvas2DSceneItem> BaseItems { get; }
    public ImmutableArray<Canvas2DDecorationPart> Parts { get; }
    public string? DefaultFontFamily { get; }

    /// <summary>
    /// Returns every exact matching part. An empty or multiple result must be handled explicitly;
    /// this method never chooses an arbitrary representation. Filter by Item.Id or visual origin
    /// when the application has an explicit authority for a particular displayed representation.
    /// </summary>
    public ImmutableArray<Canvas2DDecorationPart> FindParts(SemanticElementId ownerId,
        Canvas2DDecorationOwnerKind ownerKind, Canvas2DDecorationPartKind partKind)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        return Parts.Where(part => part.OwnerId == ownerId && part.OwnerKind == ownerKind &&
            part.Kind == partKind).ToImmutableArray();
    }
}
