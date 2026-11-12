using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal static class BpmnPresentationSelection
{
    internal static bool HasSinglePresentationInstance(IEnumerable<Canvas2DSceneItem> parts) =>
        parts.Select(static item => (item.SpatialRegion?.Id, item.ProcessPlacement?.ScopeId,
            item.ProcessPlacement?.BoundaryVisualStateId)).Distinct().Take(2).Count() == 1;

    internal static ImmutableArray<BpmnSemanticSelectionTarget> Resolve(DocumentSnapshot document,
        EditingSessionState state, out ImmutableArray<Diagnostic> diagnostics)
    {
        var targets = ImmutableArray.CreateBuilder<BpmnSemanticSelectionTarget>();
        var errors = ImmutableArray.CreateBuilder<Diagnostic>();
        // This is a projection of the session's selection, never another selection authority.
        foreach (var visualId in state.EditorState.Selection)
        {
            if (document.VisualModel.TryGetVisualState(visualId, out var visual) && visual is not null)
                Add(visual.SemanticElementId, visualId);
            else Invalid();
        }
        if (state.EditorState.SemanticSceneSelection is { } semanticId) Add(semanticId, null);
        diagnostics = errors.ToImmutable();
        return targets.ToImmutable();

        void Invalid() => errors.Add(new("BPMN_PRESENTATION_TARGET_UNRESOLVED", DiagnosticSeverity.Warning,
            "A selected target has no unambiguous visible semantic presentation."));

        void Add(SemanticElementId id, VisualStateId? visualId)
        {
            var element = document.SemanticModel.TryGetElement(id, out _);
            var relationship = document.SemanticModel.TryGetRelationship(id, out _);
            if (element == relationship) { Invalid(); return; }
            var parts = state.CurrentScene!.Items
                .Where(item => item.Origin.SemanticElementId == id && item.Origin.VisualStateId == visualId)
                .Select(item => ResolvePart(document, item))
                .OfType<BpmnPresentationPart>().ToImmutableArray();
            if (parts.IsEmpty) { Invalid(); return; }
            targets.Add(new(id, element ? BpmnSemanticTargetKind.Element : BpmnSemanticTargetKind.Relationship,
                visualId, parts));
        }
    }

    internal static BpmnPresentationPart? ResolvePart(DocumentSnapshot document, Canvas2DSceneItem item)
    {
        if (!item.IsVisible || item.HitTestPolicy.Mode == Canvas2DHitTestMode.None ||
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 ||
            item.Origin.SemanticElementId is not { } id) return null;
        var isElement = document.SemanticModel.TryGetElement(id, out _);
        var isRelationship = document.SemanticModel.TryGetRelationship(id, out _);
        if (isElement == isRelationship) return null;
        if (item.Origin.VisualStateId is { } visualId &&
            (!document.VisualModel.TryGetVisualState(visualId, out var visual) || visual?.SemanticElementId != id)) return null;
        var bounds = VisibleBounds(item);
        if (bounds is null) return null;
        BpmnPresentationPartKind kind;
        if (item.Origin.VisualStateId is null)
        {
            if (!Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item)) return null;
            kind = BpmnPresentationPartKind.SemanticRegion;
        }
        else if (isElement && item.Layer == Canvas2DSceneLayer.Content) kind = BpmnPresentationPartKind.NodeBody;
        else if (item.Layer == Canvas2DSceneLayer.Label) kind = isElement ? BpmnPresentationPartKind.NodeLabel : BpmnPresentationPartKind.ConnectorLabel;
        else if (isRelationship && item.Layer == Canvas2DSceneLayer.Connector) kind = BpmnPresentationPartKind.ConnectorPath;
        else return null;
        return new(item.Id, kind, bounds.Value, item.SpatialRegion?.Id);
    }

    private static RectD? VisibleBounds(Canvas2DSceneItem item)
    {
        // Preserve exact geometry, including horizontal/vertical connector bounds.
        var bounds = item.Bounds;
        if (item.Clip is not { } clip) return bounds;
        var left = Math.Max(bounds.Left, clip.Left);
        var top = Math.Max(bounds.Top, clip.Top);
        var right = Math.Min(bounds.Right, clip.Right);
        var bottom = Math.Min(bounds.Bottom, clip.Bottom);
        return right < left || bottom < top ? null : new RectD(left, top, right - left, bottom - top);
    }
}
