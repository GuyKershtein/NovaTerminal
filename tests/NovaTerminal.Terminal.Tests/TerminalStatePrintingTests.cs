using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Terminal;

namespace NovaTerminal.Terminal.Tests;

/// <summary>
/// Printing, wrapping and character width - the behaviour the ANSI parser will drive once it can
/// decode a byte stream.
/// </summary>
public sealed class TerminalStatePrintingTests
{
    [Fact]
    public void PrintingText_FillsCellsAndAdvancesTheCursor()
    {
        var terminal = new TerminalState(new TerminalSize(20, 5));

        terminal.Print("hello");

        Assert.Equal("hello", terminal.Buffer.GetLineText(0));
        Assert.Equal(5, terminal.Cursor.Column);
        Assert.Equal(0, terminal.Cursor.Row);
    }

    [Fact]
    public void PrintingText_AppliesTheCurrentPen()
    {
        var terminal = new TerminalState(new TerminalSize(20, 5));
        terminal.CurrentStyle = CellStyle.Default
            .WithForeground(TerminalColor.FromAnsi(AnsiColor.Red))
            .WithAttributes(TextAttributes.Bold);

        terminal.Print("hi");

        Assert.Equal(TerminalColor.FromAnsi(AnsiColor.Red), terminal.Buffer[0, 0].Foreground);
        Assert.True(terminal.Buffer[1, 0].Style.HasAttributes(TextAttributes.Bold));
    }

    [Fact]
    public void PrintingIntoTheLastColumn_DefersTheWrap()
    {
        var terminal = new TerminalState(new TerminalSize(5, 3));

        terminal.Print("abcde");

        // The cursor stays in the last column. This is the DEC "last column" rule: the wrap has
        // been decided but not yet performed.
        Assert.Equal(0, terminal.Cursor.Row);
        Assert.Equal(4, terminal.Cursor.Column);
        Assert.True(terminal.Cursor.PendingWrap);
        Assert.Equal("abcde", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void TheNextCharacterAfterADeferredWrap_MovesToTheFollowingLine()
    {
        var terminal = new TerminalState(new TerminalSize(5, 3));

        terminal.Print("abcdef");

        Assert.Equal("abcde", terminal.Buffer.GetLineText(0));
        Assert.Equal("f", terminal.Buffer.GetLineText(1));
        Assert.Equal(1, terminal.Cursor.Row);
        Assert.Equal(1, terminal.Cursor.Column);
        Assert.False(terminal.Cursor.PendingWrap);
    }

    [Fact]
    public void ExactlyOneScreenWidthFollowedByANewline_DoesNotLeaveABlankLine()
    {
        // This is what the deferred wrap exists for. Without it, filling the line would advance to
        // row 1 immediately, and the newline would then skip to row 2, leaving a blank line in
        // every piece of output formatted to the terminal's exact width.
        var terminal = new TerminalState(new TerminalSize(5, 4));

        terminal.Print("abcde");
        terminal.CarriageReturn();
        terminal.LineFeed();
        terminal.Print("next");

        Assert.Equal("abcde", terminal.Buffer.GetLineText(0));
        Assert.Equal("next", terminal.Buffer.GetLineText(1));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(2));
    }

    [Fact]
    public void WrappingRecordsThatTheLineContinues()
    {
        var terminal = new TerminalState(new TerminalSize(5, 3));

        terminal.Print("abcdefg");

        // Copying this text later must rejoin the two rows rather than insert a line break.
        Assert.True(terminal.Buffer.GetLine(0).IsWrapped);
        Assert.False(terminal.Buffer.GetLine(1).IsWrapped);
    }

    [Fact]
    public void WrappingOnTheLastRow_ScrollsTheScreen()
    {
        var terminal = new TerminalState(new TerminalSize(3, 2));

        terminal.Print("abc");   // fills row 0
        terminal.Print("def");   // wraps to row 1 and fills it
        terminal.Print("g");     // wraps again, which has to scroll

        Assert.Equal("def", terminal.Buffer.GetLineText(0));
        Assert.Equal("g", terminal.Buffer.GetLineText(1));
        Assert.Equal(1, terminal.Cursor.Row);
    }

    [Fact]
    public void WithAutoWrapDisabled_CharactersPileUpInTheLastColumn()
    {
        var terminal = new TerminalState(new TerminalSize(5, 3)) { AutoWrap = false };

        terminal.Print("abcdefg");

        // DECAWM off: the cursor never leaves the last column and each character overwrites the last.
        Assert.Equal("abcdg", terminal.Buffer.GetLineText(0));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(1));
        Assert.False(terminal.Cursor.PendingWrap);
    }

    [Fact]
    public void WideCharacters_OccupyTwoCells()
    {
        var terminal = new TerminalState(new TerminalSize(10, 3));

        terminal.Print("中文");

        Assert.True(terminal.Buffer[0, 0].IsWideLeading);
        Assert.True(terminal.Buffer[1, 0].IsWideTrailing);
        Assert.True(terminal.Buffer[2, 0].IsWideLeading);
        Assert.Equal(4, terminal.Cursor.Column);
        Assert.Equal("中文", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void OverwritingTheLeadingHalfOfAWideCharacter_ClearsItsPlaceholder()
    {
        var terminal = new TerminalState(new TerminalSize(10, 3));
        terminal.Print("中x");

        terminal.MoveCursorTo(0, 0);
        terminal.Print("a");

        // Leaving the placeholder behind would hold a column that no character occupies, shifting
        // the rest of the line by one.
        Assert.Equal(new Rune('a'), terminal.Buffer[0, 0].Character);
        Assert.Equal(CellRole.Normal, terminal.Buffer[1, 0].Role);
        Assert.True(terminal.Buffer[1, 0].IsEmpty);
    }

    [Fact]
    public void OverwritingThePlaceholderHalfOfAWideCharacter_ClearsItsLeadingCell()
    {
        var terminal = new TerminalState(new TerminalSize(10, 3));
        terminal.Print("中x");

        terminal.MoveCursorTo(1, 0);
        terminal.Print("a");

        Assert.True(terminal.Buffer[0, 0].IsEmpty);
        Assert.Equal(CellRole.Normal, terminal.Buffer[0, 0].Role);
        Assert.Equal(new Rune('a'), terminal.Buffer[1, 0].Character);
    }

    [Fact]
    public void AWideCharacterWithOneColumnLeft_WrapsRatherThanSplitting()
    {
        var terminal = new TerminalState(new TerminalSize(5, 3));

        terminal.Print("abcd");  // cursor now at column 4, the last one
        terminal.Print("中");

        // A double-width glyph cannot straddle a line break, so the odd column is left blank.
        Assert.Equal("abcd", terminal.Buffer.GetLineText(0));
        Assert.Equal("中", terminal.Buffer.GetLineText(1));
        Assert.Equal(2, terminal.Cursor.Column);
    }

    [Fact]
    public void CombiningMarks_DoNotConsumeAColumn()
    {
        var terminal = new TerminalState(new TerminalSize(10, 3));

        terminal.Print("é");

        // The accent is currently dropped rather than attached, but it must never take a column of
        // its own: doing so would shift the rest of the line relative to what the shell expects.
        Assert.Equal(1, terminal.Cursor.Column);
        Assert.Equal("e", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void PrintingMarksOnlyTheAffectedRowDirty()
    {
        var terminal = new TerminalState(new TerminalSize(10, 4));
        terminal.MoveCursorTo(0, 2);
        terminal.Buffer.ClearDamage();

        terminal.Print("hi");

        Assert.True(terminal.Buffer.IsRowDirty(2));
        Assert.False(terminal.Buffer.IsRowDirty(0));
    }
}
