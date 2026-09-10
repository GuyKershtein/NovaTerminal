using System.Globalization;
using System.Text;

namespace NovaTerminal.Input;

/// <summary>
/// Turns key presses into the bytes a shell expects to receive.
/// </summary>
/// <remarks>
/// <para>
/// A terminal has no concept of a key press. It receives bytes, and a program distinguishes the Up
/// arrow from the letter A only because the arrow arrives as the escape sequence <c>ESC [ A</c>.
/// Encoding those sequences correctly is what makes arrow keys, tab completion and history work in
/// a shell.
/// </para>
/// <para>
/// The sequences follow xterm, which is what <c>TERM=xterm-256color</c> promises and therefore what
/// every program will expect. Modifiers are encoded as a parameter equal to
/// <c>1 + shift + alt·2 + ctrl·4 + super·8</c>, which is why <see cref="KeyModifiers"/> uses exactly
/// those values: the parameter is the flags plus one, with no table to get wrong.
/// </para>
/// <para>
/// Every method is a pure function of its arguments. That is a design choice, not an accident: it
/// makes the entire key table testable as data.
/// </para>
/// </remarks>
public static class KeyEncoder
{
    private const byte Escape = 0x1B;
    private const byte CarriageReturn = 0x0D;
    private const byte Backspace = 0x08;
    private const byte Tab = 0x09;
    private const byte Delete = 0x7F;
    private const byte Null = 0x00;

    private const int ControlMask = 0x1F;
    private const int ModifierParameterBase = 1;

    /// <summary>Sent before pasted text when bracketed paste is enabled.</summary>
    private const string BracketedPasteStart = "\u001b[200~";

    /// <summary>Sent after pasted text when bracketed paste is enabled.</summary>
    private const string BracketedPasteEnd = "\u001b[201~";

    /// <summary>
    /// Encodes a special key, or returns <see langword="null"/> when the key sends nothing.
    /// </summary>
    public static byte[]? Encode(TerminalKey key, KeyModifiers modifiers, TerminalInputModes modes)
    {
        var sequence = EncodeCore(key, modifiers, modes);

        if (sequence is null)
        {
            return null;
        }

        // Alt is transmitted as an escape prefix for keys that have no modifier parameter of their
        // own. This is the convention every shell reads as "meta".
        return sequence;
    }

    /// <summary>
    /// Encodes typed text as UTF-8, applying the control and alt conventions.
    /// </summary>
    /// <param name="text">The characters the keyboard produced.</param>
    /// <param name="modifiers">Modifiers held while typing.</param>
    public static byte[]? EncodeText(string text, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            var control = EncodeControl(text[0]);

            if (control is not null)
            {
                return modifiers.HasFlag(KeyModifiers.Alt) ? Prefix(Escape, control) : control;
            }
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        return modifiers.HasFlag(KeyModifiers.Alt) ? Prefix(Escape, bytes) : bytes;
    }

    /// <summary>
    /// Encodes text arriving from the clipboard.
    /// </summary>
    /// <remarks>
    /// With bracketed paste enabled the text is wrapped in markers so the receiving program can
    /// tell it from typing. That matters for safety as much as convenience: a shell that knows text
    /// was pasted will not execute a newline inside it, so pasting a command that secretly ends in
    /// a line break cannot run it without the user pressing Enter.
    /// </remarks>
    public static byte[] EncodePaste(string text, TerminalInputModes modes)
    {
        ArgumentNullException.ThrowIfNull(text);

        // A carriage return is what the Enter key sends, so pasted line breaks are normalised to
        // match rather than arriving as a mixture the shell has to guess about.
        var normalised = text.ReplaceLineEndings("\r");

        if (!modes.BracketedPaste)
        {
            return Encoding.UTF8.GetBytes(normalised);
        }

        return Encoding.UTF8.GetBytes(BracketedPasteStart + normalised + BracketedPasteEnd);
    }

    /// <summary>
    /// Maps a character to its control code, or <see langword="null"/> when it has none.
    /// </summary>
    /// <remarks>
    /// Control codes are the letter with its upper bits cleared, which is why Ctrl+C is 0x03 and
    /// Ctrl+[ is escape. The mapping is a property of ASCII, not a lookup table.
    /// </remarks>
    public static byte[]? EncodeControl(char character)
    {
        if (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
        {
            return [(byte)(char.ToUpperInvariant(character) & ControlMask)];
        }

        return character switch
        {
            ' ' or '@' => [Null],
            '[' => [Escape],
            '\\' => [0x1C],
            ']' => [0x1D],
            '^' => [0x1E],
            '_' or '?' => [0x1F],
            _ => null,
        };
    }

    private static byte[]? EncodeCore(TerminalKey key, KeyModifiers modifiers, TerminalInputModes modes)
        => key switch
        {
            TerminalKey.Enter => WithAlt([CarriageReturn], modifiers),

            // Backspace sends DEL, not BS. The naming is a historical accident, and getting it the
            // wrong way round is why some terminals delete forwards.
            TerminalKey.Backspace => WithAlt(
                modifiers.HasFlag(KeyModifiers.Control) ? [Backspace] : [Delete], modifiers),

            TerminalKey.Tab => modifiers.HasFlag(KeyModifiers.Shift)
                ? Ascii("\u001b[Z")
                : WithAlt([Tab], modifiers),

            TerminalKey.Escape => WithAlt([Escape], modifiers),

            TerminalKey.Up => CursorKey('A', modifiers, modes),
            TerminalKey.Down => CursorKey('B', modifiers, modes),
            TerminalKey.Right => CursorKey('C', modifiers, modes),
            TerminalKey.Left => CursorKey('D', modifiers, modes),
            TerminalKey.Home => CursorKey('H', modifiers, modes),
            TerminalKey.End => CursorKey('F', modifiers, modes),

            TerminalKey.Insert => TildeKey(2, modifiers),
            TerminalKey.Delete => TildeKey(3, modifiers),
            TerminalKey.PageUp => TildeKey(5, modifiers),
            TerminalKey.PageDown => TildeKey(6, modifiers),

            // F1 to F4 come from the VT100 keypad and use a different form from the rest.
            TerminalKey.F1 => FunctionKey('P', modifiers),
            TerminalKey.F2 => FunctionKey('Q', modifiers),
            TerminalKey.F3 => FunctionKey('R', modifiers),
            TerminalKey.F4 => FunctionKey('S', modifiers),

            TerminalKey.F5 => TildeKey(15, modifiers),
            TerminalKey.F6 => TildeKey(17, modifiers),
            TerminalKey.F7 => TildeKey(18, modifiers),
            TerminalKey.F8 => TildeKey(19, modifiers),
            TerminalKey.F9 => TildeKey(20, modifiers),
            TerminalKey.F10 => TildeKey(21, modifiers),
            TerminalKey.F11 => TildeKey(23, modifiers),
            TerminalKey.F12 => TildeKey(24, modifiers),

            _ => null,
        };

    /// <summary>
    /// Encodes an arrow or Home/End key, which changes form under application cursor key mode.
    /// </summary>
    private static byte[] CursorKey(char final, KeyModifiers modifiers, TerminalInputModes modes)
    {
        if (modifiers != KeyModifiers.None)
        {
            // A modified cursor key always uses the CSI form, even in application mode, because the
            // SS3 form has nowhere to put the modifier parameter.
            return Ascii(string.Create(
                CultureInfo.InvariantCulture, $"\u001b[1;{ModifierParameter(modifiers)}{final}"));
        }

        return Ascii(modes.ApplicationCursorKeys ? $"\u001bO{final}" : $"\u001b[{final}");
    }

    private static byte[] TildeKey(int number, KeyModifiers modifiers)
        => Ascii(modifiers == KeyModifiers.None
            ? string.Create(CultureInfo.InvariantCulture, $"\u001b[{number}~")
            : string.Create(CultureInfo.InvariantCulture, $"\u001b[{number};{ModifierParameter(modifiers)}~"));

    private static byte[] FunctionKey(char final, KeyModifiers modifiers)
        => Ascii(modifiers == KeyModifiers.None
            ? $"\u001bO{final}"
            : string.Create(CultureInfo.InvariantCulture, $"\u001b[1;{ModifierParameter(modifiers)}{final}"));

    /// <summary>
    /// The xterm modifier parameter: one plus the sum of the modifier weights.
    /// </summary>
    private static int ModifierParameter(KeyModifiers modifiers) => ModifierParameterBase + (int)modifiers;

    private static byte[] WithAlt(byte[] sequence, KeyModifiers modifiers)
        => modifiers.HasFlag(KeyModifiers.Alt) ? Prefix(Escape, sequence) : sequence;

    private static byte[] Prefix(byte value, byte[] sequence)
    {
        var result = new byte[sequence.Length + 1];
        result[0] = value;
        sequence.CopyTo(result, 1);
        return result;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}
