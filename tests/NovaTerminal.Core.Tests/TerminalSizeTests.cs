using NovaTerminal.Core;

namespace NovaTerminal.Core.Tests;

public sealed class TerminalSizeTests
{
    [Fact]
    public void Constructor_StoresDimensions()
    {
        var size = new TerminalSize(120, 40);

        Assert.Equal(120, size.Columns);
        Assert.Equal(40, size.Rows);
        Assert.Equal(4800, size.CellCount);
    }

    [Fact]
    public void Default_IsTheClassicVt100Size()
    {
        Assert.Equal(new TerminalSize(80, 24), TerminalSize.Default);
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(80, 0)]
    [InlineData(-1, 24)]
    [InlineData(80, -1)]
    [InlineData(TerminalSize.MaxColumns + 1, 24)]
    [InlineData(80, TerminalSize.MaxRows + 1)]
    public void Constructor_RejectsOutOfRangeDimensions(int columns, int rows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalSize(columns, rows));
    }

    [Theory]
    [InlineData(0, 0, TerminalSize.MinColumns, TerminalSize.MinRows)]
    [InlineData(-5, -5, TerminalSize.MinColumns, TerminalSize.MinRows)]
    [InlineData(100, 30, 100, 30)]
    [InlineData(int.MaxValue, int.MaxValue, TerminalSize.MaxColumns, TerminalSize.MaxRows)]
    public void Clamp_BringsAnyInputIntoRange(int columns, int rows, int expectedColumns, int expectedRows)
    {
        var size = TerminalSize.Clamp(columns, rows);

        Assert.Equal(expectedColumns, size.Columns);
        Assert.Equal(expectedRows, size.Rows);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(79, 23, true)]
    [InlineData(80, 23, false)]
    [InlineData(79, 24, false)]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    public void Contains_TestsCellCoordinates(int column, int row, bool expected)
    {
        Assert.Equal(expected, TerminalSize.Default.Contains(column, row));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(new TerminalSize(80, 24), new TerminalSize(80, 24));
        Assert.NotEqual(new TerminalSize(80, 24), new TerminalSize(24, 80));
    }

    [Fact]
    public void Deconstruct_YieldsColumnsThenRows()
    {
        var (columns, rows) = new TerminalSize(132, 50);

        Assert.Equal(132, columns);
        Assert.Equal(50, rows);
    }

    [Fact]
    public void ToString_IsCompactAndReadable()
    {
        Assert.Equal("132x50", new TerminalSize(132, 50).ToString());
    }
}
