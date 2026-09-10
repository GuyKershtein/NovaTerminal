using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.Terminal.Tests;

public sealed class ScrollbackTests
{
    private const string Esc = "\u001b";

    [Fact]
    public void LinesThatScrollOffTheTopAreRetained()
    {
        var terminal = TerminalWithHistory(capacity: 100, lines: 10, rows: 4);

        // Ten lines printed on a four-row screen leaves six in history.
        Assert.Equal(6, terminal.Scrollback!.Count);
        Assert.Equal("line 1", terminal.Scrollback[0].GetText());
        Assert.Equal("line 6", terminal.Scrollback[5].GetText());
    }

    [Fact]
    public void HistoryIsBoundedAndDiscardsTheOldestFirst()
    {
        // The bound is what stops infinite output from exhausting memory, so it is asserted rather
        // than assumed.
        var terminal = TerminalWithHistory(capacity: 3, lines: 20, rows: 4);

        Assert.Equal(3, terminal.Scrollback!.Count);
        Assert.Equal("line 14", terminal.Scrollback[0].GetText());
        Assert.Equal("line 16", terminal.Scrollback[2].GetText());
    }

    [Fact]
    public void ScrollbackCanBeDisabledEntirely()
    {
        var terminal = TerminalWithHistory(capacity: 0, lines: 20, rows: 4);

        Assert.Null(terminal.Scrollback);
        Assert.Equal(0, terminal.MaxViewportOffset);
    }

    [Fact]
    public void ScrollingARegionDoesNotEnterHistory()
    {
        // A program rearranging part of its display is not producing history; only lines leaving
        // the top of the whole screen are.
        var terminal = NewTerminal(capacity: 100, rows: 6);
        Feed(terminal, $"{Esc}[2;4r{Esc}[4;1H");

        for (var i = 0; i < 10; i++)
        {
            Feed(terminal, "x\r\n");
        }

        Assert.Equal(0, terminal.Scrollback!.Count);
    }

    [Fact]
    public void TheAlternateScreenHasNoHistory()
    {
        var terminal = TerminalWithHistory(capacity: 100, lines: 10, rows: 4);
        Feed(terminal, $"{Esc}[?1049h");

        for (var i = 0; i < 10; i++)
        {
            Feed(terminal, "editor\r\n");
        }

        // Half-drawn editor screens are not something anyone wants to scroll through.
        Assert.Equal(0, terminal.MaxViewportOffset);
    }

    [Fact]
    public void TheViewCanBeScrolledBackThroughHistory()
    {
        var terminal = TerminalWithHistory(capacity: 100, lines: 10, rows: 4);

        Assert.True(terminal.ScrollViewBack(2));

        Assert.True(terminal.IsScrolledBack);
        Assert.Equal(2, terminal.ViewportOffset);

        // The top two rows now come from history, and the rest from the live screen.
        Assert.Equal("line 5", terminal.GetViewRowText(0));
        Assert.Equal("line 6", terminal.GetViewRowText(1));
        Assert.Equal("line 7", terminal.GetViewRowText(2));
    }

    [Fact]
    public void ScrollingIsClampedToWhatHistoryExists()
    {
        var terminal = TerminalWithHistory(capacity: 100, lines: 10, rows: 4);

        terminal.ScrollViewBack(1000);

        Assert.Equal(6, terminal.ViewportOffset);
        Assert.False(terminal.ScrollViewBack(1));
    }

    [Fact]
    public void ScrollingForwardReturnsToTheLiveScreen()
    {
        var terminal = TerminalWithHistory(capacity: 100, lines: 10, rows: 4);
        terminal.ScrollViewBack(5);

        terminal.ScrollViewForward(5);

        Assert.False(terminal.IsScrolledBack);
        Assert.Equal(terminal.Buffer.GetLineText(0), terminal.GetViewRowText(0));
    }

    [Fact]
    public void NewOutputSnapsTheViewBackToTheBottom()
    {
        // Leaving a user in history while the terminal changes underneath them is disorienting.
        var terminal = TerminalWithHistory(capacity: 100, lines: 10, rows: 4);
        terminal.ScrollViewBack(4);

        Feed(terminal, "new output");

        Assert.False(terminal.IsScrolledBack);
    }

    [Fact]
    public void EraseSavedLinesDiscardsHistory()
    {
        var terminal = TerminalWithHistory(capacity: 100, lines: 10, rows: 4);

        Feed(terminal, $"{Esc}[3J");

        Assert.Equal(0, terminal.Scrollback!.Count);
        Assert.Equal(0, terminal.MaxViewportOffset);
    }

    [Fact]
    public void ScrollingRecyclesLinesRatherThanAllocating()
    {
        // At steady state a full scrollback hands its evicted line straight back to the screen, so
        // a command producing thousands of lines allocates nothing per line.
        var scrollback = new Scrollback(2);
        var first = new TerminalLine(10);
        var second = new TerminalLine(10);
        var third = new TerminalLine(10);

        Assert.Null(scrollback.Add(first));
        Assert.Null(scrollback.Add(second));
        Assert.Same(first, scrollback.Add(third));
    }

    [Fact]
    public void ADisabledScrollbackHandsEveryLineStraightBack()
    {
        var scrollback = new Scrollback(0);
        var line = new TerminalLine(10);

        Assert.Same(line, scrollback.Add(line));
        Assert.Equal(0, scrollback.Count);
        Assert.True(scrollback.IsDisabled);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void ReadingOutsideTheRetainedRangeIsRejected(int index)
    {
        var scrollback = new Scrollback(10);
        scrollback.Add(new TerminalLine(4));

        Assert.Throws<ArgumentOutOfRangeException>(() => scrollback[index]);
    }

    private static TerminalState NewTerminal(int capacity, int rows)
    {
        var terminal = new TerminalState(new TerminalSize(20, rows));
        terminal.ConfigureScrollback(capacity);
        return terminal;
    }

    private static TerminalState TerminalWithHistory(int capacity, int lines, int rows)
    {
        var terminal = NewTerminal(capacity, rows);

        for (var line = 1; line <= lines; line++)
        {
            Feed(terminal, $"line {line}");

            if (line < lines)
            {
                Feed(terminal, "\r\n");
            }
        }

        return terminal;
    }

    private static void Feed(TerminalState terminal, string input)
        => new AnsiParser(new TerminalInterpreter(terminal)).Parse(Encoding.UTF8.GetBytes(input));
}
