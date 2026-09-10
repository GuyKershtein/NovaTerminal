namespace NovaTerminal.Core;

/// <summary>
/// A colour as specified by the terminal data stream: either "whatever the theme's default is",
/// a palette index, or a literal 24-bit RGB triple.
/// </summary>
/// <remarks>
/// <para>
/// The value is packed into a single 32-bit field: the high byte holds the
/// <see cref="TerminalColorKind"/> and the low 24 bits hold the payload (a palette index, or
/// R/G/B in that order from the most significant byte down).
/// </para>
/// <para>
/// The packing is deliberate. Every screen cell stores a foreground and a background colour, so a
/// 100x50 screen with 10,000 lines of scrollback holds well over a million colour values. Keeping
/// each one at four bytes instead of, say, a class reference keeps the buffer contiguous, keeps it
/// cache-friendly, and keeps it off the GC's scanning path entirely.
/// </para>
/// <para>
/// <c>default(TerminalColor)</c> is <see cref="Default"/>, which is the correct initial state for a
/// freshly allocated buffer: cleared cells use the theme's colours.
/// </para>
/// </remarks>
public readonly struct TerminalColor : IEquatable<TerminalColor>
{
    private const int KindShift = 24;
    private const uint PayloadMask = 0x00FF_FFFFu;
    private const int RedShift = 16;
    private const int GreenShift = 8;
    private const uint ByteMask = 0xFFu;

    private readonly uint _packed;

    private TerminalColor(TerminalColorKind kind, uint payload)
        => _packed = ((uint)kind << KindShift) | (payload & PayloadMask);

    /// <summary>The theme's default colour for this position (foreground or background).</summary>
    public static TerminalColor Default => default;

    /// <summary>Creates a colour referring to <paramref name="index"/> of the 256-colour palette.</summary>
    public static TerminalColor FromIndex(byte index) => new(TerminalColorKind.Indexed, index);

    /// <summary>Creates a colour referring to one of the sixteen named palette entries.</summary>
    public static TerminalColor FromAnsi(AnsiColor color) => FromIndex((byte)color);

    /// <summary>Creates a literal 24-bit colour.</summary>
    public static TerminalColor FromRgb(byte red, byte green, byte blue)
        => new(TerminalColorKind.Rgb, ((uint)red << RedShift) | ((uint)green << GreenShift) | blue);

    /// <summary>How this colour is specified.</summary>
    public TerminalColorKind Kind => (TerminalColorKind)(_packed >> KindShift);

    /// <summary>True when this colour defers to the theme.</summary>
    public bool IsDefault => Kind == TerminalColorKind.Default;

    /// <summary>The palette index. Valid only when <see cref="Kind"/> is <see cref="TerminalColorKind.Indexed"/>.</summary>
    /// <exception cref="InvalidOperationException">This colour is not a palette index.</exception>
    public byte Index => Kind == TerminalColorKind.Indexed
        ? (byte)(_packed & ByteMask)
        : throw new InvalidOperationException($"Colour of kind {Kind} has no palette index.");

    /// <summary>
    /// Extracts the literal RGB components, returning <see langword="false"/> for colours that must
    /// be resolved through a theme first.
    /// </summary>
    public bool TryGetRgb(out byte red, out byte green, out byte blue)
    {
        if (Kind != TerminalColorKind.Rgb)
        {
            red = green = blue = 0;
            return false;
        }

        red = (byte)((_packed >> RedShift) & ByteMask);
        green = (byte)((_packed >> GreenShift) & ByteMask);
        blue = (byte)(_packed & ByteMask);
        return true;
    }

    /// <inheritdoc />
    public bool Equals(TerminalColor other) => _packed == other._packed;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TerminalColor other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _packed.GetHashCode();

    /// <summary>Tests two colours for equality.</summary>
    public static bool operator ==(TerminalColor left, TerminalColor right) => left.Equals(right);

    /// <summary>Tests two colours for inequality.</summary>
    public static bool operator !=(TerminalColor left, TerminalColor right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        TerminalColorKind.Default => "default",
        TerminalColorKind.Indexed => $"index:{_packed & ByteMask}",
        _ => $"#{_packed & PayloadMask:X6}",
    };
}
