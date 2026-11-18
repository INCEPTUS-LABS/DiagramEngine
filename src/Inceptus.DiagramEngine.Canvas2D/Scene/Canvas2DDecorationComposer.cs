using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.Interaction;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Canvas2D.Scene;

internal sealed record Canvas2DDecorationComposition(
    Canvas2DScene Scene, bool Succeeded, ImmutableArray<Diagnostic> Diagnostics);

internal static class Canvas2DDecorationComposer
{
    internal static Canvas2DDecorationComposition Compose(Canvas2DScene nativeScene,
        DocumentSnapshot document, Guid attachmentId, long baseGeneration, long inputRevision,
        Canvas2DDecorationInput input, Canvas2DDecorationResources resources)
    {
        var diagnostics = new List<Diagnostic>();
#pragma warning disable CA1031 // Application callbacks are an untrusted extension boundary.
        try
        {
            if (nativeScene.DocumentId != document.DocumentId || nativeScene.SourceRevision != document.Revision)
                return Failure("The native decoration context is no longer coherent.");

            var parts = CreateParts(nativeScene, document);
            var context = new Canvas2DDecorationContext(attachmentId, nativeScene.DocumentId,
                nativeScene.SourceRevision, nativeScene.PresentationContext, baseGeneration, inputRevision,
                nativeScene.Items, parts, resources.DefaultFontFamily);
            var content = input.Provider.Compose(context, input.Values);
            if (content is null) return Failure("The decoration provider returned no content.");
            diagnostics.AddRange(content.Diagnostics);
            var nativeIds = nativeScene.Items.Select(static item => item.Id).ToHashSet();
            var partsById = parts.ToDictionary(static part => part.Item.Id);
            foreach (var item in content.Items)
            {
                if (!nativeIds.Add(item.Id)) return Failure("A decoration identity is duplicated or collides with native content.", item.Id.Value);
                if (!ValidateContract(item, input.ProviderId, partsById))
                    return Failure("A decoration must be passive, namespaced, and traced to one exact visible native part.", item.Id.Value);
                Canvas2DSceneBuilder.ValidateDecorationPrimitive(item, diagnostics);
                if (!HasResources(item, resources))
                    return Failure("A decoration references an image or font resource that is not configured.", item.Id.Value);
                if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    return new(nativeScene, false, Bound(diagnostics));
            }

            if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                return new(nativeScene, false, Bound(diagnostics));
            if (content.Items.IsEmpty) return new(nativeScene, true, Bound(diagnostics));

            // Every native feedback item remains above the passive lane, including connector
            // selection feedback whose native layer is Connector rather than Overlay.
            var nativeItems = NativeRenderItems(nativeScene);
            var guideIds = nativeScene.BoundaryGuides.Items.Select(static item => item.Id).ToHashSet();
            bool Feedback(Canvas2DSceneItem item) => !guideIds.Contains(item.Id) && IsFeedback(item);
            var items = nativeItems.Where(item => !Feedback(item))
                .Concat(content.Items.OrderBy(static item => item.ZIndex)
                    .ThenBy(static item => item.Id.Value, StringComparer.Ordinal))
                .Concat(nativeItems.Where(Feedback)).ToImmutableArray();
            return new(nativeScene.WithDecorations(items, Canvas2DBoundaryGuidePresentation.Empty), true, Bound(diagnostics));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            return Failure("The decoration provider failed or returned invalid output.");
        }
#pragma warning restore CA1031

        Canvas2DDecorationComposition Failure(string message, string? sourceIdentity = null)
        {
            diagnostics.Insert(0, new Diagnostic("CANVAS2D_DECORATION_FAILED", DiagnosticSeverity.Error, message, sourceIdentity));
            return new(nativeScene, false, Bound(diagnostics));
        }
    }

    private static ImmutableArray<Diagnostic> Bound(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.OrderByDescending(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(Canvas2DDecorationContent.MaximumDiagnostics).ToImmutableArray();

    private static bool IsFeedback(Canvas2DSceneItem item) => item.Layer == Canvas2DSceneLayer.Overlay ||
        (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0;

    private static ImmutableArray<Canvas2DSceneItem> NativeRenderItems(Canvas2DScene scene)
    {
        if (scene.BoundaryGuides.Items.IsEmpty) return scene.Items;
        // Full-frame uploads bake the two native guides into their original insertion positions.
        // Retaining their old content indices after adding/reordering primitives would be stale.
        var result = ImmutableArray.CreateBuilder<Canvas2DSceneItem>();
        for (var index = 0; index <= scene.Items.Length; index++)
        {
            foreach (var guide in scene.BoundaryGuides.Items)
            {
                var before = guide.Origin.StableSourceKey == "document-boundary:x-axis"
                    ? scene.BoundaryGuides.HorizontalContentIndex : scene.BoundaryGuides.VerticalContentIndex;
                if (before == index) result.Add(guide);
            }
            if (index < scene.Items.Length) result.Add(scene.Items[index]);
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<Canvas2DDecorationPart> CreateParts(Canvas2DScene scene, DocumentSnapshot document)
    {
        var parts = ImmutableArray.CreateBuilder<Canvas2DDecorationPart>();
        foreach (var item in scene.Items)
        {
            if (!item.IsVisible || IsFeedback(item) || item.Origin.SemanticElementId is not { } owner ||
                item.Clip is { } clip && (clip.IsEmpty || !Intersects(item.Bounds, clip))) continue;
            var element = document.SemanticModel.TryGetElement(owner, out _);
            var relationship = document.SemanticModel.TryGetRelationship(owner, out _);
            if (element == relationship) continue;
            var kind = Canvas2DDecorationPartKind.Other;
            if (item.Layer == Canvas2DSceneLayer.Label)
                kind = element ? Canvas2DDecorationPartKind.NodeLabel : Canvas2DDecorationPartKind.ConnectorLabel;
            else if (item.Origin.VisualStateId is null && Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item))
                kind = Canvas2DDecorationPartKind.SemanticRegion;
            else if (element && Canvas2DNodeBodyMetadata.IsNodeBody(item))
                kind = Canvas2DDecorationPartKind.NodeBody;
            else if (relationship && item.Origin.ProjectedObjectId is { } projected &&
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(projected, "connector"))
                kind = Canvas2DDecorationPartKind.ConnectorPath;
            parts.Add(new(owner, element ? Canvas2DDecorationOwnerKind.Element : Canvas2DDecorationOwnerKind.Relationship,
                kind, item));
        }
        return parts.ToImmutable();
    }

    private static bool Intersects(Inceptus.DiagramEngine.Contracts.Geometry.RectD left,
        Inceptus.DiagramEngine.Contracts.Geometry.RectD right) =>
        left.Left <= right.Right && right.Left <= left.Right && left.Top <= right.Bottom && right.Top <= left.Bottom;

    private static bool ValidateContract(Canvas2DSceneItem item, Canvas2DSceneContributorId providerId,
        Dictionary<SceneObjectId, Canvas2DDecorationPart> parts)
    {
        var origin = item.Origin;
        if (item.Layer != Canvas2DSceneLayer.Decoration || !item.HitTestPolicy.Equals(Canvas2DHitTestPolicy.None) ||
            item.PersistentAppearance.Count != 0 || item.Metadata.Count != 0 ||
            item.SpatialRegion is not null || item.ProcessPlacement is not null ||
            item.ConnectorPresentationMapping is not null || item.DerivedRegionMapping is not null ||
            (origin.Categories & Canvas2DSceneOriginCategory.RegisteredExtension) == 0 ||
            (origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 ||
            origin.StableSourceKey is null ||
            item.Id != Canvas2DSceneObjectIdentity.ForExtension(providerId, origin.StableSourceKey) ||
            origin.RelatedSceneObjectIds.Length != 1 ||
            !parts.TryGetValue(origin.RelatedSceneObjectIds[0], out var source)) return false;
        var nativeOrigin = source.Item.Origin;
        // Native derived parts, including line-jump masks, legitimately carry Configuration.
        // Preserve that provenance exactly; an application cannot invent additional categories.
        return origin.Categories == (nativeOrigin.Categories | Canvas2DSceneOriginCategory.RegisteredExtension) &&
            origin.SemanticElementId == nativeOrigin.SemanticElementId &&
            origin.VisualStateId == nativeOrigin.VisualStateId && origin.ProjectedObjectId == nativeOrigin.ProjectedObjectId &&
            origin.RelatedProjectedObjectIds.AsSpan().SequenceEqual(nativeOrigin.RelatedProjectedObjectIds.AsSpan());
    }

    private static bool HasResources(Canvas2DSceneItem item, Canvas2DDecorationResources resources)
    {
        if (item.Geometry.Kind == Canvas2DSceneGeometryKind.Image)
            return item.Geometry.Content is { } reference && resources.ImageReferences.Contains(reference);
        if (item.Geometry.Kind != Canvas2DSceneGeometryKind.Text) return true;
        var family = item.Style.FontFamily ?? resources.DefaultFontFamily;
        return family is not null && resources.FontFamilies.Contains(family);
    }
}
