using HomeApp.Core;

namespace HomeApp.Core.Tests;

public sealed class LayoutMathTests
{
    [Theory]
    [InlineData(23, 16, 16)]
    [InlineData(24, 16, 32)]
    [InlineData(40, 16, 48)]
    public void Snap_UsesNearestGridLine(double value, double gridSize, double expected)
    {
        Assert.Equal(expected, LayoutMath.Snap(value, true, gridSize));
        Assert.Equal(value, LayoutMath.Snap(value, false, gridSize));
    }

    [Fact]
    public void Normalize_ClampsInvalidGeometry()
    {
        var widget = new WidgetState
        {
            X = double.NaN,
            Y = -50,
            Width = 10,
            Height = double.PositiveInfinity
        };

        LayoutMath.Normalize(widget);

        Assert.Equal(0, widget.X);
        Assert.Equal(0, widget.Y);
        Assert.Equal(LayoutMath.MinimumWidth, widget.Width);
        Assert.Equal(300, widget.Height);
    }

    [Fact]
    public void Extent_PreservesViewportAndExpandsForDistantWidgets()
    {
        var widgets = new[]
        {
            new WidgetState { X = 1_000, Y = 700, Width = 400, Height = 300 }
        };

        var extent = LayoutMath.Extent(widgets, 800, 600, 1);

        Assert.Equal(1_448, extent.Width);
        Assert.Equal(1_048, extent.Height);
    }

    [Fact]
    public void Arrange_WrapsWithoutChangingWidgetSizes()
    {
        var widgets = new List<WidgetState>
        {
            new() { Width = 300, Height = 200 },
            new() { Width = 300, Height = 280 },
            new() { Width = 300, Height = 220 }
        };

        LayoutMath.Arrange(widgets, 650);

        Assert.Equal((16d, 16d), (widgets[0].X, widgets[0].Y));
        Assert.Equal((332d, 16d), (widgets[1].X, widgets[1].Y));
        Assert.Equal((16d, 312d), (widgets[2].X, widgets[2].Y));
        Assert.Equal(new[] { 300d, 300d, 300d }, widgets.Select(widget => widget.Width));
    }

    [Fact]
    public void InitialLayout_UsesMoreColumnsOnWideViewports()
    {
        var board = new BoardState
        {
            Widgets = Enumerable.Range(0, 4).Select(_ => SampleData.Create(WidgetKind.Note)).ToList()
        };

        LayoutMath.InitialLayout(board, 1_600);

        Assert.True(board.HasInitialLayout);
        Assert.Equal(4, board.Widgets.Select(widget => widget.X).Distinct().Count());
        Assert.All(board.Widgets, widget => Assert.InRange(widget.X + widget.Width, 0, 1_600));
    }
}
