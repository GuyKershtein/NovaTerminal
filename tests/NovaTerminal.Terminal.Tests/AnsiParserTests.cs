using System.Text;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.Terminal.Tests;

public sealed class AnsiParserTests
{
    // Written as \u001b rather than \x1b on purpose: \x takes a variable number of hex digits, so
    // "\x1bD" would silently be the single character U+01BD instead of ESC followed by 'D'.
    private const string Esc = "\u001b";

    [Fact]
    public void PlainText_IsPrintedVerbatim()
    {
        var handler = Parse("hello world");

        Assert.Equal("hello world", handler.Text);
    }

    [Fact]
    public void ControlCharacters_AreExecutedNotPrinted()
    {
        var handler = Parse("a\r\nb");

        Assert.Equal("ab", handler.Text);
        Assert.Contains("execute:0x0D", handler.Events, StringComparer.Ordinal);
        Assert.Contains("execute:0x0A", handler.Events, StringComparer.Ordinal);
    }

    [Fact]
    public void Delete_IsIgnored()
    {
        var handler = Parse("a\u007fb");

        Assert.Equal("ab", handler.Text);
    }

    [Fact]
    public void SimpleControlSequence_IsDispatchedWithItsFinalByte()
    {
        var handler = Parse($"{Esc}[H");

        var sequence = Assert.Single(handler.CsiSequences);
        Assert.Equal('H', sequence.Final);
        Assert.Empty(sequence.Parameters);
    }

    [Fact]
    public void MultipleParameters_AreParsedInOrder()
    {
        var handler = Parse($"{Esc}[1;22;333m");

        var sequence = Assert.Single(handler.CsiSequences);
        Assert.Equal([1, 22, 333], sequence.Parameters);
        Assert.Equal('m', sequence.Final);
    }

    [Fact]
    public void OmittedParameters_BecomeZero()
    {
        // "CSI ;5H" means "default row, column five"; the engine applies the convention that a
        // missing or zero parameter means one.
        var handler = Parse($"{Esc}[;5H");

        var sequence = Assert.Single(handler.CsiSequences);
        Assert.Equal([0, 5], sequence.Parameters);
    }

    [Fact]
    public void TrailingSeparator_ProducesATrailingDefaultParameter()
    {
        var handler = Parse($"{Esc}[1;m");

        Assert.Equal([1, 0], Assert.Single(handler.CsiSequences).Parameters);
    }

    [Fact]
    public void PrivateMarker_IsCaptured()
    {
        var handler = Parse($"{Esc}[?25h");

        var sequence = Assert.Single(handler.CsiSequences);
        Assert.Equal('?', sequence.PrivateMarker);
        Assert.Equal([25], sequence.Parameters);
        Assert.Equal('h', sequence.Final);
    }

    [Fact]
    public void IntermediateByte_IsCaptured()
    {
        // DECSCUSR: "CSI Ps SP q" selects the cursor shape.
        var handler = Parse($"{Esc}[2 q");

        var sequence = Assert.Single(handler.CsiSequences);
        Assert.Equal(' ', sequence.Intermediate);
        Assert.Equal('q', sequence.Final);
    }

    [Fact]
    public void SubParameters_AreDistinguishedFromParameters()
    {
        // The colon form is what ECMA-48 intended for compound values; both forms mean red.
        var handler = Parse($"{Esc}[38:2::255:0:0m");

        var sequence = Assert.Single(handler.CsiSequences);
        Assert.Equal([38, 2, 0, 255, 0, 0], sequence.Parameters);
        Assert.Equal([false, true, true, true, true, true], sequence.SubParameters);
    }

    [Fact]
    public void EscapeSequences_AreDispatched()
    {
        var handler = Parse($"{Esc}M{Esc}7{Esc}=");

        Assert.Equal(["esc:M", "esc:7", "esc:="], handler.Events);
    }

    [Fact]
    public void EscapeSequenceWithIntermediate_KeepsTheIntermediate()
    {
        var handler = Parse($"{Esc}(B");

        Assert.Equal(["esc:(B"], handler.Events);
    }

    [Fact]
    public void OperatingSystemCommand_IsDeliveredWhenTerminatedByBell()
    {
        var handler = Parse($"{Esc}]0;My Title\u0007");

        Assert.Equal(["osc:0;My Title"], handler.Events);
    }

    [Fact]
    public void OperatingSystemCommand_IsDeliveredWhenTerminatedByStringTerminator()
    {
        var handler = Parse($"{Esc}]2;Another{Esc}\\rest");

        Assert.Contains("osc:2;Another", handler.Events, StringComparer.Ordinal);
        Assert.Equal("rest", handler.Text);
    }

    // ----- Sequences split across reads: the reason this is a state machine at all. -----

    [Fact]
    public void ASequenceSplitAcrossChunks_ParsesIdentically()
    {
        var whole = Parse($"{Esc}[31mHello");
        var split = ParseChunks($"{Esc}[3", "1mHello");

        Assert.Equal("Hello", split.Text);
        Assert.Equal(whole.Events, split.Events);
    }

    [Fact]
    public void ASequenceSplitAtEveryPossiblePoint_ParsesIdentically()
    {
        // Rather than trusting one split point, try them all: a state machine that is correct only
        // for the boundaries someone thought to test is not correct.
        const string Input = $"{Esc}[1;31mRed{Esc}[0m plain{Esc}[?25l";
        var expected = Parse(Input).Events;

        for (var split = 1; split < Input.Length; split++)
        {
            var actual = ParseChunks(Input[..split], Input[split..]).Events;

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ASequenceSplitOneByteAtATime_ParsesIdentically()
    {
        const string Input = $"{Esc}[38;2;12;34;56mX{Esc}]0;t\u0007";
        var expected = Parse(Input).Events;

        var chunks = Input.Select(character => character.ToString()).ToArray();

        Assert.Equal(expected, ParseChunks(chunks).Events);
    }

    [Fact]
    public void ParserStateSurvivesAnEmptyRead()
    {
        var handler = ParseChunks($"{Esc}[", string.Empty, "31m", string.Empty, "x");

        Assert.Equal("x", handler.Text);
        Assert.Single(handler.CsiSequences);
    }

    // ----- UTF-8 -----

    [Fact]
    public void MultiByteCharacters_AreDecoded()
    {
        var handler = ParseBytes(Encoding.UTF8.GetBytes("aé中\U0001F600"));

        Assert.Equal("aé中\U0001F600", handler.Text);
    }

    [Fact]
    public void AMultiByteCharacterSplitAcrossChunks_IsDecoded()
    {
        // A three-byte character can straddle a read boundary just as an escape sequence can.
        var bytes = Encoding.UTF8.GetBytes("中");
        var handler = new RecordingOutputHandler();
        var parser = new AnsiParser(handler);

        parser.Parse(bytes.AsSpan(0, 1));
        parser.Parse(bytes.AsSpan(1, 1));
        parser.Parse(bytes.AsSpan(2));

        Assert.Equal("中", handler.Text);
    }

    [Theory]
    [InlineData(new byte[] { 0x80 })]                      // Stray continuation byte.
    [InlineData(new byte[] { 0xC0, 0x80 })]                // Overlong encoding of NUL.
    [InlineData(new byte[] { 0xE0, 0x80, 0x80 })]          // Overlong encoding.
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]          // A surrogate half, which is not a scalar.
    [InlineData(new byte[] { 0xF5, 0x80, 0x80, 0x80 })]    // Beyond the Unicode range.
    public void InvalidUtf8_BecomesAReplacementCharacter(byte[] bytes)
    {
        // Accepting overlong or surrogate encodings is a classic way to smuggle characters past
        // anything inspecting the stream, so they are rejected rather than decoded leniently.
        var handler = ParseBytes(bytes);

        Assert.Contains("�", handler.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIncompleteCharacterAtTheEndOfAReadIsHeldRatherThanRejected()
    {
        // A lone lead byte is not an error: the rest of the character may be in the next read.
        // Rejecting it here would break every multi-byte character that straddles a chunk boundary.
        var handler = new RecordingOutputHandler();
        var parser = new AnsiParser(handler);

        parser.Parse([0xC3]);
        Assert.Equal(string.Empty, handler.Text);

        parser.Parse([0xA9]);
        Assert.Equal("é", handler.Text);
    }

    [Fact]
    public void AnInterruptedMultiByteCharacter_DoesNotSwallowWhatFollows()
    {
        var handler = ParseBytes([0xE4, 0xB8, (byte)'A']);

        Assert.Equal("�A", handler.Text);
    }

    [Fact]
    public void AnEscapeInterruptingAMultiByteCharacter_StillStartsASequence()
    {
        var handler = ParseBytes([0xE4, 0xB8, 0x1B, (byte)'[', (byte)'H']);

        Assert.Equal("�", handler.Text);
        Assert.Single(handler.CsiSequences);
    }

    // ----- Malformed and hostile input -----

    [Fact]
    public void AMalformedSequence_IsDiscardedWithoutSwallowingLaterText()
    {
        // The private marker is only legal before the parameters. The sequence is abandoned, but
        // recovery happens exactly at its final byte so the text after it still prints.
        var handler = Parse($"{Esc}[1?2mtext");

        Assert.Empty(handler.CsiSequences);
        Assert.Equal("text", handler.Text);
    }

    [Fact]
    public void CancelAbandonsASequenceInProgress()
    {
        var handler = Parse($"{Esc}[31\u0018mtext");

        Assert.Empty(handler.CsiSequences);
        Assert.Equal("mtext", handler.Text);
    }

    [Fact]
    public void AnEscapeInsideASequence_RestartsIt()
    {
        var handler = Parse($"{Esc}[31{Esc}[1mtext");

        var sequence = Assert.Single(handler.CsiSequences);
        Assert.Equal([1], sequence.Parameters);
        Assert.Equal("text", handler.Text);
    }

    [Fact]
    public void ParameterValues_AreClampedRatherThanOverflowing()
    {
        // A parameter is used for loop bounds and allocation sizes; a value that wrapped negative
        // would be far worse than one that saturates.
        var handler = Parse($"{Esc}[99999999999999999999m");

        Assert.Equal([VtConstants.MaxParameterValue], Assert.Single(handler.CsiSequences).Parameters);
    }

    [Fact]
    public void ParameterCount_IsBounded()
    {
        var parameters = string.Join(";", Enumerable.Repeat("1", VtConstants.MaxParameterCount * 4));
        var handler = Parse($"{Esc}[{parameters}m");

        var sequence = Assert.Single(handler.CsiSequences);
        Assert.Equal(VtConstants.MaxParameterCount, sequence.Parameters.Length);
    }

    [Fact]
    public void StringPayloads_AreBounded()
    {
        var title = new string('x', AnsiParser.MaxStringLength * 2);
        var handler = Parse($"{Esc}]0;{title}\u0007");

        var record = Assert.Single(handler.Events);
        Assert.True(record.Length <= AnsiParser.MaxStringLength + "osc:".Length);
    }

    [Fact]
    public void DeviceControlStrings_AreConsumedNotExecuted()
    {
        // Without a passthrough state the payload would be interpreted as commands, which is how a
        // hostile stream could smuggle sequences past a naive parser.
        var handler = Parse($"{Esc}Pq#0;2;0;0;0{Esc}\\after");

        Assert.Equal("after", handler.Text);
        Assert.Empty(handler.CsiSequences);
    }

    [Fact]
    public void ApplicationProgramCommands_AreConsumed()
    {
        var handler = Parse($"{Esc}_anything at all{Esc}\\visible");

        Assert.Equal("visible", handler.Text);
    }

    [Fact]
    public void Reset_DiscardsAPartiallyReceivedSequence()
    {
        var handler = new RecordingOutputHandler();
        var parser = new AnsiParser(handler);

        parser.Parse("\u001b[31"u8);
        parser.Reset();
        parser.Parse("m"u8);

        Assert.Empty(handler.CsiSequences);
        Assert.Equal("m", handler.Text);
    }

    private static RecordingOutputHandler Parse(string input) => ParseChunks(input);

    private static RecordingOutputHandler ParseChunks(params string[] chunks)
    {
        var handler = new RecordingOutputHandler();
        var parser = new AnsiParser(handler);

        foreach (var chunk in chunks)
        {
            parser.Parse(Encoding.UTF8.GetBytes(chunk));
        }

        return handler;
    }

    private static RecordingOutputHandler ParseBytes(byte[] bytes)
    {
        var handler = new RecordingOutputHandler();
        new AnsiParser(handler).Parse(bytes);
        return handler;
    }
}
