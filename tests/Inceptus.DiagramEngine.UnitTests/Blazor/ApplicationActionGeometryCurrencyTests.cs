using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Presentation;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed class ApplicationActionGeometryCurrencyTests
{
    [Fact]
    public void PopupGeometryCurrencyRejectsDifferentRoutesWithTheSameBoundingBox()
    {
        var original = Part(Canvas2DSceneGeometry.Path([new(0, 0), new(100, 0), new(100, 100)]));
        var rerouted = Part(Canvas2DSceneGeometry.Path([new(0, 0), new(0, 100), new(100, 100)]));
        Assert.Equal(original.Bounds, rerouted.Bounds);
        Assert.Equal(original.Id, rerouted.Id);
        Assert.False(DocumentCanvasHost.ApplicationPartGeometryEquals(original, rerouted));
    }

    [Fact]
    public void PopupGeometryCurrencyPreservesEquivalentNativeGeometryAcrossFeedbackStyleChanges()
    {
        var original = Part(Canvas2DSceneGeometry.Path([new(0, 0), new(100, 0), new(100, 100)]));
        var hovered = Part(Canvas2DSceneGeometry.Path([new(0, 0), new(100, 0), new(100, 100)]),
            style: new(stroke: "#00ff00", strokeWidth: 3));
        Assert.NotEqual(original.Style, hovered.Style);
        Assert.True(DocumentCanvasHost.ApplicationPartGeometryEquals(original, hovered));
    }

    [Theory]
    [InlineData("clip")]
    [InlineData("placement")]
    [InlineData("transform")]
    [InlineData("visibility")]
    public void PopupGeometryCurrencyRetiresChangedPresentationAuthorityEvenWithStableIds(string changed)
    {
        var geometry = Canvas2DSceneGeometry.Rectangle(new(0, 0, 100, 80));
        var original = Part(geometry);
        var candidate = Part(geometry,
            clip: changed == "clip" ? new(0, 0, 90, 80) : null,
            placement: changed == "placement" ? new(new("nested-scope"), new(0, 0), new("boundary")) : null,
            transform: changed == "transform" ? new(-1, 0, 0, -1, 100, 80) : null,
            visible: changed != "visibility");
        Assert.Equal(original.Id, candidate.Id);
        Assert.Equal(original.Bounds, candidate.Bounds);
        Assert.False(DocumentCanvasHost.ApplicationPartGeometryEquals(original, candidate));
    }

    private static Canvas2DSceneItem Part(Canvas2DSceneGeometry geometry, Canvas2DSceneStyle? style = null,
        RectD? clip = null, PresentationScopePlacement? placement = null, Matrix2D? transform = null, bool visible = true) =>
        new(placement, new("test:popup-part"), Canvas2DSceneLayer.Connector, 0, geometry,
            new(Canvas2DSceneOriginCategory.SemanticElement | Canvas2DSceneOriginCategory.VisualState,
                new("test:relationship"), new("test:relationship-visual")),
            style: style, clip: clip, transform: transform, isVisible: visible);
}
