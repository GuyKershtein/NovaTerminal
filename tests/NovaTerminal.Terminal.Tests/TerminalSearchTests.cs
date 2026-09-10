using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.Terminal.Tests;

public sealed class TerminalSearchTests
{
    [Fact]
    public void SearchFindsTextOnTheLiveScreen()
    {
        var terminal = TerminalWith("alpha", "beta", "gamma");

        var matches = TerminalSearch.FindAll(terminal, "beta");

        var match = Assert.Single(matches);
        Assert.Equal(0, match.Column);
        Assert.Equal(4, match.Length);
    }

    [Fact]
    public void SearchReachesBackIntoHistory()
    {
        // The point of searching a terminal is finding output that has already scrolled away.
        var terminal = TerminalWith(rows: 3, lines: ["needle here", "b", "c", "d", "e", "f"]);

        var match = Assert.Single(TerminalSearch.FindAll(terminal, "needle"));

        Assert.Equal(0, match.Line);
        Assert.True(match.Line < terminal.Scrollback!.Count, "The match should lie in history.");
    }

    [Fact]
    public void SearchIsCaseInsensitiveByDefault()
    {
        var terminal = TerminalWith("Hello World");

        Assert.Single(TerminalSearch.FindAll(terminal, "hello world"));
        Assert.Empty(TerminalSearch.FindAll(terminal, "hello world", caseSensitive: true));
    }

    [Fact]
    public void EveryOccurrenceOnALineIsFound()
    {
        var terminal = TerminalWith("aXbXcX");

        Assert.Equal(3, TerminalSearch.FindAll(terminal, "X", caseSensitive: true).Count);
    }

    [Fact]
    public void ResultsAreBounded()
    {
        // Searching a large history for a common character must not produce an unbounded list.
        var terminal = TerminalWith(rows: 4, lines: [.. Enumerable.Repeat("aaaa", 40)]);

        Assert.Equal(10, TerminalSearch.FindAll(terminal, "a", limit: 10).Count);
    }

    [Fact]
    public void AnEmptyQueryMatchesNothing()
    {
        var terminal = TerminalWith("content");

        Assert.Empty(TerminalSearch.FindAll(terminal, string.Empty));
    }

    [Fact]
    public void FindNextWrapsAroundTheEnd()
    {
        var matches = new List<SearchMatch>
        {
            new(2, 0, 1),
            new(7, 0, 1),
        };

        Assert.Equal(matches[0], TerminalSearch.FindNext(matches, 0));
        Assert.Equal(matches[1], TerminalSearch.FindNext(matches, 3));

        // Past the last match, "next" returns to the first: what a user expects from find-next.
        Assert.Equal(matches[0], TerminalSearch.FindNext(matches, 99));
    }

    [Fact]
    public void FindPreviousWrapsAroundTheStart()
    {
        var matches = new List<SearchMatch>
        {
            new(2, 0, 1),
            new(7, 0, 1),
        };

        Assert.Equal(matches[1], TerminalSearch.FindPrevious(matches, 9));
        Assert.Equal(matches[0], TerminalSearch.FindPrevious(matches, 5));
        Assert.Equal(matches[1], TerminalSearch.FindPrevious(matches, 0));
    }

    [Fact]
    public void FindNextOnNoMatchesReturnsNothing()
    {
        Assert.Null(TerminalSearch.FindNext([], 0));
        Assert.Null(TerminalSearch.FindPrevious([], 0));
    }

    [Fact]
    public void AMatchInHistoryMapsToTheOffsetThatShowsIt()
    {
        var terminal = TerminalWith(rows: 3, lines: ["target", "b", "c", "d", "e", "f"]);

        var offset = TerminalSearch.GetViewportOffsetFor(terminal, 0);

        Assert.True(offset > 0);
        terminal.ScrollViewBack(offset);
        Assert.Equal("target", terminal.GetViewRowText(0));
    }

    [Fact]
    public void AMatchOnTheLiveScreenNeedsNoScrolling()
    {
        var terminal = TerminalWith(rows: 3, lines: ["a", "b", "c", "d", "target"]);
        var line = TerminalSearch.GetTotalLineCount(terminal) - 1;

        Assert.Equal(0, TerminalSearch.GetViewportOffsetFor(terminal, line));
    }

    private static TerminalState TerminalWith(params string[] lines) => TerminalWith(10, lines);

    private static TerminalState TerminalWith(int rows, string[] lines)
    {
        var terminal = new TerminalState(new TerminalSize(30, rows));
        terminal.ConfigureScrollback(200);
        var parser = new AnsiParser(new TerminalInterpreter(terminal));

        for (var index = 0; index < lines.Length; index++)
        {
            parser.Parse(Encoding.UTF8.GetBytes(lines[index]));

            if (index < lines.Length - 1)
            {
                parser.Parse("\r\n"u8);
            }
        }

        return terminal;
    }
}
