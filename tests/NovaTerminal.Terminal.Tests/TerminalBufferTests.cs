using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Terminal;

namespace NovaTerminal.Terminal.Tests;

public sealed class TerminalBufferTests
{
    private static readonly CellStyle OnBlue = CellStyle.Default
        .WithBackground(TerminalColor.FromAnsi(AnsiColor.Blue));

    [Fact]
    public void NewBuffer_IsBlankAndFullyDamaged()
    {
        var buffer = new TerminalBuffer(new TerminalSize(10, 4));

        Assert.Equal(new TerminalSize(10, 4), buffer.Size);
        Assert.Equal(string.Empty, buffer.GetLineText(0));

        // Nothing has been drawn yet, so every row needs painting.
        Assert.True(buffer.HasDamage);
        Assert.True(buffer.IsRowDirty(3));
    }

    [Fact]
    public void SetCell_WritesAndMarksOnlyThatRowDirty()
    {
        var buffer = Buffer(10, 4);
        buffer.ClearDamage();

        buffer.SetCell(2, 1, new TerminalCell(new Rune('X'), CellStyle.Default));

        Assert.Equal(new Rune('X'), buffer[2, 1].Character);
        Assert.True(buffer.IsRowDirty(1));
        Assert.False(buffer.IsRowDirty(0));
        Assert.False(buffer.IsRowDirty(2));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(10, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 4)]
    public void Coordinates_OutsideTheBufferAreRejected(int column, int row)
    {
        var buffer = Buffer(10, 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[column, row]);
    }

    [Fact]
    public void ClearDamage_ResetsEveryRow()
    {
        var buffer = Buffer(10, 4);

        buffer.ClearDamage();

        Assert.False(buffer.HasDamage);
        Assert.False(buffer.IsRowDirty(0));
    }

    [Fact]
    public void ScrollUp_MovesContentTowardTheTopAndBlanksTheBottom()
    {
        var buffer = Buffer(10, 4);
        WriteLines(buffer, "one", "two", "three", "four");

        buffer.ScrollUp(ScrollRegion.FullScreen(buffer.Size), 1, CellStyle.Default);

        Assert.Equal("two", buffer.GetLineText(0));
        Assert.Equal("three", buffer.GetLineText(1));
        Assert.Equal("four", buffer.GetLineText(2));
        Assert.Equal(string.Empty, buffer.GetLineText(3));
    }

    [Fact]
    public void ScrollDown_MovesContentTowardTheBottomAndBlanksTheTop()
    {
        var buffer = Buffer(10, 4);
        WriteLines(buffer, "one", "two", "three", "four");

        buffer.ScrollDown(ScrollRegion.FullScreen(buffer.Size), 1, CellStyle.Default);

        Assert.Equal(string.Empty, buffer.GetLineText(0));
        Assert.Equal("one", buffer.GetLineText(1));
        Assert.Equal("two", buffer.GetLineText(2));
        Assert.Equal("three", buffer.GetLineText(3));
    }

    [Fact]
    public void Scrolling_AffectsOnlyTheRegion()
    {
        var buffer = Buffer(10, 5);
        WriteLines(buffer, "keep", "a", "b", "c", "keep too");

        // A program pinning a header and a status line scrolls only what lies between them.
        buffer.ScrollUp(new ScrollRegion(1, 3), 1, CellStyle.Default);

        Assert.Equal("keep", buffer.GetLineText(0));
        Assert.Equal("b", buffer.GetLineText(1));
        Assert.Equal("c", buffer.GetLineText(2));
        Assert.Equal(string.Empty, buffer.GetLineText(3));
        Assert.Equal("keep too", buffer.GetLineText(4));
    }

    [Fact]
    public void ScrollingByTheRegionHeightOrMore_ClearsTheRegion()
    {
        var buffer = Buffer(10, 4);
        WriteLines(buffer, "one", "two", "three", "four");

        buffer.ScrollUp(ScrollRegion.FullScreen(buffer.Size), 99, CellStyle.Default);

        Assert.Equal(string.Empty, buffer.GetText().Replace("\n", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void ScrollingByZeroOrLess_DoesNothing()
    {
        var buffer = Buffer(10, 4);
        WriteLines(buffer, "one", "two", "three", "four");

        buffer.ScrollUp(ScrollRegion.FullScreen(buffer.Size), 0, CellStyle.Default);
        buffer.ScrollDown(ScrollRegion.FullScreen(buffer.Size), -5, CellStyle.Default);

        Assert.Equal("one", buffer.GetLineText(0));
        Assert.Equal("four", buffer.GetLineText(3));
    }

    [Fact]
    public void Scrolling_AppliesTheFillStyleToVacatedLines()
    {
        var buffer = Buffer(10, 3);
        WriteLines(buffer, "one", "two", "three");

        buffer.ScrollUp(ScrollRegion.FullScreen(buffer.Size), 1, OnBlue);

        // Erased space takes the current background: this is what keeps a coloured region coloured
        // as it scrolls.
        Assert.Equal(OnBlue.Background, buffer[0, 2].Background);
    }

    [Fact]
    public void Scrolling_RecyclesLinesRatherThanAllocatingThem()
    {
        var buffer = Buffer(10, 3);
        var originalTopLine = buffer.GetLine(0);

        buffer.ScrollUp(ScrollRegion.FullScreen(buffer.Size), 1, CellStyle.Default);

        // The line that scrolled off the top is cleared and reused at the bottom. Scrolling
        // allocating nothing is what stops a flood of output from becoming GC pressure.
        Assert.Same(originalTopLine, buffer.GetLine(2));
    }

    [Fact]
    public void Scrolling_MarksTheWholeRegionDirty()
    {
        var buffer = Buffer(10, 5);
        buffer.ClearDamage();

        buffer.ScrollUp(new ScrollRegion(1, 3), 1, CellStyle.Default);

        Assert.False(buffer.IsRowDirty(0));
        Assert.True(buffer.IsRowDirty(1));
        Assert.True(buffer.IsRowDirty(3));
        Assert.False(buffer.IsRowDirty(4));
    }

    [Fact]
    public void ScrollRegion_OutsideTheBufferIsRejected()
    {
        var buffer = Buffer(10, 4);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => buffer.ScrollUp(new ScrollRegion(0, 4), 1, CellStyle.Default));
    }

    [Fact]
    public void ClearRows_ErasesAnInclusiveSpan()
    {
        var buffer = Buffer(10, 4);
        WriteLines(buffer, "one", "two", "three", "four");

        buffer.ClearRows(1, 2, CellStyle.Default);

        Assert.Equal("one", buffer.GetLineText(0));
        Assert.Equal(string.Empty, buffer.GetLineText(1));
        Assert.Equal(string.Empty, buffer.GetLineText(2));
        Assert.Equal("four", buffer.GetLineText(3));
    }

    [Fact]
    public void Resize_Wider_KeepsContent()
    {
        var buffer = Buffer(10, 3);
        WriteLines(buffer, "hello", "world");

        buffer.Resize(new TerminalSize(20, 3), CellStyle.Default);

        Assert.Equal(new TerminalSize(20, 3), buffer.Size);
        Assert.Equal("hello", buffer.GetLineText(0));
        Assert.Equal("world", buffer.GetLineText(1));
    }

    [Fact]
    public void Resize_MoreRows_AddsBlankLines()
    {
        var buffer = Buffer(10, 2);
        WriteLines(buffer, "hello", "world");

        buffer.Resize(new TerminalSize(10, 4), CellStyle.Default);

        Assert.Equal("hello", buffer.GetLineText(0));
        Assert.Equal(string.Empty, buffer.GetLineText(3));
    }

    [Fact]
    public void Resize_FewerRows_DropsFromTheBottom()
    {
        var buffer = Buffer(10, 4);
        WriteLines(buffer, "one", "two", "three", "four");

        buffer.Resize(new TerminalSize(10, 2), CellStyle.Default);

        Assert.Equal(2, buffer.Size.Rows);
        Assert.Equal("one", buffer.GetLineText(0));
        Assert.Equal("two", buffer.GetLineText(1));
    }

    [Fact]
    public void Resize_MarksEverythingDirty()
    {
        var buffer = Buffer(10, 3);
        buffer.ClearDamage();

        buffer.Resize(new TerminalSize(12, 3), CellStyle.Default);

        Assert.True(buffer.IsRowDirty(0));
        Assert.True(buffer.IsRowDirty(2));
    }

    [Fact]
    public void GetText_JoinsRowsWithNewlines()
    {
        var buffer = Buffer(10, 3);
        WriteLines(buffer, "one", "two");

        Assert.Equal("one\ntwo\n", buffer.GetText());
    }

    private static TerminalBuffer Buffer(int columns, int rows) => new(new TerminalSize(columns, rows));

    private static void WriteLines(TerminalBuffer buffer, params string[] lines)
    {
        for (var row = 0; row < lines.Length; row++)
        {
            for (var column = 0; column < lines[row].Length; column++)
            {
                buffer.SetCell(column, row, new TerminalCell(new Rune(lines[row][column]), CellStyle.Default));
            }
        }
    }
}
