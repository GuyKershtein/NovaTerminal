using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovaTerminal.Core;

namespace NovaTerminal.Terminal.Parsing;

/// <summary>
/// Carries out the control functions the parser recognises, driving a <see cref="TerminalState"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is where escape sequences stop being syntax and start being meaning. It is the only place
/// that knows, for instance, that <c>CSI 5;10H</c> means row five column ten, one-based, or that
/// erasing uses the current background colour.
/// </para>
/// <para>
/// Some sequences are questions rather than commands: <c>CSI 6n</c> asks where the cursor is, and
/// the terminal is expected to write an answer back to the shell as though the user had typed it.
/// Those answers are surfaced through <see cref="ResponseRequested"/> rather than written directly,
/// because this class has no business knowing what a pseudo-terminal is.
/// </para>
/// </remarks>
public sealed class TerminalInterpreter : ITerminalOutputHandler
{
    /// <summary>
    /// How many distinct unsupported sequences are reported before reporting stops.
    /// </summary>
    /// <remarks>
    /// Output is untrusted and can be generated far faster than a log can absorb it. Each distinct
    /// sequence is reported once, and the set of "already reported" sequences is itself capped, so
    /// a stream of endlessly varied unknown sequences cannot grow memory or flood the log.
    /// </remarks>
    private const int MaxReportedSequences = 64;

    private const int DeviceStatusReportTerminalOk = 5;
    private const int DeviceStatusReportCursorPosition = 6;
    private const int TabulationClearAtCursor = 0;
    private const int TabulationClearAll = 3;

    private const int ModeApplicationCursorKeys = 1;
    private const int ModeOrigin = 6;
    private const int ModeAutoWrap = 7;
    private const int ModeCursorVisible = 25;
    private const int ModeAlternateScreenLegacy = 47;
    private const int ModeAlternateScreenClearing = 1047;
    private const int ModeSaveCursor = 1048;
    private const int ModeAlternateScreenFull = 1049;
    private const int ModeBracketedPaste = 2004;

    private const int ModeInsertReplace = 4;

    private const int OscSetIconAndTitle = 0;
    private const int OscSetIconName = 1;
    private const int OscSetTitle = 2;

    private static readonly byte[] TerminalOkResponse = "\u001b[0n"u8.ToArray();

    /// <summary>
    /// Primary Device Attributes reply. Identifies as a VT100 with the Advanced Video Option, which
    /// is what most software expects and what xterm reports by default.
    /// </summary>
    private static readonly byte[] DeviceAttributesResponse = "\u001b[?1;2c"u8.ToArray();

    private readonly TerminalState _terminal;
    private readonly ILogger _logger;
    private readonly HashSet<int> _reportedSequences = [];

    /// <summary>Creates an interpreter driving <paramref name="terminal"/>.</summary>
    public TerminalInterpreter(TerminalState terminal, ILogger<TerminalInterpreter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        _terminal = terminal;
        _logger = logger ?? NullLogger<TerminalInterpreter>.Instance;
    }

    /// <summary>
    /// Raised when the terminal must answer a query by writing bytes back to the shell, as though
    /// the user had typed them.
    /// </summary>
    public event EventHandler<ReadOnlyMemory<byte>>? ResponseRequested;

    /// <summary>Raised when the data stream contains a bell character.</summary>
    public event EventHandler? BellRequested;

    /// <summary>Raised when the shell sets the window title.</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>The terminal this interpreter drives.</summary>
    public TerminalState Terminal => _terminal;

    /// <inheritdoc />
    public void Print(Rune rune) => _terminal.Print(rune);

    /// <inheritdoc />
    public void Execute(byte control)
    {
        switch (control)
        {
            case VtConstants.Bel:
                BellRequested?.Invoke(this, EventArgs.Empty);
                break;
            case VtConstants.Backspace:
                _terminal.Backspace();
                break;
            case VtConstants.Tab:
                _terminal.HorizontalTab();
                break;
            case VtConstants.LineFeed:
            case VtConstants.VerticalTab:
            case VtConstants.FormFeed:
                // A vertical tab and a form feed both behave as a line feed on a video terminal.
                _terminal.LineFeed();
                break;
            case VtConstants.CarriageReturn:
                _terminal.CarriageReturn();
                break;
            case VtConstants.ShiftOut:
                // Shift out selects G1, shift in selects G0. Programs pair them around box-drawing
                // runs: shift out, draw the border, shift back.
                _terminal.UsingG1 = true;
                break;
            case VtConstants.ShiftIn:
                _terminal.UsingG1 = false;
                break;
            default:
                // NUL and the rest have no effect here. They are common enough in real output that
                // reporting them would be noise rather than information.
                break;
        }
    }

    /// <inheritdoc />
    public void EscapeDispatch(char final, char intermediate)
    {
        if (intermediate != CsiSequence.None)
        {
            DispatchEscapeWithIntermediate(final, intermediate);
            return;
        }

        switch (final)
        {
            case EscapeFinal.ResetToInitialState:
                _terminal.Reset();
                break;
            case EscapeFinal.Index:
                _terminal.LineFeed();
                break;
            case EscapeFinal.NextLine:
                _terminal.CarriageReturn();
                _terminal.LineFeed();
                break;
            case EscapeFinal.ReverseIndex:
                _terminal.ReverseLineFeed();
                break;
            case EscapeFinal.TabulationSet:
                _terminal.TabStops.Set(_terminal.Cursor.Column);
                break;
            case EscapeFinal.SaveCursor:
                _terminal.SaveCursor();
                break;
            case EscapeFinal.RestoreCursor:
                _terminal.RestoreCursor();
                break;
            case EscapeFinal.ApplicationKeypad:
                _terminal.ApplicationKeypad = true;
                break;
            case EscapeFinal.NumericKeypad:
                _terminal.ApplicationKeypad = false;
                break;
            case EscapeFinal.StringTerminator:
                // The string it terminated has already been dispatched.
                break;
            default:
                ReportUnsupported(SequenceKind.Escape, final, CsiSequence.None, CsiSequence.None);
                break;
        }
    }

    /// <inheritdoc />
    public void CsiDispatch(in CsiSequence sequence)
    {
        if (sequence.PrivateMarker != CsiSequence.None)
        {
            DispatchPrivateMode(in sequence);
            return;
        }

        if (sequence.Intermediate != CsiSequence.None)
        {
            if (sequence.Intermediate == ' ' && sequence.Final == CursorStyleFinal)
            {
                SetCursorStyle(sequence.GetOrDefault(0, 0));
                return;
            }

            ReportUnsupported(SequenceKind.Csi, sequence.Final, sequence.Intermediate, CsiSequence.None);
            return;
        }

        switch (sequence.Final)
        {
            case CsiFinal.CursorUp:
                _terminal.MoveCursorUp(sequence.GetOrDefault(0, 1));
                break;
            case CsiFinal.CursorDown:
            case CsiFinal.VerticalPositionRelative:
                _terminal.MoveCursorDown(sequence.GetOrDefault(0, 1));
                break;
            case CsiFinal.CursorForward:
            case CsiFinal.HorizontalPositionRelative:
                _terminal.MoveCursorForward(sequence.GetOrDefault(0, 1));
                break;
            case CsiFinal.CursorBackward:
                _terminal.MoveCursorBackward(sequence.GetOrDefault(0, 1));
                break;

            case CsiFinal.CursorNextLine:
                _terminal.MoveCursorDown(sequence.GetOrDefault(0, 1));
                _terminal.CarriageReturn();
                break;
            case CsiFinal.CursorPreviousLine:
                _terminal.MoveCursorUp(sequence.GetOrDefault(0, 1));
                _terminal.CarriageReturn();
                break;

            case CsiFinal.CursorHorizontalAbsolute:
            case CsiFinal.HorizontalPositionAbsolute:
                _terminal.MoveCursorToColumn(ToZeroBased(sequence.GetOrDefault(0, 1)));
                break;
            case CsiFinal.VerticalPositionAbsolute:
                _terminal.MoveCursorToRow(_terminal.ResolveRow(sequence.GetOrDefault(0, 1)));
                break;

            case CsiFinal.CursorPosition:
            case CsiFinal.HorizontalVerticalPosition:
                // Parameters are row then column, both one-based - the opposite order to the
                // engine's (column, row) convention, and the classic source of off-by-one bugs.
                // Under origin mode the row is also relative to the scrolling region.
                _terminal.MoveCursorTo(
                    ToZeroBased(sequence.GetOrDefault(1, 1)),
                    _terminal.ResolveRow(sequence.GetOrDefault(0, 1)));
                break;

            case CsiFinal.CursorForwardTabulation:
                RepeatTab(sequence.GetOrDefault(0, 1), forward: true);
                break;
            case CsiFinal.CursorBackwardTabulation:
                RepeatTab(sequence.GetOrDefault(0, 1), forward: false);
                break;

            case CsiFinal.EraseInDisplay:
                EraseInDisplay(in sequence);
                break;
            case CsiFinal.EraseInLine:
                EraseInLine(in sequence);
                break;
            case CsiFinal.EraseCharacter:
                _terminal.EraseCharacters(sequence.GetOrDefault(0, 1));
                break;

            case CsiFinal.InsertCharacter:
                _terminal.InsertCharacters(sequence.GetOrDefault(0, 1));
                break;
            case CsiFinal.DeleteCharacter:
                _terminal.DeleteCharacters(sequence.GetOrDefault(0, 1));
                break;
            case CsiFinal.InsertLine:
                _terminal.InsertLines(sequence.GetOrDefault(0, 1));
                break;
            case CsiFinal.DeleteLine:
                _terminal.DeleteLines(sequence.GetOrDefault(0, 1));
                break;

            case CsiFinal.ScrollUp:
                _terminal.ScrollUp(sequence.GetOrDefault(0, 1));
                break;
            case CsiFinal.ScrollDown:
                _terminal.ScrollDown(sequence.GetOrDefault(0, 1));
                break;

            case CsiFinal.SetScrollingRegion:
                SetScrollingRegion(in sequence);
                break;

            case CsiFinal.TabulationClear:
                ClearTabulation(in sequence);
                break;

            case CsiFinal.SelectGraphicRendition:
                _terminal.CurrentStyle = SgrInterpreter.Apply(_terminal.CurrentStyle, in sequence);
                break;

            case CsiFinal.SaveCursor:
                _terminal.SaveCursor();
                break;
            case CsiFinal.RestoreCursor:
                _terminal.RestoreCursor();
                break;

            case CsiFinal.DeviceStatusReport:
                ReportStatus(in sequence);
                break;
            case CsiFinal.DeviceAttributes:
                Respond(DeviceAttributesResponse);
                break;

            case CsiFinal.SetMode:
            case CsiFinal.ResetMode:
                DispatchStandardMode(in sequence);
                break;

            default:
                ReportUnsupported(SequenceKind.Csi, sequence.Final, CsiSequence.None, CsiSequence.None);
                break;
        }
    }

    /// <inheritdoc />
    public void OscDispatch(ReadOnlySpan<byte> data)
    {
        var separator = data.IndexOf((byte)';');
        var commandText = separator < 0 ? data : data[..separator];

        if (!TryParseAscii(commandText, out var command))
        {
            ReportUnsupported(SequenceKind.Osc, CsiSequence.None, CsiSequence.None, CsiSequence.None);
            return;
        }

        var payload = separator < 0 ? [] : data[(separator + 1)..];

        switch (command)
        {
            case OscSetIconAndTitle:
            case OscSetTitle:
                // Titles are UTF-8 and arbitrary; decoding with replacement rather than throwing
                // keeps malformed output from taking down the session.
                TitleChanged?.Invoke(this, Encoding.UTF8.GetString(payload));
                break;

            case OscSetIconName:
                // The icon name has no separate meaning in a windowed terminal.
                break;

            default:
                ReportUnsupported(SequenceKind.Osc, (char)command, CsiSequence.None, CsiSequence.None);
                break;
        }
    }

    /// <summary>
    /// Handles escape sequences that carry an intermediate byte: character set designation and the
    /// screen alignment pattern.
    /// </summary>
    private void DispatchEscapeWithIntermediate(char final, char intermediate)
    {
        switch (intermediate)
        {
            // "ESC ( x" designates a set into G0, "ESC ) x" into G1.
            case '(':
            case ')':
                {
                    var slot = intermediate == '(' ? 0 : 1;

                    var characterSet = final switch
                    {
                        '0' => CharacterSet.DecSpecialGraphics,
                        'B' => CharacterSet.UsAscii,
                        _ => (CharacterSet?)null,
                    };

                    if (characterSet is { } selected)
                    {
                        _terminal.DesignateCharacterSet(slot, selected);
                    }
                    else
                    {
                        // Any other national set is treated as ASCII: the alternative is mangling text
                        // that is almost certainly UTF-8 in practice.
                        _terminal.DesignateCharacterSet(slot, CharacterSet.UsAscii);
                        ReportUnsupported(SequenceKind.Escape, final, intermediate, CsiSequence.None);
                    }

                    return;
                }

            case '#' when final == '8':
                _terminal.ScreenAlignmentPattern();
                return;

            default:
                ReportUnsupported(SequenceKind.Escape, final, intermediate, CsiSequence.None);
                return;
        }
    }

    /// <summary>Final byte of DECSCUSR, which selects the cursor shape.</summary>
    private const char CursorStyleFinal = 'q';

    /// <summary>Applies the cursor shape selected by <c>DECSCUSR</c>.</summary>
    /// <remarks>
    /// The parameter encodes shape and blinking together: odd values blink, even values do not, and
    /// zero means "back to the configured default".
    /// </remarks>
    private void SetCursorStyle(int parameter)
    {
        var cursor = _terminal.Cursor;

        switch (parameter)
        {
            case 0:
            case 1:
            case 2:
                cursor.Style = CursorStyle.Block;
                break;
            case 3:
            case 4:
                cursor.Style = CursorStyle.Underline;
                break;
            case 5:
            case 6:
                cursor.Style = CursorStyle.Bar;
                break;
            default:
                ReportUnsupported(SequenceKind.Csi, CursorStyleFinal, ' ', CsiSequence.None);
                break;
        }
    }

    /// <summary>Handles the standard, non-private modes.</summary>
    private void DispatchStandardMode(in CsiSequence sequence)
    {
        var isSet = sequence.Final == CsiFinal.SetMode;

        for (var index = 0; index < sequence.Count; index++)
        {
            if (sequence[index] == ModeInsertReplace)
            {
                _terminal.InsertMode = isSet;
            }
            else
            {
                ReportUnsupported(SequenceKind.Csi, sequence.Final, CsiSequence.None, CsiSequence.None);
            }
        }
    }

    /// <summary>Switches between the primary and alternate screens.</summary>
    private void SwitchAlternateScreen(bool enable, bool withCursor)
    {
        if (enable)
        {
            _terminal.EnableAlternateScreen(withCursor);
        }
        else
        {
            _terminal.DisableAlternateScreen(withCursor);
        }
    }

    private void DispatchPrivateMode(in CsiSequence sequence)
    {
        var isSet = sequence.Final == CsiFinal.SetMode;

        if (!isSet && sequence.Final != CsiFinal.ResetMode)
        {
            ReportUnsupported(SequenceKind.Csi, sequence.Final, CsiSequence.None, sequence.PrivateMarker);
            return;
        }

        for (var index = 0; index < sequence.Count; index++)
        {
            switch (sequence[index])
            {
                case ModeApplicationCursorKeys:
                    // DECCKM changes what the arrow keys send, so it belongs to input rather than
                    // to the screen; the flag is recorded for the input layer to read.
                    _terminal.ApplicationCursorKeys = isSet;
                    break;
                case ModeOrigin:
                    _terminal.OriginMode = isSet;

                    // Setting or clearing origin mode homes the cursor, which under origin mode
                    // means the top of the scrolling region rather than the top of the screen.
                    _terminal.MoveCursorTo(0, isSet ? _terminal.ScrollRegion.Top : 0);
                    break;
                case ModeBracketedPaste:
                    _terminal.BracketedPaste = isSet;
                    break;
                case ModeAlternateScreenLegacy:
                case ModeAlternateScreenClearing:
                    SwitchAlternateScreen(isSet, withCursor: false);
                    break;
                case ModeAlternateScreenFull:
                    SwitchAlternateScreen(isSet, withCursor: true);
                    break;
                case ModeSaveCursor:
                    if (isSet)
                    {
                        _terminal.SaveCursor();
                    }
                    else
                    {
                        _terminal.RestoreCursor();
                    }

                    break;
                case ModeAutoWrap:
                    _terminal.AutoWrap = isSet;
                    break;
                case ModeCursorVisible:
                    _terminal.Cursor.IsVisible = isSet;
                    break;
                default:
                    ReportUnsupported(SequenceKind.PrivateMode, (char)sequence[index], CsiSequence.None, sequence.PrivateMarker);
                    break;
            }
        }
    }

    private const int EraseScrollback = 3;

    private void EraseInDisplay(in CsiSequence sequence)
    {
        var parameter = sequence.GetOrDefault(0, 0);

        if (parameter == EraseScrollback)
        {
            // "Erase saved lines": what a shell's clear command sends to make the history
            // unreachable as well as the screen blank.
            _terminal.ClearScrollback();
            return;
        }

        if (!TryGetEraseExtent(parameter, out var extent))
        {
            ReportUnsupported(SequenceKind.Csi, CsiFinal.EraseInDisplay, (char)parameter, CsiSequence.None);
            return;
        }

        _terminal.EraseInDisplay(extent);
    }

    private void EraseInLine(in CsiSequence sequence)
    {
        var parameter = sequence.GetOrDefault(0, 0);

        if (!TryGetEraseExtent(parameter, out var extent))
        {
            ReportUnsupported(SequenceKind.Csi, CsiFinal.EraseInLine, (char)parameter, CsiSequence.None);
            return;
        }

        _terminal.EraseInLine(extent);
    }

    private void SetScrollingRegion(in CsiSequence sequence)
    {
        // With no parameters the region is the whole screen again.
        if (sequence.Count == 0)
        {
            _terminal.ResetScrollRegion();
            return;
        }

        var top = ToZeroBased(sequence.GetOrDefault(0, 1));
        var bottom = ToZeroBased(sequence.GetOrDefault(1, _terminal.Size.Rows));

        if (_terminal.SetScrollRegion(top, bottom))
        {
            return;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var description = sequence.ToString();
            _logger.InvalidParameters(description);
        }
    }

    private void ClearTabulation(in CsiSequence sequence)
    {
        switch (sequence.GetOrDefault(0, TabulationClearAtCursor))
        {
            case TabulationClearAtCursor:
                _terminal.TabStops.Clear(_terminal.Cursor.Column);
                break;
            case TabulationClearAll:
                _terminal.TabStops.ClearAll();
                break;
            default:
                ReportUnsupported(SequenceKind.Csi, CsiFinal.TabulationClear, CsiSequence.None, CsiSequence.None);
                break;
        }
    }

    private void RepeatTab(int count, bool forward)
    {
        for (var repeat = 0; repeat < Math.Max(1, count); repeat++)
        {
            if (forward)
            {
                _terminal.HorizontalTab();
            }
            else
            {
                _terminal.BackTab();
            }
        }
    }

    private void ReportStatus(in CsiSequence sequence)
    {
        switch (sequence.GetOrDefault(0, 0))
        {
            case DeviceStatusReportTerminalOk:
                Respond(TerminalOkResponse);
                break;

            case DeviceStatusReportCursorPosition:
                {
                    // The reply is one-based, matching the coordinates CUP accepts, so that a program
                    // can feed the answer straight back as a cursor position.
                    var row = _terminal.Cursor.Row + 1;
                    var column = _terminal.Cursor.Column + 1;
                    var reply = string.Create(
                        CultureInfo.InvariantCulture,
                        $"\u001b[{row};{column}R");
                    Respond(Encoding.ASCII.GetBytes(reply));
                    break;
                }

            default:
                ReportUnsupported(SequenceKind.Csi, CsiFinal.DeviceStatusReport, CsiSequence.None, CsiSequence.None);
                break;
        }
    }

    private void Respond(ReadOnlyMemory<byte> response) => ResponseRequested?.Invoke(this, response);

    private static bool TryGetEraseExtent(int parameter, out EraseExtent extent)
    {
        extent = (EraseExtent)parameter;
        return Enum.IsDefined(extent);
    }

    private static bool TryParseAscii(ReadOnlySpan<byte> text, out int value)
    {
        value = 0;

        if (text.IsEmpty)
        {
            return true; // An empty command is command zero.
        }

        foreach (var digit in text)
        {
            if (digit is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            value = (value * 10) + (digit - '0');

            if (value > VtConstants.MaxParameterValue)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Converts a one-based protocol coordinate to a zero-based engine coordinate.</summary>
    private static int ToZeroBased(int oneBased) => Math.Max(1, oneBased) - 1;

    private void ReportUnsupported(SequenceKind kind, char final, char intermediate, char marker)
    {
        if (_reportedSequences.Count >= MaxReportedSequences)
        {
            return;
        }

        var key = (int)kind | (final << 8) | (intermediate << 16) | (marker << 24);

        if (!_reportedSequences.Add(key))
        {
            return;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var description = DescribeSequence(kind, final, intermediate, marker);
            _logger.UnsupportedSequence(description);
        }
    }

    private static string DescribeSequence(SequenceKind kind, char final, char intermediate, char marker)
    {
        var markerText = marker == CsiSequence.None ? string.Empty : marker.ToString();
        var intermediateText = intermediate == CsiSequence.None ? string.Empty : intermediate.ToString();

        return kind switch
        {
            SequenceKind.Escape => $"ESC {intermediateText}{final}",
            SequenceKind.Csi => $"CSI {markerText}...{intermediateText}{final}",
            SequenceKind.PrivateMode => $"CSI {markerText}{(int)final} h/l",
            _ => $"OSC {(int)final}",
        };
    }

    private enum SequenceKind
    {
        Escape = 1,
        Csi = 2,
        PrivateMode = 3,
        Osc = 4,
    }
}
