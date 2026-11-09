using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.Canvas2D;

/// <summary>
/// Assigns one canonical Process Visual State to one transient spatial region.
/// </summary>
public sealed record Canvas2DSpatialVisualPlacement
{
    public Canvas2DSpatialVisualPlacement(
        VisualStateId visualStateId,
        Canvas2DSpatialRegionId regionId,
        bool isVisible = true)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(regionId);
        VisualStateId = visualStateId;
        RegionId = regionId;
        IsVisible = isVisible;
    }

    public VisualStateId VisualStateId { get; }

    public Canvas2DSpatialRegionId RegionId { get; }

    public bool IsVisible { get; }
}
