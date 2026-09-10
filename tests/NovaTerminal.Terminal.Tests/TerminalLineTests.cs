using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Terminal;

namespace NovaTerminal.Terminal.Tests;

public sealed class TerminalLineTests
{
    private static readonly CellStyle OnBlue = CellStyle.Default
        .WithBackground(TerminalColor.FromAnsi(AnsiColor.Blue));

    [Fact]
    public void NewLine_IsBlank()
    {
        var line = new TerminalLine(10);

        Assert.Equal(10, line.Columns);
        Assert.False(line.IsWrapped);
        Assert.Equal(string.Empty, line.GetText());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveWidths(int columns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalLine(columns));
    }

    [Fact]
    public void Indexer_RoundTripsCells()
    {
        var line = Line("abc");

        Assert.Equal(new Rune('b'), line[1].Character);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Indexer_RejectsColumnsOutsideTheLine(int column)
    {
        var line = new TerminalLine(5);

        Assert.Throws<ArgumentOutOfRangeException>(() => line[column]);
    }

    [Fact]
    public void GetText_TrimsTrailingBlanksByDefault()
    {
        var line = Line("hi", columns: 10);

        Assert.Equal("hi", line.GetText());
        Assert.Equal("hi        ", line.GetText(trimTrailingBlanks: false));
    }

    [Fact]
    public void GetText_SkipsThePlaceholderHalfOfAWideCharacter()
    {
        var line = new TerminalLine(6);
        line[0] = new TerminalCell(new Rune(0x4E2D), CellStyle.Default, CellRole.WideLeading);
        line[1] = TerminalCell.WideTrailing(CellStyle.Default);
        line[2] = new TerminalCell(new Rune('!'), CellStyle.Default);

        // The placeholder holds a column but is not a character, so copied text must not gain a
        // stray space where a wide character stood.
        Assert.Equal("中!", line.GetText());
    }

    [Fact]
    public void Clear_ErasesEverythingAndAppliesTheStyle()
    {
        var line = Line("hello");
        line.IsWrapped = true;

        line.Clear(OnBlue);

        Assert.Equal(string.Empty, line.GetText());
        Assert.Equal(OnBlue.Background, line[0].Background);
        Assert.False(line.IsWrapped);
    }

    [Fact]
    public void ClearRange_ErasesOnlyTheGivenColumns()
    {
        var line = Line("abcdefghij");

        line.Clear(2, 4, CellStyle.Default);

        Assert.Equal("ab   fghij", line.GetText(trimTrailingBlanks: false));
    }

    [Fact]
    public void ClearRange_IgnoresAnInvertedRange()
    {
        var line = Line("abcde");

        line.Clear(3, 1, CellStyle.Default);

        Assert.Equal("abcde", line.GetText());
    }

    [Fact]
    public void InsertCells_PushesTheRemainderRightAndDropsWhatFallsOff()
    {
        var line = Line("abcde");

        line.InsertCells(1, 2, CellStyle.Default);

        // "a", two blanks, then "bc"; "d" and "e" were pushed past the end and discarded.
        Assert.Equal("a  bc", line.GetText(trimTrailingBlanks: false));
    }

    [Fact]
    public void DeleteCells_PullsTheRemainderLeftAndBlanksTheEnd()
    {
        var line = Line("abcde");

        line.DeleteCells(1, 2, CellStyle.Default);

        Assert.Equal("ade  ", line.GetText(trimTrailingBlanks: false));
    }

    [Fact]
    public void InsertAndDeleteCells_ClampToTheEndOfTheLine()
    {
        var line = Line("abcde");

        line.InsertCells(3, 100, CellStyle.Default);
        Assert.Equal("abc  ", line.GetText(trimTrailingBlanks: false));

        line = Line("abcde");
        line.DeleteCells(3, 100, CellStyle.Default);
        Assert.Equal("abc  ", line.GetText(trimTrailingBlanks: false));
    }

    [Fact]
    public void Resize_Wider_KeepsContentAndBlanksTheNewSpace()
    {
        var line = Line("abc");

        line.Resize(6, CellStyle.Default);

        Assert.Equal(6, line.Columns);
        Assert.Equal("abc", line.GetText());
    }

    [Fact]
    public void Resize_Narrower_TruncatesFromTheRight()
    {
        var line = Line("abcdef");

        line.Resize(3, CellStyle.Default);

        Assert.Equal("abc", line.GetText());
    }

    [Fact]
    public void Resize_Narrower_DoesNotLeaveHalfOfAWideCharacter()
    {
        var line = new TerminalLine(6);
        line[0] = new TerminalCell(new Rune('a'), CellStyle.Default);
        line[1] = new TerminalCell(new Rune(0x4E2D), CellStyle.Default, CellRole.WideLeading);
        line[2] = TerminalCell.WideTrailing(CellStyle.Default);

        // Cutting between the two halves would leave a leading cell with nothing to spill into.
        line.Resize(2, CellStyle.Default);

        Assert.Equal("a", line.GetText());
        Assert.True(line[1].IsEmpty);
        Assert.Equal(CellRole.Normal, line[1].Role);
    }

    private static TerminalLine Line(string text, int columns = 0)
    {
        var line = new TerminalLine(Math.Max(Math.Max(columns, text.Length), 1));
        for (var column = 0; column < text.Length; column++)
        {
            line[column] = new TerminalCell(new Rune(text[column]), CellStyle.Default);
        }

        return line;
    }
}
