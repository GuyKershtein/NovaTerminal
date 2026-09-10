using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.Terminal.Tests;

/// <summary>
/// End-to-end tests: bytes in, screen state out. This is the whole engine behaving as a terminal.
/// </summary>
public sealed class TerminalInterpreterTests
{
    private const string Esc = "";

    [Fact]
    public void ColouredText_IsPrintedWithTheColourApplied()
    {
        var terminal = Feed($"{Esc}[31mHello{Esc}[0m");

        Assert.Equal("Hello", terminal.Buffer.GetLineText(0));

        for (var column = 0; column < 5; column++)
        {
            Assert.Equal(TerminalColor.FromAnsi(AnsiColor.Red), terminal.Buffer[column, 0].Foreground);
        }

        // The reset must take effect for whatever is printed next, not retroactively.
        Assert.Equal(CellStyle.Default, terminal.CurrentStyle);
    }

    [Fact]
    public void ColouredText_ArrivingInChunksProducesTheSameScreen()
    {
        // The example that motivates the whole design: the same bytes, delivered differently.
        var whole = Feed($"{Esc}[31mHello{Esc}[0m");
        var split = FeedChunks($"{Esc}[3", "1mHel", "lo", $"{Esc}[", "0m");

        Assert.Equal(whole.Buffer.GetText(), split.Buffer.GetText());
        Assert.Equal(whole.Buffer[0, 0].Foreground, split.Buffer[0, 0].Foreground);
        Assert.Equal(TerminalColor.FromAnsi(AnsiColor.Red), split.Buffer[0, 0].Foreground);
    }

    [Fact]
    public void Attributes_Accumulate()
    {
        var terminal = Feed($"{Esc}[1m{Esc}[4m{Esc}[31mX");

        var style = terminal.Buffer[0, 0].Style;
        Assert.True(style.HasAttributes(TextAttributes.Bold | TextAttributes.Underline));
        Assert.Equal(TerminalColor.FromAnsi(AnsiColor.Red), style.Foreground);
    }

    [Theory]
    [InlineData("[38;5;200m", 200)]
    [InlineData("[38:5:200m", 200)]
    public void ExtendedIndexedColour_IsAcceptedInBothSpellings(string sequence, int expectedIndex)
    {
        var terminal = Feed(sequence + "X");

        Assert.Equal(TerminalColor.FromIndex((byte)expectedIndex), terminal.Buffer[0, 0].Foreground);
    }

    [Theory]
    [InlineData("[38;2;10;20;30m")]
    [InlineData("[38:2:10:20:30m")]
    [InlineData("[38:2::10:20:30m")]
    public void TrueColour_IsAcceptedInAllThreeSpellings(string sequence)
    {
        var terminal = Feed(sequence + "X");

        Assert.Equal(TerminalColor.FromRgb(10, 20, 30), terminal.Buffer[0, 0].Foreground);
    }

    [Fact]
    public void BrightColours_MapToTheUpperPaletteEntries()
    {
        var terminal = Feed($"{Esc}[91mX{Esc}[104mY");

        Assert.Equal(TerminalColor.FromAnsi(AnsiColor.BrightRed), terminal.Buffer[0, 0].Foreground);
        Assert.Equal(TerminalColor.FromAnsi(AnsiColor.BrightBlue), terminal.Buffer[1, 0].Background);
    }

    [Fact]
    public void CursorPosition_UsesOneBasedRowThenColumn()
    {
        // The classic off-by-one: the protocol counts from one and puts the row first, while the
        // engine counts from zero and takes the column first.
        var terminal = Feed($"{Esc}[3;5H");

        Assert.Equal(4, terminal.Cursor.Column);
        Assert.Equal(2, terminal.Cursor.Row);
    }

    [Fact]
    public void CursorPositionWithoutParameters_HomesTheCursor()
    {
        var terminal = Feed($"{Esc}[5;5H{Esc}[H");

        Assert.Equal(0, terminal.Cursor.Column);
        Assert.Equal(0, terminal.Cursor.Row);
    }

    [Fact]
    public void RelativeCursorMovement_Works()
    {
        var terminal = Feed($"{Esc}[10;10H{Esc}[2A{Esc}[3C{Esc}[1B{Esc}[4D");

        Assert.Equal(8, terminal.Cursor.Column);
        Assert.Equal(8, terminal.Cursor.Row);
    }

    [Fact]
    public void EraseInLine_ClearsFromTheCursor()
    {
        var terminal = Feed($"abcdefghij{Esc}[5G{Esc}[K");

        Assert.Equal("abcd", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void EraseInDisplay_ClearsTheScreen()
    {
        var terminal = Feed($"one\r\ntwo\r\nthree{Esc}[2J");

        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(0));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(2));
    }

    [Fact]
    public void EraseUsesTheCurrentBackground()
    {
        var terminal = Feed($"{Esc}[44m{Esc}[2J");

        Assert.Equal(TerminalColor.FromAnsi(AnsiColor.Blue), terminal.Buffer[0, 0].Background);
    }

    [Fact]
    public void ScrollingRegion_IsHonoured()
    {
        var terminal = Feed($"{Esc}[2;4r{Esc}[4;1Hlast\n");

        Assert.Equal(new ScrollRegion(1, 3), terminal.ScrollRegion);
    }

    [Fact]
    public void InsertAndDeleteLines_Work()
    {
        var terminal = Feed($"one\r\ntwo\r\nthree{Esc}[2;1H{Esc}[L");

        Assert.Equal("one", terminal.Buffer.GetLineText(0));
        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(1));
        Assert.Equal("two", terminal.Buffer.GetLineText(2));
    }

    [Fact]
    public void InsertAndDeleteCharacters_Work()
    {
        var terminal = Feed($"abcdef{Esc}[3G{Esc}[2P");

        Assert.Equal("abef", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void SaveAndRestoreCursor_RoundTrip()
    {
        var terminal = Feed($"{Esc}[5;7H{Esc}7{Esc}[1;1H{Esc}8");

        Assert.Equal(6, terminal.Cursor.Column);
        Assert.Equal(4, terminal.Cursor.Row);
    }

    [Fact]
    public void PrivateModes_ToggleAutoWrapAndCursorVisibility()
    {
        var terminal = Feed($"{Esc}[?25l{Esc}[?7l");

        Assert.False(terminal.Cursor.IsVisible);
        Assert.False(terminal.AutoWrap);

        Feed(terminal, $"{Esc}[?25h{Esc}[?7h");

        Assert.True(terminal.Cursor.IsVisible);
        Assert.True(terminal.AutoWrap);
    }

    [Fact]
    public void ApplicationCursorKeys_AreRecordedForTheInputLayer()
    {
        var terminal = Feed($"{Esc}[?1h");

        Assert.True(terminal.ApplicationCursorKeys);
    }

    [Fact]
    public void ApplicationKeypad_IsRecordedForTheInputLayer()
    {
        var terminal = Feed($"{Esc}={Esc}[H");

        Assert.True(terminal.ApplicationKeypad);
    }

    [Fact]
    public void ReverseIndexAtTheTop_ScrollsDown()
    {
        var terminal = Feed($"one{Esc}[1;1H{Esc}M");

        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(0));
        Assert.Equal("one", terminal.Buffer.GetLineText(1));
    }

    [Fact]
    public void FullReset_ClearsEverything()
    {
        var terminal = Feed($"{Esc}[31mtext{Esc}c");

        Assert.Equal(string.Empty, terminal.Buffer.GetLineText(0));
        Assert.Equal(CellStyle.Default, terminal.CurrentStyle);
        Assert.Equal(0, terminal.Cursor.Row);
    }

    [Fact]
    public void TabStops_CanBeSetAndCleared()
    {
        var terminal = Feed($"{Esc}[1;3H{Esc}H{Esc}[1;1H\t");

        // A stop was placed at column three (one-based), so the tab lands there rather than at
        // the default eight.
        Assert.Equal(2, terminal.Cursor.Column);
    }

    [Fact]
    public void WindowTitle_IsReported()
    {
        var titles = new List<string>();
        var terminal = new TerminalState(new TerminalSize(40, 6));
        var interpreter = new TerminalInterpreter(terminal);
        interpreter.TitleChanged += (_, title) => titles.Add(title);
        var parser = new AnsiParser(interpreter);

        parser.Parse(Encoding.UTF8.GetBytes($"{Esc}]0;NovaTerminal — 中文\u0007"));

        Assert.Equal(["NovaTerminal — 中文"], titles);
    }

    [Fact]
    public void Bell_IsReported()
    {
        var rung = 0;
        var terminal = new TerminalState(new TerminalSize(40, 6));
        var interpreter = new TerminalInterpreter(terminal);
        interpreter.BellRequested += (_, _) => rung++;
        new AnsiParser(interpreter).Parse("a\u0007b"u8);

        Assert.Equal(1, rung);
        Assert.Equal("ab", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void CursorPositionReport_AnswersWithOneBasedCoordinates()
    {
        var responses = CaptureResponses($"{Esc}[3;7H{Esc}[6n");

        // The reply is formatted so a program can feed it straight back as a CUP sequence.
        Assert.Equal([$"{Esc}[3;7R"], responses);
    }

    [Fact]
    public void StatusReport_AnswersThatTheTerminalIsHealthy()
    {
        Assert.Equal([$"{Esc}[0n"], CaptureResponses($"{Esc}[5n"));
    }

    [Fact]
    public void DeviceAttributes_IdentifyTheTerminal()
    {
        Assert.Equal([$"{Esc}[?1;2c"], CaptureResponses($"{Esc}[c"));
    }

    [Fact]
    public void UnsupportedSequences_AreIgnoredWithoutDisturbingTheScreen()
    {
        // A terminal that broke on an unimplemented sequence would be useless, since new ones
        // appear constantly.
        var terminal = Feed($"a{Esc}[>4;2m{Esc}[?2004h{Esc}]52;c;SGVsbG8=\u0007b");

        Assert.Equal("ab", terminal.Buffer.GetLineText(0));
    }

    [Fact]
    public void ARealisticPromptSequence_RendersCorrectly()
    {
        // Roughly what a colour prompt plus a command and its output looks like on the wire.
        var terminal = Feed(
            $"{Esc}[?25l{Esc}[H{Esc}[2J" +
            $"{Esc}[1;32muser@host{Esc}[0m:{Esc}[1;34m~/src{Esc}[0m$ ls\r\n" +
            $"{Esc}[34mdocs{Esc}[0m  README.md\r\n" +
            $"{Esc}[?25h");

        Assert.Equal("user@host:~/src$ ls", terminal.Buffer.GetLineText(0));
        Assert.Equal("docs  README.md", terminal.Buffer.GetLineText(1));
        Assert.Equal(TerminalColor.FromAnsi(AnsiColor.Green), terminal.Buffer[0, 0].Foreground);
        Assert.True(terminal.Buffer[0, 0].Style.HasAttributes(TextAttributes.Bold));
        Assert.Equal(TerminalColor.FromAnsi(AnsiColor.Blue), terminal.Buffer[0, 1].Foreground);
        Assert.True(terminal.Cursor.IsVisible);
    }

    private static TerminalState Feed(string input) => FeedChunks(input);

    private static TerminalState FeedChunks(params string[] chunks)
    {
        var terminal = new TerminalState(new TerminalSize(40, 12));
        var parser = new AnsiParser(new TerminalInterpreter(terminal));

        foreach (var chunk in chunks)
        {
            parser.Parse(Encoding.UTF8.GetBytes(chunk));
        }

        return terminal;
    }

    private static void Feed(TerminalState terminal, string input)
        => new AnsiParser(new TerminalInterpreter(terminal)).Parse(Encoding.UTF8.GetBytes(input));

    private static List<string> CaptureResponses(string input)
    {
        var responses = new List<string>();
        var terminal = new TerminalState(new TerminalSize(40, 6));
        var interpreter = new TerminalInterpreter(terminal);
        interpreter.ResponseRequested += (_, data) => responses.Add(Encoding.ASCII.GetString(data.Span));

        new AnsiParser(interpreter).Parse(Encoding.UTF8.GetBytes(input));

        return responses;
    }
}
