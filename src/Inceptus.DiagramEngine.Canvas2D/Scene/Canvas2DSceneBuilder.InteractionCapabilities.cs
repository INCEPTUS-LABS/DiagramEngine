using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Projection;
using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private static void ApplyTransientInteractionCapabilities(
        ProjectedGraph graph, List<Canvas2DSceneItem> items)
    {
        var capabilities = new Dictionary<ProjectedObjectId, KeyValuePair<string, PropertyValue>>();
        foreach (var node in graph.Nodes)
        {
            if (HasCapability(node.ProjectedProperties,
                    Canvas2DTransientInteractionMetadata.BoundedSelectionEligible))
            {
                capabilities.Add(node.Id, Canvas2DTransientInteractionMetadata.BoundedSelectionEnabled);
            }
        }
        foreach (var label in graph.Labels)
        {
            if (HasCapability(label.ProjectedProperties, Canvas2DTransientInteractionMetadata.ExcludeFromHover))
            {
                capabilities.Add(label.Id, Canvas2DTransientInteractionMetadata.HoverExcluded);
            }
        }
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Origin.ProjectedObjectId is not { } id || !capabilities.TryGetValue(id, out var capability))
            {
                continue;
            }
            items[index] = new Canvas2DSceneItem(item.Id, item.Layer, item.ZIndex, item.Geometry,
                item.Origin, item.Transform, item.Clip, item.Style, item.IsVisible, item.HitTestPolicy,
                item.PersistentAppearance, item.Metadata.Append(capability), item.Bounds,
                item.SpatialRegion, item.ConnectorPresentationMapping);
        }
    }

    private static bool HasCapability(PropertyMap metadata, string key) =>
        metadata.TryGetValue(key, out var value) && value.Kind == PropertyValueKind.Boolean && value.BooleanValue;
}
