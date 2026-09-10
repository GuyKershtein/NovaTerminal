using NovaTerminal.Core;
using NovaTerminal.Rendering;

namespace NovaTerminal.Integration.Tests;

public sealed class CellMetricsTests
{
    private static readonly CellMetrics Metrics = new(width: 8, height: 16, baseline: 12);

    [Fact]
    public void MeasureViewport_CountsOnlyWholeCells()
    {
        // 803 pixels holds 100 whole 8-pixel columns with 3 pixels left over; a 101st column would
        // be clipped, and the shell would format output for space that cannot be painted.
        var size = Metrics.MeasureViewport(803, 399);

        Assert.Equal(100, size.Columns);
        Assert.Equal(24, size.Rows);
    }

    [Fact]
    public void MeasureViewport_SurvivesAZeroSizedWindow()
    {
        // Windows report a zero viewport while being created or minimised. Layout must not throw.
        var size = Metrics.MeasureViewport(0, 0);

        Assert.Equal(TerminalSize.MinColumns, size.Columns);
        Assert.Equal(TerminalSize.MinRows, size.Rows);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MeasureViewport_SurvivesNonFiniteMeasurements(double dimension)
    {
        // An unconstrained measure pass legitimately passes infinity.
        var size = Metrics.MeasureViewport(dimension, dimension);

        Assert.InRange(size.Columns, TerminalSize.MinColumns, TerminalSize.MaxColumns);
        Assert.InRange(size.Rows, TerminalSize.MinRows, TerminalSize.MaxRows);
    }

    [Fact]
    public void GetCellOrigin_IsPlainMultiplication()
    {
        var (x, y) = Metrics.GetCellOrigin(column: 10, row: 3);

        Assert.Equal(80, x);
        Assert.Equal(48, y);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(7.9, 15.9, 0, 0)]
    [InlineData(8, 16, 1, 1)]
    [InlineData(1_000, 1_000, 79, 23)]
    [InlineData(-5, -5, 0, 0)]
    public void HitTest_MapsPixelsToCellsAndClampsToTheScreen(
        double x, double y, int expectedColumn, int expectedRow)
    {
        var (column, row) = Metrics.HitTest(x, y, TerminalSize.Default);

        Assert.Equal(expectedColumn, column);
        Assert.Equal(expectedRow, row);
    }

    [Fact]
    public void HitTest_RoundTripsWithGetCellOrigin()
    {
        var (x, y) = Metrics.GetCellOrigin(42, 17);

        Assert.Equal((42, 17), Metrics.HitTest(x, y, new TerminalSize(120, 40)));
    }

    [Theory]
    [InlineData(0, 16, 12)]
    [InlineData(-1, 16, 12)]
    [InlineData(8, 0, 0)]
    [InlineData(double.NaN, 16, 12)]
    [InlineData(double.PositiveInfinity, 16, 12)]
    [InlineData(8, 16, 20)]
    [InlineData(8, 16, -1)]
    public void Constructor_RejectsGeometryThatCannotDescribeAFont(double width, double height, double baseline)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellMetrics(width, height, baseline));
    }
}
