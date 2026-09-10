using NovaTerminal.Core;
using NovaTerminal.Terminal;

namespace NovaTerminal.Terminal.Tests;

/// <summary>Erasing, inserting, deleting, scrolling regions, resizing and reset.</summary>
public sealed class TerminalStateEditingTests
{
    [Fact]
    public void EraseInLine_ToEndClearsFromTheCursorOnward()
    {
        var terminal = Terminal("abcdefghij");
        terminal.MoveCursorTo(4, 0);

        terminal.EraseInLine(EraseExtent.ToEnd);

        Assert.Equal("abcd", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void EraseInLine_ToStartClearsUpToAndIncludingTheCursor()
    {
        var terminal = Terminal("abcdefghij");
        terminal.MoveCursorTo(4, 0);

        terminal.EraseInLine(EraseExtent.ToStart);

        Assert.Equal("     fghij", terminal.Buffer.GetLine(0).GetText(trimTrailingBlanks: false));
    }

    [Fact]
    public void EraseInLine_AllClearsTheWholeRowAndLeavesTheCursorAlone()
    {
        var terminal = Terminal("abcdefghij");
        terminal.MoveCursorTo(4, 0);

        terminal.EraseInLine(EraseExtent.All);

        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(0));
        Assert.Equal(4, terminal.Cursor.Column);
    }

    [Fact]
    public void Erasing_AppliesTheCurrentBackgroundButNotOtherAttributes()
    {
        var terminal = Terminal("abcdefghij");
        terminal.CurrentStyle = CellStyle.Default
            .WithBackground(TerminalColor.FromAnsi(AnsiColor.Blue))
            .WithForeground(TerminalColor.FromAnsi(AnsiColor.Red))
            .WithAttributes(TextAttributes.Underline);
        terminal.MoveCursorTo(0, 0);

        terminal.EraseInLine(EraseExtent.All);

        var cell = terminal.Buffer[0, 0];

        // Background colour erase: the region keeps the colour, but carrying the underline across
        // would paint stray lines through supposedly blank space.
        Assert.Equal(TerminalColor.FromAnsi(AnsiColor.Blue), cell.Background);
        Assert.Equal(TextAttributes.None, cell.Attributes);
        Assert.True(cell.Foreground.IsDefault);
    }

    [Fact]
    public void EraseInDisplay_ToEndClearsTheRestOfTheScreen()
    {
        var terminal = ScreenOf("one", "two", "three", "four");
        terminal.MoveCursorTo(1, 1);

        terminal.EraseInDisplay(EraseExtent.ToEnd);

        Assert.Equal("one", terminal.Buffer.GetLineText(0));
        Assert.Equal("t", terminal.Buffer.GetLineText(1));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(2));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(3));
    }

    [Fact]
    public void EraseInDisplay_ToStartClearsEverythingBefore()
    {
        var terminal = ScreenOf("one", "two", "three", "four");
        terminal.MoveCursorTo(1, 1);

        terminal.EraseInDisplay(EraseExtent.ToStart);

        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(0));
        Assert.Equal("  o", terminal.Buffer.GetLine(1).GetText(trimTrailingBlanks: false)[..3]);
        Assert.Equal("three", terminal.Buffer.GetLineText(2));
    }

    [Fact]
    public void EraseInDisplay_AllClearsEverythingAndKeepsTheCursor()
    {
        var terminal = ScreenOf("one", "two", "three");
        terminal.MoveCursorTo(2, 1);

        terminal.EraseInDisplay(EraseExtent.All);

        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(0));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(2));
        Assert.Equal(2, terminal.Cursor.Column);
        Assert.Equal(1, terminal.Cursor.Row);
    }

    [Fact]
    public void EraseCharacters_BlanksInPlaceWithoutShifting()
    {
        var terminal = Terminal("abcdefghij");
        terminal.MoveCursorTo(2, 0);

        terminal.EraseCharacters(3);

        // ECH leaves the rest of the line where it is; only DCH pulls it left.
        Assert.Equal("ab   fghij", terminal.Buffer.GetLine(0).GetText(trimTrailingBlanks: false));
    }

    [Fact]
    public void InsertCharacters_PushesTheRestOfTheLineRight()
    {
        var terminal = Terminal("abcdefghij");
        terminal.MoveCursorTo(2, 0);

        terminal.InsertCharacters(2);

        Assert.Equal("ab  cdefgh", terminal.Buffer.GetLine(0).GetText(trimTrailingBlanks: false));
    }

    [Fact]
    public void DeleteCharacters_PullsTheRestOfTheLineLeft()
    {
        var terminal = Terminal("abcdefghij");
        terminal.MoveCursorTo(2, 0);

        terminal.DeleteCharacters(2);

        Assert.Equal("abefghij  ", terminal.Buffer.GetLine(0).GetText(trimTrailingBlanks: false));
    }

    [Fact]
    public void InsertLines_PushesTheRestOfTheRegionDown()
    {
        var terminal = ScreenOf("one", "two", "three", "four");
        terminal.MoveCursorTo(0, 1);

        terminal.InsertLines(1);

        Assert.Equal("one", terminal.Buffer.GetLineText(0));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(1));
        Assert.Equal("two", terminal.Buffer.GetLineText(2));
        Assert.Equal("three", terminal.Buffer.GetLineText(3));
    }

    [Fact]
    public void DeleteLines_PullsTheRestOfTheRegionUp()
    {
        var terminal = ScreenOf("one", "two", "three", "four");
        terminal.MoveCursorTo(0, 1);

        terminal.DeleteLines(1);

        Assert.Equal("one", terminal.Buffer.GetLineText(0));
        Assert.Equal("three", terminal.Buffer.GetLineText(1));
        Assert.Equal("four", terminal.Buffer.GetLineText(2));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(3));
    }

    [Fact]
    public void InsertAndDeleteLines_AreIgnoredOutsideTheScrollingRegion()
    {
        var terminal = ScreenOf("one", "two", "three", "four", "five");
        terminal.SetScrollRegion(1, 3);
        terminal.MoveCursorTo(0, 4);

        terminal.InsertLines(1);
        terminal.DeleteLines(1);

        Assert.Equal("five", terminal.Buffer.GetLineText(4));
        Assert.Equal("two", terminal.Buffer.GetLineText(1));
    }

    [Fact]
    public void LineFeedAtTheBottomMargin_ScrollsOnlyTheRegion()
    {
        var terminal = ScreenOf("header", "a", "b", "c", "status");
        terminal.SetScrollRegion(1, 3);
        terminal.MoveCursorTo(0, 3);

        terminal.LineFeed();

        // The pinned header and status line must not move.
        Assert.Equal("header", terminal.Buffer.GetLineText(0));
        Assert.Equal("b", terminal.Buffer.GetLineText(1));
        Assert.Equal("c", terminal.Buffer.GetLineText(2));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(3));
        Assert.Equal("status", terminal.Buffer.GetLineText(4));
        Assert.Equal(3, terminal.Cursor.Row);
    }

    [Fact]
    public void ReverseLineFeedAtTheTopMargin_ScrollsTheRegionDown()
    {
        var terminal = ScreenOf("header", "a", "b", "c", "status");
        terminal.SetScrollRegion(1, 3);
        terminal.MoveCursorTo(0, 1);

        terminal.ReverseLineFeed();

        Assert.Equal("header", terminal.Buffer.GetLineText(0));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(1));
        Assert.Equal("a", terminal.Buffer.GetLineText(2));
        Assert.Equal("b", terminal.Buffer.GetLineText(3));
        Assert.Equal("status", terminal.Buffer.GetLineText(4));
    }

    [Fact]
    public void SetScrollRegion_HomesTheCursor()
    {
        var terminal = ScreenOf("one", "two", "three", "four");
        terminal.MoveCursorTo(5, 3);

        Assert.True(terminal.SetScrollRegion(1, 3));

        Assert.Equal(0, terminal.Cursor.Column);
        Assert.Equal(0, terminal.Cursor.Row);
    }

    [Theory]
    [InlineData(2, 2)]   // A one-row region could not scroll.
    [InlineData(3, 1)]   // Inverted.
    [InlineData(-1, 3)]  // Off-screen.
    [InlineData(0, 99)]  // Past the last row.
    public void SetScrollRegion_RejectsRegionsThatCannotScroll(int top, int bottom)
    {
        var terminal = ScreenOf("one", "two", "three", "four");

        Assert.False(terminal.SetScrollRegion(top, bottom));
        Assert.Equal(ScrollRegion.FullScreen(terminal.Size), terminal.ScrollRegion);
    }

    [Fact]
    public void Resize_Wider_KeepsContentAndReleasesTheCursorClamp()
    {
        var terminal = ScreenOf("hello");

        terminal.Resize(new TerminalSize(40, 6));

        Assert.Equal(new TerminalSize(40, 6), terminal.Size);
        Assert.Equal("hello", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void Resize_ShorterKeepsTheCursorLineVisible()
    {
        var terminal = ScreenOf("one", "two", "three", "four", "five");
        terminal.MoveCursorTo(0, 4);

        terminal.Resize(new TerminalSize(10, 3));

        // Dropping rows from the bottom would discard the line the user is typing on. The content
        // is scrolled up instead, so the prompt survives.
        Assert.Equal("three", terminal.Buffer.GetLineText(0));
        Assert.Equal("five", terminal.Buffer.GetLineText(2));
        Assert.Equal(2, terminal.Cursor.Row);
    }

    [Fact]
    public void Resize_ResetsTheScrollingRegion()
    {
        var terminal = ScreenOf("one", "two", "three", "four");
        terminal.SetScrollRegion(1, 2);

        terminal.Resize(new TerminalSize(10, 8));

        // Margins chosen for one screen height rarely make sense at another.
        Assert.Equal(ScrollRegion.FullScreen(terminal.Size), terminal.ScrollRegion);
    }

    [Fact]
    public void Resize_ExtendsTabStopsAcrossTheNewWidth()
    {
        var terminal = new TerminalState(new TerminalSize(10, 3));

        terminal.Resize(new TerminalSize(40, 3));
        terminal.MoveCursorTo(20, 0);
        terminal.HorizontalTab();

        Assert.Equal(24, terminal.Cursor.Column);
    }

    [Fact]
    public void Reset_ReturnsEverythingToItsPowerOnState()
    {
        var terminal = ScreenOf("one", "two", "three", "four");
        terminal.SetScrollRegion(1, 2);
        terminal.CurrentStyle = CellStyle.Default.WithAttributes(TextAttributes.Bold);
        terminal.AutoWrap = false;
        terminal.Cursor.IsVisible = false;
        terminal.MoveCursorTo(5, 2);
        terminal.TabStops.ClearAll();

        terminal.Reset();

        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(0));
        Assert.Equal(CellStyle.Default, terminal.CurrentStyle);
        Assert.True(terminal.AutoWrap);
        Assert.True(terminal.Cursor.IsVisible);
        Assert.Equal(0, terminal.Cursor.Row);
        Assert.Equal(ScrollRegion.FullScreen(terminal.Size), terminal.ScrollRegion);
        Assert.True(terminal.TabStops.IsStop(8));
    }

    private static TerminalState Terminal(string text)
    {
        var terminal = new TerminalState(new TerminalSize(Math.Max(text.Length, 1), 4));
        terminal.Print(text);
        terminal.MoveCursorTo(0, 0);
        return terminal;
    }

    private static TerminalState ScreenOf(params string[] lines)
    {
        var columns = Math.Max(lines.Max(line => line.Length), 10);
        var terminal = new TerminalState(new TerminalSize(columns, lines.Length));

        for (var row = 0; row < lines.Length; row++)
        {
            terminal.MoveCursorTo(0, row);
            terminal.Print(lines[row]);
        }

        terminal.MoveCursorTo(0, 0);
        return terminal;
    }
}
