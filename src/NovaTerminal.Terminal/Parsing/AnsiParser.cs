using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NovaTerminal.Terminal.Parsing;

/// <summary>
/// Turns a raw terminal byte stream into printable characters and control functions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a state machine.</b> Bytes arrive from a pseudo-terminal in whatever chunks the operating
/// system chooses. The sequence <c>ESC [ 3 1 m</c> may arrive complete, or as <c>ESC</c> then
/// <c>[3</c> then <c>1m</c> across three reads milliseconds apart. A parser that scanned a buffer
/// for complete sequences would therefore be wrong; this one keeps its state between calls, so a
/// half-received sequence is simply a state it is sitting in.
/// </para>
/// <para>
/// <b>The model.</b> This follows Paul Williams' VT500 parser state diagram, the same model used by
/// xterm and by most modern emulators. The states that look redundant are the ones that make
/// malformed input safe: <see cref="ParserState.CsiIgnore"/> consumes a bad sequence up to its
/// final byte and discards it, so one broken sequence cannot corrupt the text after it, and
/// <see cref="ParserState.DcsPassthrough"/> consumes device control payloads that would otherwise
/// be executed as if they were commands.
/// </para>
/// <para>
/// <b>One deliberate deviation.</b> The original diagram routes a colon to
/// <see cref="ParserState.CsiIgnore"/>. Modern terminals use colons for SGR sub-parameters, as in
/// <c>SGR 38:2::255:0:0</c> for true colour, so colons are recorded as sub-parameter separators
/// instead.
/// </para>
/// <para>
/// <b>Untrusted input.</b> Terminal output may come from a remote host or a hostile program. Every
/// unbounded quantity is bounded here: parameter values, parameter counts and string payload
/// lengths. No sequence can cause unbounded allocation, and no sequence does anything but change
/// terminal state.
/// </para>
/// <para>
/// <b>Threading.</b> An instance is not thread-safe. One pump loop owns a parser and feeds it in
/// order, which is the only ordering that makes sense for a stream anyway.
/// </para>
/// </remarks>
public sealed class AnsiParser
{
    /// <summary>Largest OSC payload retained. Titles are short; this bounds a hostile stream.</summary>
    public const int MaxStringLength = 4096;

    private const byte ParameterDigitLow = (byte)'0';
    private const byte ParameterDigitHigh = (byte)'9';
    private const byte IntermediateLow = 0x20;
    private const byte IntermediateHigh = 0x2F;
    private const byte PrivateMarkerLow = 0x3C;
    private const byte PrivateMarkerHigh = 0x3F;
    private const byte FinalByteLow = 0x40;
    private const byte FinalByteHigh = 0x7E;

    private const int ReplacementCharacter = 0xFFFD;
    private const int MaxIntermediates = 1;

    private readonly ITerminalOutputHandler _handler;
    private readonly ILogger _logger;

    private readonly int[] _parameters = new int[VtConstants.MaxParameterCount];
    private readonly bool[] _subParameterFlags = new bool[VtConstants.MaxParameterCount];
    private readonly byte[] _stringBuffer = new byte[MaxStringLength];

    private ParserState _state = ParserState.Ground;
    private int _parameterCount;
    private bool _parametersTruncated;
    private char _privateMarker = CsiSequence.None;
    private char _intermediate = CsiSequence.None;
    private int _intermediateCount;
    private int _stringLength;
    private bool _stringTruncated;

    private int _utf8Pending;
    private int _utf8CodePoint;
    private int _utf8Length;

    /// <summary>Creates a parser that reports to <paramref name="handler"/>.</summary>
    public AnsiParser(ITerminalOutputHandler handler, ILogger<AnsiParser>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handler = handler;
        _logger = logger ?? NullLogger<AnsiParser>.Instance;
    }

    /// <summary>
    /// Feeds bytes to the parser. Any sequence left incomplete at the end of
    /// <paramref name="data"/> is carried over to the next call.
    /// </summary>
    public void Parse(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            ProcessByte(value);
        }
    }

    /// <summary>
    /// Abandons any partially received sequence and returns to the ground state. Used when a
    /// session is reset, so that a half-parsed sequence cannot bleed into a fresh stream.
    /// </summary>
    public void Reset()
    {
        _state = ParserState.Ground;
        ClearSequence();
        ResetUtf8();
    }

    private void ProcessByte(byte value)
    {
        // A multi-byte character interrupted by anything other than a continuation byte is
        // truncated. Emit a replacement character and let the interrupting byte be handled below.
        if (_utf8Pending > 0 && !IsUtf8Continuation(value))
        {
            EmitReplacement();
            ResetUtf8();
        }

        // These two abort whatever is in progress, from any state.
        if (value is VtConstants.Cancel or VtConstants.Substitute)
        {
            AbandonSequence();
            return;
        }

        if (value == VtConstants.Escape)
        {
            BeginEscape();
            return;
        }

        switch (_state)
        {
            case ParserState.Ground:
                GroundByte(value);
                break;
            case ParserState.Escape:
                EscapeByte(value);
                break;
            case ParserState.EscapeIntermediate:
                EscapeIntermediateByte(value);
                break;
            case ParserState.CsiEntry:
                CsiEntryByte(value);
                break;
            case ParserState.CsiParam:
                CsiParamByte(value);
                break;
            case ParserState.CsiIntermediate:
                CsiIntermediateByte(value);
                break;
            case ParserState.CsiIgnore:
                CsiIgnoreByte(value);
                break;
            case ParserState.OscString:
                OscStringByte(value);
                break;
            case ParserState.DcsEntry:
            case ParserState.DcsParam:
            case ParserState.DcsIntermediate:
                DcsPrologueByte(value);
                break;
            case ParserState.DcsPassthrough:
            case ParserState.DcsIgnore:
            case ParserState.SosPmApcString:
                // The payload is consumed and discarded; only the terminator matters, and ESC has
                // already been handled above.
                break;
            default:
                _state = ParserState.Ground;
                break;
        }
    }

    private void GroundByte(byte value)
    {
        if (_utf8Pending > 0)
        {
            ContinueUtf8(value);
            return;
        }

        if (VtConstants.IsC0Control(value))
        {
            _handler.Execute(value);
            return;
        }

        if (value < VtConstants.Delete)
        {
            _handler.Print(new Rune((char)value));
            return;
        }

        if (value == VtConstants.Delete)
        {
            // DEL is a no-op on a video terminal; it exists so paper tape could be corrected.
            return;
        }

        BeginUtf8(value);
    }

    private void EscapeByte(byte value)
    {
        if (VtConstants.IsC0Control(value))
        {
            _handler.Execute(value);
            return;
        }

        switch (value)
        {
            case VtConstants.CsiIntroducer:
                _state = ParserState.CsiEntry;
                return;
            case VtConstants.OscIntroducer:
                _state = ParserState.OscString;
                return;
            case VtConstants.DcsIntroducer:
                _state = ParserState.DcsEntry;
                return;
            case (byte)'X': // SOS
            case (byte)'^': // PM
            case (byte)'_': // APC
                _state = ParserState.SosPmApcString;
                return;
            case VtConstants.Delete:
                return;
        }

        if (value is >= IntermediateLow and <= IntermediateHigh)
        {
            CollectIntermediate(value);
            _state = ParserState.EscapeIntermediate;
            return;
        }

        DispatchEscape((char)value);
    }

    private void EscapeIntermediateByte(byte value)
    {
        if (VtConstants.IsC0Control(value))
        {
            _handler.Execute(value);
            return;
        }

        if (value is >= IntermediateLow and <= IntermediateHigh)
        {
            CollectIntermediate(value);
            return;
        }

        if (value == VtConstants.Delete)
        {
            return;
        }

        DispatchEscape((char)value);
    }

    private void CsiEntryByte(byte value)
    {
        if (VtConstants.IsC0Control(value))
        {
            _handler.Execute(value);
            return;
        }

        if (value is >= PrivateMarkerLow and <= PrivateMarkerHigh)
        {
            _privateMarker = (char)value;
            _state = ParserState.CsiParam;
            return;
        }

        CsiParamByte(value);
    }

    private void CsiParamByte(byte value)
    {
        if (VtConstants.IsC0Control(value))
        {
            _handler.Execute(value);
            return;
        }

        if (value is >= ParameterDigitLow and <= ParameterDigitHigh)
        {
            AppendParameterDigit(value);
            _state = ParserState.CsiParam;
            return;
        }

        switch (value)
        {
            case VtConstants.ParameterSeparator:
                BeginParameter(isSubParameter: false);
                _state = ParserState.CsiParam;
                return;
            case VtConstants.SubParameterSeparator:
                BeginParameter(isSubParameter: true);
                _state = ParserState.CsiParam;
                return;
            case VtConstants.Delete:
                return;
        }

        if (value is >= IntermediateLow and <= IntermediateHigh)
        {
            CollectIntermediate(value);
            _state = _intermediateCount > MaxIntermediates ? ParserState.CsiIgnore : ParserState.CsiIntermediate;
            return;
        }

        if (value is >= FinalByteLow and <= FinalByteHigh)
        {
            DispatchCsi((char)value);
            return;
        }

        // A private marker after the parameters have started, or any other stray byte.
        _state = ParserState.CsiIgnore;
    }

    private void CsiIntermediateByte(byte value)
    {
        if (VtConstants.IsC0Control(value))
        {
            _handler.Execute(value);
            return;
        }

        if (value is >= IntermediateLow and <= IntermediateHigh)
        {
            CollectIntermediate(value);
            if (_intermediateCount > MaxIntermediates)
            {
                _state = ParserState.CsiIgnore;
            }

            return;
        }

        if (value is >= FinalByteLow and <= FinalByteHigh)
        {
            DispatchCsi((char)value);
            return;
        }

        // Parameters are not allowed after intermediates.
        _state = ParserState.CsiIgnore;
    }

    private void CsiIgnoreByte(byte value)
    {
        if (VtConstants.IsC0Control(value))
        {
            _handler.Execute(value);
            return;
        }

        if (value is >= FinalByteLow and <= FinalByteHigh)
        {
            // The sequence ends here and is discarded. Recovering exactly at the final byte is what
            // keeps a malformed sequence from swallowing the text that follows it.
            ClearSequence();
            _state = ParserState.Ground;
        }
    }

    private void OscStringByte(byte value)
    {
        if (value == VtConstants.Bel)
        {
            DispatchOsc();
            return;
        }

        if (VtConstants.IsC0Control(value))
        {
            return;
        }

        if (_stringLength < _stringBuffer.Length)
        {
            _stringBuffer[_stringLength++] = value;
        }
        else
        {
            _stringTruncated = true;
        }
    }

    private void DcsPrologueByte(byte value)
    {
        // Parameters and intermediates of a device control string are consumed but unused: no DCS
        // function is implemented. What matters is reaching the payload state so the payload is
        // not mistaken for commands.
        if (value is >= FinalByteLow and <= FinalByteHigh)
        {
            _state = ParserState.DcsPassthrough;
        }
    }

    private void BeginEscape()
    {
        // ESC terminates a string in progress: "ESC \" is the standard string terminator.
        if (_state == ParserState.OscString)
        {
            DispatchOsc();
        }

        ClearSequence();
        ResetUtf8();
        _state = ParserState.Escape;
    }

    private void AbandonSequence()
    {
        // CAN and SUB mean "forget the sequence in progress". Any partial sequence is discarded
        // rather than dispatched, which is exactly what a sender uses them for.
        ClearSequence();
        ResetUtf8();
        _state = ParserState.Ground;
    }

    private void CollectIntermediate(byte value)
    {
        _intermediateCount++;

        if (_intermediateCount == 1)
        {
            _intermediate = (char)value;
        }
    }

    private void BeginParameter(bool isSubParameter)
    {
        // A separator with nothing before it means an omitted leading parameter, as in "CSI ;5m".
        if (_parameterCount == 0)
        {
            AppendParameterSlot(isSubParameter: false);
        }

        AppendParameterSlot(isSubParameter);
    }

    private void AppendParameterSlot(bool isSubParameter)
    {
        if (_parameterCount >= _parameters.Length)
        {
            _parametersTruncated = true;
            return;
        }

        _parameters[_parameterCount] = 0;
        _subParameterFlags[_parameterCount] = isSubParameter;
        _parameterCount++;
    }

    private void AppendParameterDigit(byte value)
    {
        if (_parameterCount == 0)
        {
            AppendParameterSlot(isSubParameter: false);
        }

        if (_parameterCount == 0)
        {
            return;
        }

        var index = _parameterCount - 1;

        // Saturating rather than overflowing: the value comes from untrusted output and is used
        // for loop bounds, so a wrapped negative would be far worse than a clamped maximum.
        var next = (_parameters[index] * 10L) + (value - ParameterDigitLow);
        _parameters[index] = (int)Math.Min(next, VtConstants.MaxParameterValue);
    }

    private void DispatchCsi(char final)
    {
        if (_parametersTruncated)
        {
            _logger.ParametersTruncated(final, VtConstants.MaxParameterCount);
        }

        var sequence = new CsiSequence(
            _parameters.AsSpan(0, _parameterCount),
            _subParameterFlags.AsSpan(0, _parameterCount),
            _privateMarker,
            _intermediate,
            final);

        _handler.CsiDispatch(in sequence);

        ClearSequence();
        _state = ParserState.Ground;
    }

    private void DispatchEscape(char final)
    {
        _handler.EscapeDispatch(final, _intermediate);
        ClearSequence();
        _state = ParserState.Ground;
    }

    private void DispatchOsc()
    {
        if (_stringTruncated)
        {
            _logger.StringTruncated(MaxStringLength);
        }

        _handler.OscDispatch(_stringBuffer.AsSpan(0, _stringLength));

        ClearSequence();
        _state = ParserState.Ground;
    }

    private void ClearSequence()
    {
        _parameterCount = 0;
        _parametersTruncated = false;
        _privateMarker = CsiSequence.None;
        _intermediate = CsiSequence.None;
        _intermediateCount = 0;
        _stringLength = 0;
        _stringTruncated = false;
    }

    private static bool IsUtf8Continuation(byte value) => (value & 0xC0) == 0x80;

    private void BeginUtf8(byte value)
    {
        // Lead byte shapes: 110xxxxx, 1110xxxx, 11110xxx. Anything else is not a valid start.
        if ((value & 0xE0) == 0xC0 && value >= 0xC2)
        {
            _utf8CodePoint = value & 0x1F;
            _utf8Pending = 1;
        }
        else if ((value & 0xF0) == 0xE0)
        {
            _utf8CodePoint = value & 0x0F;
            _utf8Pending = 2;
        }
        else if ((value & 0xF8) == 0xF0 && value <= 0xF4)
        {
            _utf8CodePoint = value & 0x07;
            _utf8Pending = 3;
        }
        else
        {
            // A stray continuation byte, or an encoding that has never been legal (0xC0, 0xC1,
            // 0xF5 and above). Emit a replacement character rather than guessing.
            EmitReplacement();
            return;
        }

        _utf8Length = _utf8Pending + 1;
    }

    private void ContinueUtf8(byte value)
    {
        _utf8CodePoint = (_utf8CodePoint << 6) | (value & 0x3F);
        _utf8Pending--;

        if (_utf8Pending > 0)
        {
            return;
        }

        var codePoint = _utf8CodePoint;
        var length = _utf8Length;
        ResetUtf8();

        if (IsValidScalar(codePoint, length))
        {
            _handler.Print(new Rune(codePoint));
        }
        else
        {
            // Overlong encodings and surrogate halves are rejected: accepting them is a
            // well-known way to smuggle characters past filters that inspect the stream.
            EmitReplacement();
        }
    }

    private static bool IsValidScalar(int codePoint, int length)
    {
        if (codePoint > 0x10FFFF)
        {
            return false;
        }

        if (codePoint is >= 0xD800 and <= 0xDFFF)
        {
            return false;
        }

        var minimum = length switch
        {
            2 => 0x80,
            3 => 0x800,
            4 => 0x10000,
            _ => 0,
        };

        return codePoint >= minimum;
    }

    private void EmitReplacement() => _handler.Print(new Rune(ReplacementCharacter));

    private void ResetUtf8()
    {
        _utf8Pending = 0;
        _utf8CodePoint = 0;
        _utf8Length = 0;
    }
}
