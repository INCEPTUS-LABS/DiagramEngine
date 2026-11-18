using System.Collections.Immutable;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Contracts.Geometry;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed class ApplicationToolbarLayoutTests
{
    [Fact]
    public void ToolbarUsesFourIconPrefixWithReservedOverflowAndConstantCssSizing()
    {
        Assert.True(ApplicationToolbarLayout.TryPlace(new(220, 200, 100, 60), 700, 500,
            5, [], [], 0, out var placement));
        Assert.Equal(4, placement!.VisibleActionCount);
        Assert.True(placement.Overflow);
        Assert.Equal(244, placement.Width);
        Assert.Equal(140, placement.Position.Y); // 200 - 52 - 8.
        Assert.Equal(8, placement.Anchor.Top - placement.Position.Y - ApplicationToolbarLayout.Height);
        Assert.InRange(placement.Position.X, 8, 700 - 244 - 8);
    }

    [Fact]
    public void ToolbarFallbackKeepsEveryActionInOverflowInsteadOfRejectingDescriptors()
    {
        Assert.True(ApplicationToolbarLayout.TryPlace(new(30, 130, 20, 40), 80, 300,
            6, [], [], 0, out var placement));
        Assert.Equal(0, placement!.VisibleActionCount);
        Assert.True(placement.Overflow);
        Assert.Equal(52, placement.Width);
    }

    [Theory]
    [InlineData(-200, 40, 100, 60)]
    [InlineData(600, 40, 100, 60)]
    [InlineData(40, -100, 100, 60)]
    [InlineData(40, 500, 100, 60)]
    public void WhollyOffscreenAnchorsNeverBecomeDetachedEdgeBars(double x, double y, double width, double height)
    {
        Assert.False(ApplicationToolbarLayout.TryPlace(new(x, y, width, height), 500, 400, 1, [], [], 0, out _));
    }

    [Fact]
    public void VisibleAnchorIsClippedButClampingRetainsAnAssociatedEdge()
    {
        Assert.True(ApplicationToolbarLayout.TryPlace(new(-20, 100, 60, 40), 400, 300, 1, [], [], 0, out var placement));
        Assert.Equal(new RectD(0, 100, 40, 40), placement!.Anchor);
        var bar = new RectD(placement.Position.X, placement.Position.Y, placement.Width, 52);
        Assert.False(bar.Intersects(placement.Anchor));
        Assert.True(bar.Bottom <= 92 && bar.Left < 40 || bar.Left >= 48 && bar.Top < 140);
    }

    [Fact]
    public void HardLabelObstaclesAreAvoidedAndPassiveDecorationsAreSecondary()
    {
        var anchor = new RectD(220, 180, 80, 50);
        var above = new RectD(200, 100, 120, 70);
        Assert.True(ApplicationToolbarLayout.TryPlace(anchor, 600, 400, 1, [above], [], 0, out var labelAvoidance));
        Assert.Equal(308, labelAvoidance!.Position.X);
        Assert.True(ApplicationToolbarLayout.TryPlace(anchor, 600, 400, 1, [], [above], 0, out var decorationAvoidance));
        Assert.Equal(labelAvoidance.Position, decorationAvoidance!.Position);
        Assert.True(ApplicationToolbarLayout.TryPlace(anchor, 600, 400, 1, [], [new(0, 0, 600, 400)], 0, out var allDecorated));
        Assert.Equal(120, allDecorated!.Position.Y); // Decorations do not hide otherwise valid controls.
    }

    [Fact]
    public void ImpossibleGapOrSurfaceSuspendsInsteadOfOverlappingTarget()
    {
        Assert.False(ApplicationToolbarLayout.TryPlace(new(8, 8, 84, 84), 100, 100, 1, [], [], 0, out _));
        Assert.False(ApplicationToolbarLayout.TryPlace(new(20, 20, 10, 10), 60, 60, 1, [], [], 0, out _));
    }

    [Fact]
    public void NativeGroupConsumesTheSameShellBudgetWithoutChangingActionOrder()
    {
        Assert.True(ApplicationToolbarLayout.TryPlace(new(120, 200, 80, 40), 320, 500,
            5, [], [], 140, out var placement));
        Assert.Equal(2, placement!.VisibleActionCount);
        Assert.Equal(296, placement.Width);
        Assert.True(placement.Overflow);
    }

    [Fact]
    public void RelationshipAnchorUsesPolylineArcLengthRatherThanBoundsCenter()
    {
        ImmutableArray<PointD> route = [new(0, 0), new(100, 0), new(100, 300)];
        Assert.Equal(new PointD(100, 100), ApplicationToolbarLayout.ArcMidpoint(route));
        Assert.Equal(new PointD(10, 20), ApplicationToolbarLayout.ArcMidpoint([new(10, 20), new(10, 20)]));
        Assert.Equal(new PointD(50, 0), ApplicationToolbarLayout.ArcMidpoint([new(0, 0), new(100, 0)]));
        Assert.Equal(new PointD(0, 50), ApplicationToolbarLayout.ArcMidpoint([new(0, 0), new(0, 100)]));
    }
}
