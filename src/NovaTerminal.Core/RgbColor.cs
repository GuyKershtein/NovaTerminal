using System.Globalization;

namespace NovaTerminal.Core;

/// <summary>
/// A concrete 24-bit colour, after a <see cref="TerminalColor"/> has been resolved through a theme.
/// </summary>
/// <remarks>
/// The distinction from <see cref="TerminalColor"/> matters. That type records what the data stream
/// <em>asked for</em> - "the default foreground", "palette entry 4" - which cannot be drawn until a
/// theme says what those mean. This type is the answer, and it is the only kind of colour the
/// renderer ever sees.
/// </remarks>
/// <param name="Red">Red component.</param>
/// <param name="Green">Green component.</param>
/// <param name="Blue">Blue component.</param>
public readonly record struct RgbColor(byte Red, byte Green, byte Blue)
{
    private const int HexLength = 6;
    private const int HexLengthWithHash = 7;

    /// <summary>Opaque black.</summary>
    public static RgbColor Black => new(0, 0, 0);

    /// <summary>Opaque white.</summary>
    public static RgbColor White => new(255, 255, 255);

    /// <summary>Parses a colour written as <c>#RRGGBB</c> or <c>RRGGBB</c>.</summary>
    /// <exception cref="FormatException">The text is not a six-digit hexadecimal colour.</exception>
    public static RgbColor Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!TryParse(text, out var color))
        {
            throw new FormatException($"'{text}' is not a colour of the form #RRGGBB.");
        }

        return color;
    }

    /// <summary>Parses a colour written as <c>#RRGGBB</c>, returning false rather than throwing.</summary>
    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;

        if (text is null || text.Length is not (HexLength or HexLengthWithHash))
        {
            return false;
        }

        var digits = text.Length == HexLengthWithHash ? text.AsSpan(1) : text.AsSpan();

        if (text.Length == HexLengthWithHash && text[0] != '#')
        {
            return false;
        }

        if (!byte.TryParse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            || !byte.TryParse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            || !byte.TryParse(digits[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        color = new RgbColor(red, green, blue);
        return true;
    }

    /// <summary>
    /// Blends toward <paramref name="other"/>, where zero is this colour and one is the other.
    /// Used to render the faint attribute, which is defined as a reduced-intensity foreground.
    /// </summary>
    public RgbColor Blend(RgbColor other, double amount)
    {
        var weight = Math.Clamp(amount, 0, 1);

        return new RgbColor(
            Interpolate(Red, other.Red, weight),
            Interpolate(Green, other.Green, weight),
            Interpolate(Blue, other.Blue, weight));
    }

    /// <inheritdoc />
    public override string ToString() => $"#{Red:X2}{Green:X2}{Blue:X2}";

    private static byte Interpolate(byte from, byte to, double weight)
        => (byte)Math.Round(from + ((to - from) * weight));
}
