using NovaTerminal.Core;
using NovaTerminal.Terminal;

namespace NovaTerminal.Terminal.Tests;

/// <summary>Cursor movement, the C0 control characters, tabs and the saved cursor.</summary>
public sealed class TerminalStateCursorTests
{
    [Fact]
    public void CarriageReturn_ReturnsToColumnZeroWithoutChangingRow()
    {
        var terminal = new TerminalState(new TerminalSize(20, 5));
        terminal.Print("hello");

        terminal.CarriageReturn();

        Assert.Equal(0, terminal.Cursor.Column);
        Assert.Equal(0, terminal.Cursor.Row);
        Assert.Equal("hello", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void LineFeed_MovesDownWithoutChangingColumn()
    {
        var terminal = new TerminalState(new TerminalSize(20, 5));
        terminal.Print("hello");

        terminal.LineFeed();

        // A bare line feed is not a newline: the column is deliberately left alone, which is why
        // Unix output pairs it with a carriage return.
        Assert.Equal(5, terminal.Cursor.Column);
        Assert.Equal(1, terminal.Cursor.Row);
    }

    [Fact]
    public void LineFeedOnTheLastRow_ScrollsInsteadOfMoving()
    {
        var terminal = new TerminalState(new TerminalSize(10, 3));
        terminal.Print("one");
        terminal.CarriageReturn();
        terminal.LineFeed();
        terminal.Print("two");
        terminal.CarriageReturn();
        terminal.LineFeed();
        terminal.Print("three");

        terminal.LineFeed();

        Assert.Equal(2, terminal.Cursor.Row);
        Assert.Equal("two", terminal.Buffer.GetLineText(0));
        Assert.Equal("three", terminal.Buffer.GetLineText(1));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(2));
    }

    [Fact]
    public void ReverseLineFeedOnTheFirstRow_ScrollsTheScreenDown()
    {
        var terminal = new TerminalState(new TerminalSize(10, 3));
        terminal.Print("one");
        terminal.MoveCursorTo(0, 0);

        terminal.ReverseLineFeed();

        Assert.Equal(0, terminal.Cursor.Row);
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(0));
        Assert.Equal("one", terminal.Buffer.GetLineText(1));
    }

    [Fact]
    public void Backspace_MovesOneColumnLeftWithoutErasing()
    {
        var terminal = new TerminalState(new TerminalSize(10, 3));
        terminal.Print("abc");

        terminal.Backspace();

        Assert.Equal(2, terminal.Cursor.Column);
        Assert.Equal("abc", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void Backspace_AtColumnZeroDoesNothing()
    {
        var terminal = new TerminalState(new TerminalSize(10, 3));
        terminal.MoveCursorTo(0, 1);

        terminal.Backspace();

        Assert.Equal(0, terminal.Cursor.Column);
        Assert.Equal(1, terminal.Cursor.Row);
    }

    [Fact]
    public void Backspace_WithAWrapPendingCancelsTheWrapAndStaysPut()
    {
        var terminal = new TerminalState(new TerminalSize(5, 3));
        terminal.Print("abcde");

        terminal.Backspace();

        // The cursor was logically past the end of the line while being shown in the last column.
        // Backspacing leaves it over the character just typed, which is where a shell's
        // "backspace, space, backspace" erase expects to land.
        Assert.False(terminal.Cursor.PendingWrap);
        Assert.Equal(4, terminal.Cursor.Column);
    }

    [Fact]
    public void BackspaceSpaceBackspace_ErasesTheLastCharacter()
    {
        // The sequence every shell sends when the user presses Backspace.
        var terminal = new TerminalState(new TerminalSize(10, 3));
        terminal.Print("abc");

        terminal.Backspace();
        terminal.Print(" ");
        terminal.Backspace();

        Assert.Equal(2, terminal.Cursor.Column);

        // The "c" is gone, replaced by a space that was genuinely written. The buffer keeps that
        // distinction: trailing cells are trimmed from extracted text only when nothing was ever
        // written to them, because a written space is content a selection should include.
        Assert.Equal(new System.Text.Rune(' '), terminal.Buffer[2, 0].Character);
        Assert.False(terminal.Buffer[2, 0].IsEmpty);
        Assert.Equal("ab ", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void Tab_AdvancesToTheNextStop()
    {
        var terminal = new TerminalState(new TerminalSize(40, 3));

        terminal.HorizontalTab();
        Assert.Equal(8, terminal.Cursor.Column);

        terminal.HorizontalTab();
        Assert.Equal(16, terminal.Cursor.Column);
    }

    [Fact]
    public void Tab_FromMidwayAdvancesToTheNextStopNotAFixedDistance()
    {
        var terminal = new TerminalState(new TerminalSize(40, 3));
        terminal.Print("abc");

        terminal.HorizontalTab();

        // A tab means "go to the next stop", not "insert eight spaces".
        Assert.Equal(8, terminal.Cursor.Column);
    }

    [Fact]
    public void Tab_StopsAtTheLastColumnWhenNoStopRemains()
    {
        var terminal = new TerminalState(new TerminalSize(10, 3));
        terminal.MoveCursorTo(9, 0);

        terminal.HorizontalTab();

        Assert.Equal(9, terminal.Cursor.Column);
    }

    [Fact]
    public void BackTab_ReturnsToThePreviousStop()
    {
        var terminal = new TerminalState(new TerminalSize(40, 3));
        terminal.MoveCursorTo(20, 0);

        terminal.BackTab();

        Assert.Equal(16, terminal.Cursor.Column);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(9, 9)]
    [InlineData(100, 9)]
    public void MoveCursorTo_ClampsToTheScreen(int requested, int expected)
    {
        var terminal = new TerminalState(new TerminalSize(10, 5));

        terminal.MoveCursorTo(requested, 0);

        Assert.Equal(expected, terminal.Cursor.Column);
    }

    [Fact]
    public void CursorMovement_ClearsAPendingWrap()
    {
        var terminal = new TerminalState(new TerminalSize(5, 3));
        terminal.Print("abcde");

        terminal.MoveCursorTo(0, 0);

        Assert.False(terminal.Cursor.PendingWrap);
    }

    [Fact]
    public void RelativeMovement_ClampsAtTheScreenEdges()
    {
        var terminal = new TerminalState(new TerminalSize(10, 5));
        terminal.MoveCursorTo(5, 2);

        terminal.MoveCursorUp(100);
        Assert.Equal(0, terminal.Cursor.Row);

        terminal.MoveCursorDown(100);
        Assert.Equal(4, terminal.Cursor.Row);

        terminal.MoveCursorBackward(100);
        Assert.Equal(0, terminal.Cursor.Column);

        terminal.MoveCursorForward(100);
        Assert.Equal(9, terminal.Cursor.Column);
    }

    [Fact]
    public void RelativeMovement_TreatsZeroAsOne()
    {
        // A CSI parameter of zero means one, which the engine applies in a single place.
        var terminal = new TerminalState(new TerminalSize(10, 5));
        terminal.MoveCursorTo(5, 2);

        terminal.MoveCursorUp(0);

        Assert.Equal(1, terminal.Cursor.Row);
    }

    [Fact]
    public void CursorMovement_StopsAtTheMarginsRatherThanScrolling()
    {
        var terminal = new TerminalState(new TerminalSize(10, 6));
        terminal.SetScrollRegion(1, 4);
        terminal.MoveCursorTo(0, 2);

        terminal.MoveCursorUp(10);
        Assert.Equal(1, terminal.Cursor.Row);

        terminal.MoveCursorDown(10);
        Assert.Equal(4, terminal.Cursor.Row);
    }

    [Fact]
    public void SaveAndRestore_RoundTripPositionAndPen()
    {
        var terminal = new TerminalState(new TerminalSize(20, 5));
        terminal.MoveCursorTo(7, 3);
        terminal.CurrentStyle = CellStyle.Default.WithForeground(TerminalColor.FromAnsi(AnsiColor.Green));

        terminal.SaveCursor();
        terminal.MoveCursorTo(0, 0);
        terminal.CurrentStyle = CellStyle.Default;
        terminal.RestoreCursor();

        // DECSC saves the graphic rendition along with the position, so one sequence puts both back.
        Assert.Equal(7, terminal.Cursor.Column);
        Assert.Equal(3, terminal.Cursor.Row);
        Assert.Equal(TerminalColor.FromAnsi(AnsiColor.Green), terminal.CurrentStyle.Foreground);
    }

    [Fact]
    public void RestoreWithoutASave_HomesTheCursorAndResetsThePen()
    {
        var terminal = new TerminalState(new TerminalSize(20, 5));
        terminal.MoveCursorTo(7, 3);
        terminal.CurrentStyle = CellStyle.Default.WithAttributes(TextAttributes.Bold);

        terminal.RestoreCursor();

        Assert.Equal(0, terminal.Cursor.Column);
        Assert.Equal(0, terminal.Cursor.Row);
        Assert.Equal(CellStyle.Default, terminal.CurrentStyle);
    }

    [Fact]
    public void RestoreAfterTheScreenShrank_ClampsTheSavedPosition()
    {
        var terminal = new TerminalState(new TerminalSize(20, 10));
        terminal.MoveCursorTo(15, 8);
        terminal.SaveCursor();

        terminal.Resize(new TerminalSize(10, 4));
        terminal.RestoreCursor();

        Assert.Equal(9, terminal.Cursor.Column);
        Assert.Equal(3, terminal.Cursor.Row);
    }
}
