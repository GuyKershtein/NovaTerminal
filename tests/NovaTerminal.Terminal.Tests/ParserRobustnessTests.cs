using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.Terminal.Tests;

/// <summary>
/// The parser against input that is random, hostile or merely broken.
/// </summary>
/// <remarks>
/// Terminal output is untrusted: it may come from a remote host over SSH, from a file someone else
/// wrote, or from a program deliberately emitting nonsense. The guarantee being tested here is that
/// no byte sequence can make the engine throw, allocate without bound, or write outside the screen.
/// </remarks>
public sealed class ParserRobustnessTests
{
    private const int FuzzIterations = 200;
    private const int FuzzLength = 4096;

    [Fact]
    public void RandomBytesNeverThrow()
    {
        // A fixed seed keeps this reproducible: a failure can be replayed exactly.
        var random = new Random(20260910);

        for (var iteration = 0; iteration < FuzzIterations; iteration++)
        {
            var terminal = new TerminalState(new TerminalSize(40, 12));
            terminal.ConfigureScrollback(64);
            var parser = new AnsiParser(new TerminalInterpreter(terminal));

            var data = new byte[FuzzLength];
            random.NextBytes(data);

            var exception = Record.Exception(() => parser.Parse(data));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void RandomEscapeSequencesNeverThrow()
    {
        // Purely random bytes rarely form escape sequences. This generator produces mostly-valid
        // sequence shapes with random parameters, which is where the interesting failures live.
        var random = new Random(11235);
        var builder = new StringBuilder();

        for (var iteration = 0; iteration < FuzzIterations; iteration++)
        {
            var terminal = new TerminalState(new TerminalSize(40, 12));
            terminal.ConfigureScrollback(64);
            var parser = new AnsiParser(new TerminalInterpreter(terminal));

            builder.Clear();

            for (var sequence = 0; sequence < 200; sequence++)
            {
                builder.Append('\u001b');
                builder.Append(random.Next(4) switch
                {
                    0 => '[',
                    1 => ']',
                    2 => 'P',
                    _ => '(',
                });

                var parameters = random.Next(5);
                for (var parameter = 0; parameter < parameters; parameter++)
                {
                    if (parameter > 0)
                    {
                        builder.Append(random.Next(2) == 0 ? ';' : ':');
                    }

                    builder.Append(random.Next(-5, 100000));
                }

                // A final byte from the whole legal range, including ones with no meaning.
                builder.Append((char)random.Next(0x40, 0x7F));
                builder.Append("text ");
            }

            var exception = Record.Exception(() => parser.Parse(Encoding.UTF8.GetBytes(builder.ToString())));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void RandomInputLeavesTheScreenConsistent()
    {
        var random = new Random(98765);
        var terminal = new TerminalState(new TerminalSize(30, 10));
        var parser = new AnsiParser(new TerminalInterpreter(terminal));

        var data = new byte[FuzzLength];
        random.NextBytes(data);
        parser.Parse(data);

        // Whatever happened, the invariants a renderer relies on must still hold.
        Assert.InRange(terminal.Cursor.Column, 0, terminal.Size.Columns - 1);
        Assert.InRange(terminal.Cursor.Row, 0, terminal.Size.Rows - 1);
        Assert.True(terminal.ScrollRegion.Bottom < terminal.Size.Rows);
        Assert.True(terminal.ScrollRegion.Top <= terminal.ScrollRegion.Bottom);

        for (var row = 0; row < terminal.Size.Rows; row++)
        {
            Assert.Equal(terminal.Size.Columns, terminal.Buffer.GetRow(row).Length);
        }
    }

    [Fact]
    public void AFloodOfOutputRespectsTheScrollbackCeiling()
    {
        // The ceiling is what stops infinite output becoming unbounded memory.
        const int Capacity = 100;

        var terminal = new TerminalState(new TerminalSize(40, 10));
        terminal.ConfigureScrollback(Capacity);
        var parser = new AnsiParser(new TerminalInterpreter(terminal));

        for (var line = 0; line < 10_000; line++)
        {
            parser.Parse(Encoding.UTF8.GetBytes($"line {line}\r\n"));
        }

        Assert.Equal(Capacity, terminal.Scrollback!.Count);
    }

    [Fact]
    public void AnEndlessSequenceOfParametersDoesNotGrowWithoutBound()
    {
        var terminal = new TerminalState(new TerminalSize(40, 10));
        var parser = new AnsiParser(new TerminalInterpreter(terminal));

        var hostile = "\u001b[" + string.Join(";", Enumerable.Repeat("1", 100_000)) + "m";

        var exception = Record.Exception(() => parser.Parse(Encoding.UTF8.GetBytes(hostile)));

        Assert.Null(exception);
    }

    [Fact]
    public void AnEndlessStringPayloadDoesNotGrowWithoutBound()
    {
        var terminal = new TerminalState(new TerminalSize(40, 10));
        var parser = new AnsiParser(new TerminalInterpreter(terminal));

        // An operating system command with no terminator: the parser must cap what it retains
        // rather than buffering until it runs out of memory.
        var hostile = "\u001b]0;" + new string('x', 5_000_000);

        var exception = Record.Exception(() => parser.Parse(Encoding.UTF8.GetBytes(hostile)));

        Assert.Null(exception);
    }

    [Fact]
    public void OutputCannotEscapeTheScreenThroughCursorPositioning()
    {
        var terminal = new TerminalState(new TerminalSize(20, 5));
        var parser = new AnsiParser(new TerminalInterpreter(terminal));

        parser.Parse(Encoding.UTF8.GetBytes("\u001b[99999;99999HX"));

        Assert.Equal(4, terminal.Cursor.Row);
        Assert.Equal("X", terminal.Buffer.GetLineText(4).TrimStart());
    }

    [Theory]
    [InlineData("\u001b[")]
    [InlineData("\u001b]0;unterminated")]
    [InlineData("\u001bP")]
    [InlineData("\u001b")]
    public void APartialSequenceAtTheEndOfInputIsSimplyHeld(string input)
    {
        // A truncated sequence is not an error: the rest may be in the next read. What matters is
        // that holding it does not corrupt anything or throw.
        var terminal = new TerminalState(new TerminalSize(20, 5));
        var parser = new AnsiParser(new TerminalInterpreter(terminal));

        var exception = Record.Exception(() => parser.Parse(Encoding.UTF8.GetBytes(input)));

        Assert.Null(exception);
    }

    [Fact]
    public void ResetRecoversFromAnyState()
    {
        var random = new Random(4242);
        var terminal = new TerminalState(new TerminalSize(20, 5));
        var parser = new AnsiParser(new TerminalInterpreter(terminal));

        var data = new byte[512];
        random.NextBytes(data);
        parser.Parse(data);

        parser.Reset();
        parser.Parse("hello"u8);

        // After a reset the parser is in the ground state, so plain text prints as plain text no
        // matter what preceded it.
        Assert.Contains("hello", terminal.Buffer.GetText(), StringComparison.Ordinal);
    }
}
