using System.Globalization;
using System.Text;

namespace NovaTerminal.Terminal;

/// <summary>
/// Determines how many grid columns a character occupies.
/// </summary>
/// <remarks>
/// <para>
/// Terminals lay text out on a fixed grid, but Unicode characters are not uniformly one column
/// wide. Three cases matter:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Zero columns</b> - combining marks and formatting characters. A combining acute accent
///     modifies the character before it rather than occupying space of its own.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>One column</b> - the ordinary case, including all of ASCII.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Two columns</b> - characters with East Asian Width "Wide" or "Fullwidth", such as CJK
///     ideographs and most emoji. These occupy a cell pair in the grid.
///     </description>
///   </item>
/// </list>
/// <para>
/// This is the C library's <c>wcwidth</c> problem. Getting it wrong does not merely look bad: the
/// shell and the emulator must agree on where the cursor is after printing a string, so a
/// disagreement about one character desynchronises the whole line.
/// </para>
/// <para>
/// Zero-width characters are identified from their Unicode category, which the runtime already
/// knows. Double-width characters are identified from a sorted range table, searched in
/// <c>O(log n)</c>: there is no such property exposed by the base class library.
/// </para>
/// </remarks>
public static class CharacterWidth
{
    /// <summary>Width reported for characters that combine with a preceding character.</summary>
    public const int ZeroWidth = 0;

    /// <summary>Width reported for ordinary characters.</summary>
    public const int SingleWidth = 1;

    /// <summary>Width reported for East Asian wide and fullwidth characters.</summary>
    public const int DoubleWidth = 2;

    private const int AsciiPrintableStart = 0x20;
    private const int AsciiPrintableEnd = 0x7E;

    /// <summary>
    /// Inclusive code point ranges whose characters occupy two columns, sorted by start value.
    /// Derived from the Unicode East Asian Width property (values W and F), plus the emoji blocks
    /// that terminals conventionally render double-width.
    /// </summary>
    private static readonly (int Start, int End)[] WideRanges =
    [
        (0x1100, 0x115F),     // Hangul Jamo initial consonants
        (0x2329, 0x232A),     // Angle brackets
        (0x2E80, 0x303E),     // CJK radicals, Kangxi radicals, CJK symbols and punctuation
        (0x3041, 0x33FF),     // Hiragana, Katakana, Bopomofo, Hangul Compatibility Jamo, CJK compatibility
        (0x3400, 0x4DBF),     // CJK unified ideographs extension A
        (0x4E00, 0x9FFF),     // CJK unified ideographs
        (0xA000, 0xA4CF),     // Yi syllables and radicals
        (0xA960, 0xA97F),     // Hangul Jamo extended A
        (0xAC00, 0xD7A3),     // Hangul syllables
        (0xF900, 0xFAFF),     // CJK compatibility ideographs
        (0xFE10, 0xFE19),     // Vertical forms
        (0xFE30, 0xFE6F),     // CJK compatibility forms, small form variants
        (0xFF00, 0xFF60),     // Fullwidth ASCII variants and punctuation
        (0xFFE0, 0xFFE6),     // Fullwidth currency and other signs
        (0x16FE0, 0x16FE4),   // Tangut and Nushu components
        (0x17000, 0x18AFF),   // Tangut ideographs and components
        (0x1B000, 0x1B12F),   // Kana supplement and extended A
        (0x1F004, 0x1F004),   // Mahjong tile red dragon
        (0x1F0CF, 0x1F0CF),   // Playing card black joker
        (0x1F18E, 0x1F18E),   // Negative squared AB
        (0x1F191, 0x1F19A),   // Squared CL through squared VS
        (0x1F200, 0x1F2FF),   // Enclosed ideographic supplement
        (0x1F300, 0x1F64F),   // Miscellaneous symbols and pictographs, emoticons
        (0x1F680, 0x1F6FF),   // Transport and map symbols
        (0x1F900, 0x1F9FF),   // Supplemental symbols and pictographs
        (0x1FA70, 0x1FAFF),   // Symbols and pictographs extended A
        (0x20000, 0x2FFFD),   // CJK unified ideographs extensions B onward
        (0x30000, 0x3FFFD),   // CJK unified ideographs extension G onward
    ];

    /// <summary>
    /// Returns the number of columns <paramref name="rune"/> occupies: 0, 1 or 2.
    /// </summary>
    /// <remarks>
    /// Control characters report zero. They should never reach this method - the parser executes
    /// them rather than printing them - but reporting zero means that a stray one cannot corrupt
    /// the layout if it does.
    /// </remarks>
    public static int Measure(Rune rune)
    {
        var value = rune.Value;

        // Printable ASCII is the overwhelming majority of terminal output, so it is answered first
        // without touching either the category lookup or the range table.
        if (value is >= AsciiPrintableStart and <= AsciiPrintableEnd)
        {
            return SingleWidth;
        }

        if (IsZeroWidth(rune))
        {
            return ZeroWidth;
        }

        return IsWide(value) ? DoubleWidth : SingleWidth;
    }

    /// <summary>
    /// Returns the number of columns a string occupies, which is what the shell believes the
    /// cursor will advance by.
    /// </summary>
    public static int Measure(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var total = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            total += Measure(rune);
        }

        return total;
    }

    /// <summary>
    /// Determines whether a character combines with the one before it instead of occupying its own
    /// column.
    /// </summary>
    public static bool IsZeroWidth(Rune rune)
        => Rune.GetUnicodeCategory(rune) switch
        {
            // Combining marks attach to the previous character.
            UnicodeCategory.NonSpacingMark => true,
            UnicodeCategory.EnclosingMark => true,

            // Formatting characters such as the zero-width joiner are invisible directives.
            UnicodeCategory.Format => rune.Value != 0x00AD, // The soft hyphen does occupy a column.

            // Control characters are executed, never printed.
            UnicodeCategory.Control => true,
            _ => false,
        };

    /// <summary>Determines whether a code point occupies two columns.</summary>
    public static bool IsWide(int codePoint)
    {
        var low = 0;
        var high = WideRanges.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var (start, end) = WideRanges[middle];

            if (codePoint < start)
            {
                high = middle - 1;
            }
            else if (codePoint > end)
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
