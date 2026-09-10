using System.Text;

namespace NovaTerminal.Terminal;

/// <summary>The character sets a terminal can map into its G0 and G1 slots.</summary>
public enum CharacterSet
{
    /// <summary>Ordinary ASCII, selected by <c>ESC ( B</c>.</summary>
    UsAscii = 0,

    /// <summary>
    /// DEC Special Graphics, selected by <c>ESC ( 0</c>: box-drawing characters in place of
    /// punctuation.
    /// </summary>
    DecSpecialGraphics = 1,
}

/// <summary>
/// The DEC Special Graphics character set: how text-mode programs draw boxes.
/// </summary>
/// <remarks>
/// <para>
/// A program that wants a horizontal line selects this set and prints the letter <c>q</c>. The
/// terminal is expected to draw a line, not a <c>q</c>. The mapping covers <c>0x5F</c> to
/// <c>0x7E</c>; everything else passes through unchanged.
/// </para>
/// <para>
/// This is not a historical curiosity. Programs still use it in preference to the Unicode
/// box-drawing block, because it works on terminals and fonts that predate widespread Unicode
/// support, and a terminal that ignores it renders their borders as random letters.
/// </para>
/// </remarks>
public static class DecSpecialGraphics
{
    private const int RangeStart = 0x5F;
    private const int RangeEnd = 0x7E;

    /// <summary>
    /// The replacements for <c>0x5F</c> through <c>0x7E</c>, in order.
    /// </summary>
    private static readonly char[] Replacements =
    [
        ' ', // _ no-break space
        '◆', // ` diamond
        '▒', // a checkerboard
        '␉', // b horizontal tab symbol
        '␌', // c form feed symbol
        '␍', // d carriage return symbol
        '␊', // e line feed symbol
        '°', // f degree sign
        '±', // g plus/minus
        '␤', // h newline symbol
        '␋', // i vertical tab symbol
        '┘', // j lower-right corner
        '┐', // k upper-right corner
        '┌', // l upper-left corner
        '└', // m lower-left corner
        '┼', // n crossing lines
        '⎺', // o horizontal line, scan 1
        '⎻', // p horizontal line, scan 3
        '─', // q horizontal line, scan 5
        '⎼', // r horizontal line, scan 7
        '⎽', // s horizontal line, scan 9
        '├', // t left tee
        '┤', // u right tee
        '┴', // v bottom tee
        '┬', // w top tee
        '│', // x vertical line
        '≤', // y less than or equal
        '≥', // z greater than or equal
        'π', // { pi
        '≠', // | not equal
        '£', // } sterling
        '·', // ~ centred dot
    ];

    /// <summary>
    /// Returns the box-drawing character for <paramref name="rune"/>, or the character unchanged
    /// when it lies outside the mapped range.
    /// </summary>
    public static Rune Translate(Rune rune)
    {
        var value = rune.Value;

        return value is >= RangeStart and <= RangeEnd
            ? new Rune(Replacements[value - RangeStart])
            : rune;
    }
}
