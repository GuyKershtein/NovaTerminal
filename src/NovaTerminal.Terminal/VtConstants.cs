namespace NovaTerminal.Terminal;

/// <summary>
/// Byte values defined by the C0 control set and the ECMA-48 escape sequence grammar.
/// </summary>
/// <remarks>
/// <para>
/// A terminal data stream mixes printable text with in-band control codes. Everything below 0x20 is
/// a C0 control character, and 0x1B (ESC) introduces the multi-byte sequences that carry cursor
/// movement, colour and mode changes.
/// </para>
/// <para>
/// These live as named constants because a parser full of bare hex literals is a parser nobody can
/// review. The names follow the standards (ECMA-48, DEC VT series) so they can be checked against
/// the specification directly.
/// </para>
/// </remarks>
public static class VtConstants
{
    /// <summary>Null (0x00). Ignored by the parser.</summary>
    public const byte Nul = 0x00;

    /// <summary>Bell (0x07). Requests an audible or visual alert; also terminates OSC strings.</summary>
    public const byte Bel = 0x07;

    /// <summary>Backspace (0x08). Moves the cursor one column left without erasing.</summary>
    public const byte Backspace = 0x08;

    /// <summary>Horizontal tab (0x09). Advances to the next tab stop.</summary>
    public const byte Tab = 0x09;

    /// <summary>Line feed (0x0A). Moves the cursor down one row, scrolling if at the bottom margin.</summary>
    public const byte LineFeed = 0x0A;

    /// <summary>Vertical tab (0x0B). Treated as a line feed, as on a real VT100.</summary>
    public const byte VerticalTab = 0x0B;

    /// <summary>Form feed (0x0C). Treated as a line feed.</summary>
    public const byte FormFeed = 0x0C;

    /// <summary>Carriage return (0x0D). Moves the cursor to column zero.</summary>
    public const byte CarriageReturn = 0x0D;

    /// <summary>Shift out (0x0E). Selects the G1 character set.</summary>
    public const byte ShiftOut = 0x0E;

    /// <summary>Shift in (0x0F). Selects the G0 character set.</summary>
    public const byte ShiftIn = 0x0F;

    /// <summary>Cancel (0x18). Aborts an escape sequence in progress.</summary>
    public const byte Cancel = 0x18;

    /// <summary>Substitute (0x1A). Aborts an escape sequence in progress, like <see cref="Cancel"/>.</summary>
    public const byte Substitute = 0x1A;

    /// <summary>Escape (0x1B). Introduces every multi-byte control sequence.</summary>
    public const byte Escape = 0x1B;

    /// <summary>Delete (0x7F). Ignored in the ground state.</summary>
    public const byte Delete = 0x7F;

    /// <summary>Space (0x20). The lowest printable byte, and an intermediate byte in some sequences.</summary>
    public const byte Space = 0x20;

    /// <summary>
    /// The byte that turns <c>ESC</c> into a Control Sequence Introducer: <c>ESC [</c>.
    /// </summary>
    public const byte CsiIntroducer = (byte)'[';

    /// <summary>The byte that turns <c>ESC</c> into an Operating System Command: <c>ESC ]</c>.</summary>
    public const byte OscIntroducer = (byte)']';

    /// <summary>The byte that turns <c>ESC</c> into a Device Control String: <c>ESC P</c>.</summary>
    public const byte DcsIntroducer = (byte)'P';

    /// <summary>String Terminator introducer: <c>ESC \</c>, which ends OSC and DCS strings.</summary>
    public const byte StringTerminator = (byte)'\\';

    /// <summary>Separator between numeric parameters in a CSI sequence, as in <c>CSI 1;31 m</c>.</summary>
    public const byte ParameterSeparator = (byte)';';

    /// <summary>
    /// Sub-parameter separator (colon), used by extended SGR forms such as
    /// <c>SGR 4:3</c> (curly underline).
    /// </summary>
    public const byte SubParameterSeparator = (byte)':';

    /// <summary>
    /// Upper bound applied to any single numeric parameter. ECMA-48 places no limit, so the parser
    /// imposes one: parameters come from untrusted output and are used for allocation and loop
    /// bounds.
    /// </summary>
    public const int MaxParameterValue = 65535;

    /// <summary>
    /// Maximum number of parameters retained for one sequence. Real sequences use a handful; the
    /// limit stops a hostile stream from growing the parameter list without bound.
    /// </summary>
    public const int MaxParameterCount = 32;

    /// <summary>
    /// Determines whether <paramref name="value"/> is a C0 control character (0x00-0x1F).
    /// </summary>
    public static bool IsC0Control(byte value) => value < Space;
}
