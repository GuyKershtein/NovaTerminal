using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.Terminal.Tests;

/// <summary>
/// The features that make full-screen programs work: the alternate screen, character sets, origin
/// mode, insert mode and cursor styling.
/// </summary>
public sealed class TerminalFeatureTests
{
    private const string Esc = "\u001b";

    // ----- Alternate screen -----

    [Fact]
    public void TheAlternateScreenLeavesThePrimaryScreenUntouched()
    {
        // This is why your shell history is still there after quitting an editor.
        var terminal = Feed($"shell history{Esc}[?1049h");

        Assert.True(terminal.IsAlternateScreenActive);
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(0));

        Feed(terminal, $"editor content{Esc}[?1049l");

        Assert.False(terminal.IsAlternateScreenActive);
        Assert.Equal("shell history", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void SwitchingToTheAlternateScreenSavesAndRestoresTheCursor()
    {
        var terminal = Feed($"{Esc}[5;10H{Esc}[?1049h{Esc}[1;1H");

        Assert.Equal(0, terminal.Cursor.Row);

        Feed(terminal, $"{Esc}[?1049l");

        Assert.Equal(4, terminal.Cursor.Row);
        Assert.Equal(9, terminal.Cursor.Column);
    }

    [Fact]
    public void TheLegacyAlternateScreenModeDoesNotTouchTheCursor()
    {
        // Modes 47 and 1047 predate the combined behaviour and neither save the cursor nor clear.
        var terminal = Feed($"{Esc}[5;10H{Esc}[?47h");

        Assert.True(terminal.IsAlternateScreenActive);
        Assert.Equal(4, terminal.Cursor.Row);
    }

    [Fact]
    public void SwitchingScreensTwiceIsHarmless()
    {
        var terminal = Feed($"{Esc}[?1049h{Esc}[?1049h");
        Assert.True(terminal.IsAlternateScreenActive);

        Feed(terminal, $"{Esc}[?1049l{Esc}[?1049l");
        Assert.False(terminal.IsAlternateScreenActive);
    }

    [Fact]
    public void ResizingAffectsBothScreens()
    {
        var terminal = Feed($"primary{Esc}[?1049h");

        terminal.Resize(new TerminalSize(30, 8));

        Assert.Equal(new TerminalSize(30, 8), terminal.Buffer.Size);

        Feed(terminal, $"{Esc}[?1049l");

        Assert.Equal(new TerminalSize(30, 8), terminal.Buffer.Size);
        Assert.Equal("primary", terminal.Buffer.GetLineText(0));
    }

    // ----- Character sets -----

    [Fact]
    public void DecSpecialGraphicsDrawsBoxesRatherThanLetters()
    {
        // A program selects the graphics set and prints "lqk" expecting a top border. Without the
        // translation the user sees the literal letters.
        var terminal = Feed($"{Esc}(0lqk{Esc}(B");

        Assert.Equal("┌─┐", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void ShiftOutSelectsTheOtherCharacterSetSlot()
    {
        var terminal = Feed($"{Esc})0AqB");

        // "A" from G0 (ASCII), the line from G1 (graphics), then back to G0 for "B".
        Assert.Equal("A─B", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void AsciiIsRestoredWhenTheSetIsDesignatedBack()
    {
        var terminal = Feed($"{Esc}(0q{Esc}(Bq");

        Assert.Equal("─q", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void CharactersOutsideTheMappedRangePassThrough()
    {
        var terminal = Feed($"{Esc}(0AZ09");

        Assert.Equal("AZ09", terminal.Buffer.GetLineText(0));
    }

    // ----- Origin mode -----

    [Fact]
    public void OriginModeMakesPositioningRelativeToTheScrollingRegion()
    {
        var terminal = Feed($"{Esc}[3;6r{Esc}[?6h{Esc}[1;1H");

        // Row one means the top margin, not the top of the screen.
        Assert.Equal(2, terminal.Cursor.Row);
    }

    [Fact]
    public void OriginModeConfinesTheCursorToTheRegion()
    {
        var terminal = Feed($"{Esc}[3;6r{Esc}[?6h{Esc}[99;1H");

        Assert.Equal(5, terminal.Cursor.Row);

        Feed(terminal, $"{Esc}[1;1H{Esc}[A");
        Assert.Equal(2, terminal.Cursor.Row);
    }

    [Fact]
    public void ClearingOriginModeReturnsToScreenCoordinates()
    {
        var terminal = Feed($"{Esc}[3;6r{Esc}[?6h{Esc}[?6l{Esc}[1;1H");

        Assert.Equal(0, terminal.Cursor.Row);
    }

    // ----- Insert mode -----

    [Fact]
    public void InsertModePushesTheRestOfTheLineRight()
    {
        var terminal = Feed($"world{Esc}[1;1H{Esc}[4hhello ");

        Assert.Equal("hello world", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void ReplaceModeIsTheDefault()
    {
        var terminal = Feed($"world{Esc}[1;1Hhello ");

        Assert.Equal("hello ", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void InsertModeCanBeTurnedBackOff()
    {
        var terminal = Feed($"{Esc}[4h{Esc}[4l");

        Assert.False(terminal.InsertMode);
    }

    // ----- Cursor styling and other modes -----

    [Theory]
    [InlineData(1, CursorStyle.Block)]
    [InlineData(2, CursorStyle.Block)]
    [InlineData(3, CursorStyle.Underline)]
    [InlineData(4, CursorStyle.Underline)]
    [InlineData(5, CursorStyle.Bar)]
    [InlineData(6, CursorStyle.Bar)]
    public void CursorShapeIsSelectableByTheProgram(int parameter, CursorStyle expected)
    {
        var terminal = Feed($"{Esc}[{parameter} q");

        Assert.Equal(expected, terminal.Cursor.Style);
    }

    [Fact]
    public void BracketedPasteModeIsRecordedForTheInputLayer()
    {
        var terminal = Feed($"{Esc}[?2004h");
        Assert.True(terminal.BracketedPaste);

        Feed(terminal, $"{Esc}[?2004l");
        Assert.False(terminal.BracketedPaste);
    }

    [Fact]
    public void TheAlignmentPatternFillsTheScreen()
    {
        var terminal = Feed($"{Esc}#8");

        Assert.Equal(new string('E', terminal.Size.Columns), terminal.Buffer.GetLineText(0));
        Assert.Equal(new string('E', terminal.Size.Columns), terminal.Buffer.GetLineText(terminal.Size.Rows - 1));
    }

    [Fact]
    public void ModeSaveAndRestoreCursorWorksThroughItsPrivateMode()
    {
        var terminal = Feed($"{Esc}[4;5H{Esc}[?1048h{Esc}[1;1H{Esc}[?1048l");

        Assert.Equal(3, terminal.Cursor.Row);
        Assert.Equal(4, terminal.Cursor.Column);
    }

    [Fact]
    public void ResetReturnsEveryModeToItsDefault()
    {
        var terminal = Feed($"{Esc}[?1049h{Esc}[?6h{Esc}[4h{Esc}[?2004h{Esc}(0{Esc}c");

        Assert.False(terminal.IsAlternateScreenActive);
        Assert.False(terminal.OriginMode);
        Assert.False(terminal.InsertMode);
        Assert.False(terminal.BracketedPaste);
        Assert.Equal(CharacterSet.UsAscii, terminal.G0CharacterSet);
    }

    private static TerminalState Feed(string input)
    {
        var terminal = new TerminalState(new TerminalSize(40, 10));
        Feed(terminal, input);
        return terminal;
    }

    private static void Feed(TerminalState terminal, string input)
        => new AnsiParser(new TerminalInterpreter(terminal)).Parse(Encoding.UTF8.GetBytes(input));
}
