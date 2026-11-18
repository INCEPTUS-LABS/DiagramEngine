using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Geometry;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed record ApplicationToolbarPlacement(RectD Anchor, PointD Position, double Width,
    int VisibleActionCount, bool Overflow, double SurfaceWidth, double SurfaceHeight)
{
    internal ElementToolboxState? NativeToolbox { get; init; }
    internal double NativeWidth { get; init; }
}

internal sealed record DocumentCanvasApplicationToolbarState(BpmnApplicationActionContext Context,
    ImmutableArray<BpmnApplicationAction> Actions, bool IsDiscoveryPending, ApplicationToolbarPlacement Placement);

/// <summary>Internal CSS layout only; the semantic target and final native geometry remain authoritative.</summary>
internal static class ApplicationToolbarLayout
{
    internal const double Gap = 8;
    internal const double Height = 52;

    internal static bool TryPlace(RectD originalAnchor, double surfaceWidth, double surfaceHeight,
        int actionCount, IReadOnlyList<RectD> obstacles, IReadOnlyList<RectD> decorations,
        double nativeWidth, out ApplicationToolbarPlacement? placement)
    {
        placement = null;
        var viewport = new RectD(0, 0, surfaceWidth, surfaceHeight);
        if (originalAnchor.Intersection(viewport) is not { } anchor || surfaceHeight < Height + 16) return false;
        var budget = Math.Min(480, surfaceWidth - 16);
        var native = nativeWidth > 0 ? nativeWidth + 8 : 0;
        var count = 0;
        var contentWidth = 0d;
        for (var index = 0; index < Math.Min(4, actionCount); index++)
        {
            var next = contentWidth + (index == 0 ? 0 : 4) + 44;
            var reserve = index + 1 < actionCount ? 48 : 0;
            if (8 + native + next + reserve > budget) break;
            contentWidth = next;
            count++;
        }
        var overflow = count < actionCount;
        var width = 8 + native + contentWidth + (overflow ? count > 0 ? 48 : 44 : 0);
        // Pending discovery and feasibility probes use one minimum control footprint.
        if (actionCount == 0) width = 52 + native;
        if (width <= budget && TryCandidates(width, out var point))
        {
            placement = new(anchor, point, width, count, overflow, surfaceWidth, surfaceHeight);
            return true;
        }
        width = 52 + native;
        if (width <= budget && TryCandidates(width, out point))
        {
            placement = new(anchor, point, width, 0, actionCount > 0, surfaceWidth, surfaceHeight);
            return true;
        }
        return false;

        bool TryCandidates(double candidateWidth, out PointD position)
        {
            position = default;
            var candidates = new[]
            {
                new PointD((anchor.Left + anchor.Right - candidateWidth) / 2, anchor.Top - Height - Gap),
                new PointD(anchor.Right + Gap, (anchor.Top + anchor.Bottom - Height) / 2),
                new PointD(anchor.Left - candidateWidth - Gap, (anchor.Top + anchor.Bottom - Height) / 2),
                new PointD((anchor.Left + anchor.Right - candidateWidth) / 2, anchor.Bottom + Gap),
            };
            PointD? firstValid = null;
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                var bounded = new PointD(Math.Clamp(candidate.X, 8, surfaceWidth - candidateWidth - 8),
                    Math.Clamp(candidate.Y, 8, surfaceHeight - Height - 8));
                var bounds = new RectD(bounded.X, bounded.Y, candidateWidth, Height);
                var associated = index switch
                {
                    0 => bounds.Bottom <= anchor.Top - Gap && bounds.Right > anchor.Left && bounds.Left < anchor.Right,
                    1 => bounds.Left >= anchor.Right + Gap && bounds.Bottom > anchor.Top && bounds.Top < anchor.Bottom,
                    2 => bounds.Right <= anchor.Left - Gap && bounds.Bottom > anchor.Top && bounds.Top < anchor.Bottom,
                    _ => bounds.Top >= anchor.Bottom + Gap && bounds.Right > anchor.Left && bounds.Left < anchor.Right,
                };
                if (!associated || obstacles.Any(bounds.Intersects)) continue;
                firstValid ??= bounded;
                if (decorations.Any(bounds.Intersects)) continue;
                position = bounded;
                return true;
            }
            if (firstValid is not { } fallback) return false;
            position = fallback;
            return true;
        }
    }

    internal static PointD ArcMidpoint(ImmutableArray<PointD> points)
    {
        if (points.IsEmpty) throw new ArgumentException("A route needs a point.", nameof(points));
        var distance = 0d;
        for (var index = 1; index < points.Length; index++) distance += Length(points[index - 1], points[index]);
        var remaining = distance / 2;
        for (var index = 1; index < points.Length; index++)
        {
            var length = Length(points[index - 1], points[index]);
            if (length > 0 && remaining <= length)
            {
                var fraction = remaining / length;
                return new(points[index - 1].X + (points[index].X - points[index - 1].X) * fraction,
                    points[index - 1].Y + (points[index].Y - points[index - 1].Y) * fraction);
            }
            remaining -= length;
        }
        return points[0];
    }

    private static double Length(PointD a, PointD b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}

internal sealed partial class DocumentCanvasHost
{
    private bool TryResolveApplicationToolbarPlacementUnderLock(BpmnSemanticSelectionTarget target,
        BpmnPresentationPart? part, ImmutableArray<BpmnApplicationAction> actions,
        out ApplicationToolbarPlacement? placement, out bool ambiguous)
    {
        placement = null;
        ambiguous = false;
        if (_surfaceSize is not { } surface || _session?.CaptureState() is not
            { IsCurrentScenePresented: true, CurrentScene: { } scene } state) return false;
        var owned = scene.Items.Where(item => item.IsVisible && target.Parts.Any(p => p.SceneObjectId == item.Id)).ToArray();
        var hit = part is null ? null : owned.SingleOrDefault(item => item.Id == part.SceneObjectId);
        if (part is not null && hit is null) return false;
        if (hit is not null)
            owned = owned.Where(item => item.SpatialRegion?.Id == hit.SpatialRegion?.Id &&
                item.ProcessPlacement?.ScopeId == hit.ProcessPlacement?.ScopeId &&
                item.ProcessPlacement?.BoundaryVisualStateId == hit.ProcessPlacement?.BoundaryVisualStateId).ToArray();
        else if (!BpmnPresentationSelection.HasSinglePresentationInstance(owned)) { ambiguous = true; return false; }
        Canvas2DSceneItem[] anchors;
        if (target.Kind == BpmnSemanticTargetKind.Relationship)
            anchors = owned.Where(item => item.Origin.ProjectedObjectId is { } projected &&
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(projected, "connector") &&
                target.Parts.Any(p => p.SceneObjectId == item.Id && p.Kind == BpmnPresentationPartKind.ConnectorPath)).ToArray();
        else
        {
            var headers = owned.Where(item => StringComparer.Ordinal.Equals(item.Origin.StableSourceKey,
                target.SemanticId.Value + ":pool-header")).ToArray();
            anchors = headers.Length > 0 ? headers : owned.Where(item => item.Layer == Canvas2DSceneLayer.Content &&
                target.Parts.Any(p => p.SceneObjectId == item.Id && p.Kind is BpmnPresentationPartKind.NodeBody or BpmnPresentationPartKind.SemanticRegion)).ToArray();
        }
        if (anchors.Length != 1) { ambiguous = true; return false; }
        var item = anchors[0];
        RectD anchor;
        if (target.Kind == BpmnSemanticTargetKind.Relationship)
        {
            // This is the final displayed polyline, including native presentation transforms
            // and sampled crossing arcs. Internal routed/editable centerlines are not UI anchors.
            var route = item.Geometry.Points;
            if (route.IsEmpty) return false;
            var points = route.Select(item.Transform.TransformPoint).ToImmutableArray();
            var middle = ApplicationToolbarLayout.ArcMidpoint(points);
            if (item.Clip is { } clip && !clip.Contains(middle)) return false;
            var css = scene.ViewportTransform.TransformPoint(middle);
            anchor = new(css.X - 4, css.Y - 4, 8, 8);
        }
        else
        {
            var bounds = target.Parts.Single(p => p.SceneObjectId == item.Id).Bounds;
            anchor = ElementToolboxScreenBounds(scene, bounds);
        }
        var labels = scene.Items.Where(candidate => candidate.IsVisible && candidate.Origin.SemanticElementId == target.SemanticId &&
            candidate.Origin.VisualStateId == target.VisualStateId && candidate.Layer == Canvas2DSceneLayer.Label)
            .Select(candidate => ElementToolboxScreenBounds(scene, candidate.Bounds)).ToList();
        var native = CaptureElementToolbox(state);
        if (native is not null && native.VisualId != target.VisualStateId) native = null;
        if (native is { Candidates.IsEmpty: false })
            labels.Add(new(native.CreationPosition.X, native.CreationPosition.Y, native.CreationWidth, 158));
        var decorations = scene.Items.Where(static candidate => candidate.IsVisible && candidate.Layer == Canvas2DSceneLayer.Decoration)
            .Select(candidate => ElementToolboxScreenBounds(scene, candidate.Bounds)).ToArray();
        var nativeCount = native is null ? 0 : (native.CanProperties ? 1 : 0) + (native.OpenSubProcessScopeId is null ? 0 : 1) +
            (native.IsConnector ? 0 : 1) + 1;
        var nativeWidth = nativeCount > 0 ? nativeCount * 44 + (nativeCount - 1) * 4 : 0;
        if (!ApplicationToolbarLayout.TryPlace(anchor, surface.CssWidth, surface.CssHeight, actions.Length, labels,
                decorations, nativeWidth, out placement)) return false;
        placement = placement! with { NativeToolbox = native, NativeWidth = nativeWidth };
        return true;
    }
}
