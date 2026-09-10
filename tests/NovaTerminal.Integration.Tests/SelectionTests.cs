using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Rendering;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.Integration.Tests;

/// <summary>
/// Selection and the text it produces. Copying from a terminal means copying the text that was
/// displayed, not a picture of it, and these are the rules that make the result usable.
/// </summary>
public sealed class SelectionTests
{
    [Fact]
    public void ASelectionOnOneRowYieldsThatSpan()
    {
        var terminal = TerminalWith("hello world");
        var selection = new TerminalSelection(new CellPosition(6, 0), new CellPosition(11, 0));

        Assert.Equal("world", selection.GetText(terminal));
    }

    [Fact]
    public void SelectingBackwardsIsTheSameAsSelectingForwards()
    {
        // The anchor is wherever the drag started, which may be the later of the two ends.
        var terminal = TerminalWith("hello world");

        var forwards = new TerminalSelection(new CellPosition(0, 0), new CellPosition(5, 0));
        var backwards = new TerminalSelection(new CellPosition(5, 0), new CellPosition(0, 0));

        Assert.Equal(forwards.GetText(terminal), backwards.GetText(terminal));
        Assert.Equal("hello", backwards.GetText(terminal));
    }

    [Fact]
    public void AMultiRowSelectionJoinsRowsWithLineBreaks()
    {
        var terminal = TerminalWith("first", "second", "third");
        var selection = new TerminalSelection(new CellPosition(0, 0), new CellPosition(6, 1));

        Assert.Equal("first\nsecond", selection.GetText(terminal));
    }

    [Fact]
    public void TrailingBlanksAreNotCopied()
    {
        // The cells to the right of a line's text were never part of it; pasting them would paste
        // invisible padding.
        var terminal = TerminalWith("hi", "there");
        var selection = new TerminalSelection(new CellPosition(0, 0), new CellPosition(30, 1));

        Assert.Equal("hi\nthere", selection.GetText(terminal));
    }

    [Fact]
    public void AWrappedLineIsCopiedAsOneLine()
    {
        // The terminal broke this line for display. The user selected one logical line, so pasting
        // it into an editor should not introduce a break the author never wrote.
        var terminal = new TerminalState(new TerminalSize(10, 4));
        Feed(terminal, "abcdefghijKLMNO");

        var selection = new TerminalSelection(new CellPosition(0, 0), new CellPosition(5, 1));

        Assert.True(terminal.Buffer.GetLine(0).IsWrapped);
        Assert.Equal("abcdefghijKLMNO", selection.GetText(terminal));
    }

    [Fact]
    public void WideCharactersAreCopiedOnce()
    {
        var terminal = TerminalWith("中文ok");
        var selection = new TerminalSelection(new CellPosition(0, 0), new CellPosition(6, 0));

        // Each ideograph occupies two cells but is one character; the placeholder must not appear.
        Assert.Equal("中文ok", selection.GetText(terminal));
    }

    [Fact]
    public void BlockSelectionTakesTheSameColumnsFromEachRow()
    {
        // What you want when the output is a table and you need one column of it.
        var terminal = TerminalWith("aaXXbb", "ccXXdd", "eeXXff");
        var selection = new TerminalSelection(
            new CellPosition(2, 0), new CellPosition(4, 2), SelectionMode.Block);

        Assert.Equal("XX\nXX\nXX", selection.GetText(terminal));
    }

    [Fact]
    public void AnEmptySelectionYieldsNothing()
    {
        var terminal = TerminalWith("content");
        var selection = new TerminalSelection(new CellPosition(3, 0), new CellPosition(3, 0));

        Assert.True(selection.IsEmpty);
        Assert.Equal(string.Empty, selection.GetText(terminal));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(2, 0, true)]
    [InlineData(4, 0, false)]
    public void ContainsReportsWhichCellsAreHighlighted(int column, int row, bool expected)
    {
        var selection = new TerminalSelection(new CellPosition(1, 0), new CellPosition(4, 0));

        Assert.Equal(expected, selection.Contains(column, row));
    }

    [Fact]
    public void ContainsSpansWholeRowsInTheMiddleOfALinearSelection()
    {
        var selection = new TerminalSelection(new CellPosition(3, 0), new CellPosition(2, 2));

        Assert.False(selection.Contains(1, 0));
        Assert.True(selection.Contains(1, 1));
        Assert.True(selection.Contains(99, 1));
        Assert.True(selection.Contains(1, 2));
        Assert.False(selection.Contains(3, 2));
    }

    [Fact]
    public void SelectionReadsFromHistoryWhenTheViewIsScrolledBack()
    {
        var terminal = new TerminalState(new TerminalSize(20, 3));
        terminal.ConfigureScrollback(50);

        for (var line = 1; line <= 8; line++)
        {
            Feed(terminal, $"line {line}");
            if (line < 8)
            {
                Feed(terminal, "\r\n");
            }
        }

        terminal.ScrollViewBack(3);

        var selection = new TerminalSelection(new CellPosition(0, 0), new CellPosition(6, 0));

        Assert.Equal("line 3", selection.GetText(terminal));
    }

    private static TerminalState TerminalWith(params string[] lines)
    {
        var terminal = new TerminalState(new TerminalSize(30, 6));

        for (var index = 0; index < lines.Length; index++)
        {
            Feed(terminal, lines[index]);

            if (index < lines.Length - 1)
            {
                Feed(terminal, "\r\n");
            }
        }

        return terminal;
    }

    private static void Feed(TerminalState terminal, string text)
        => new AnsiParser(new TerminalInterpreter(terminal)).Parse(Encoding.UTF8.GetBytes(text));
}
