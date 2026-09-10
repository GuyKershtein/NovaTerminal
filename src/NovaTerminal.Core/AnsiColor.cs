namespace NovaTerminal.Core;

/// <summary>
/// The sixteen colours addressable by the classic SGR colour codes (30-37 and 90-97 for foreground,
/// 40-47 and 100-107 for background).
/// </summary>
/// <remarks>
/// These are names, not values. They correspond to indices 0-15 of the 256-colour palette and are
/// resolved to concrete RGB by the active theme, which is why "red" looks different in every
/// terminal colour scheme yet still means the same thing to the program producing it.
/// </remarks>
public enum AnsiColor : byte
{
    /// <summary>Palette index 0.</summary>
    Black = 0,

    /// <summary>Palette index 1.</summary>
    Red = 1,

    /// <summary>Palette index 2.</summary>
    Green = 2,

    /// <summary>Palette index 3.</summary>
    Yellow = 3,

    /// <summary>Palette index 4.</summary>
    Blue = 4,

    /// <summary>Palette index 5.</summary>
    Magenta = 5,

    /// <summary>Palette index 6.</summary>
    Cyan = 6,

    /// <summary>Palette index 7.</summary>
    White = 7,

    /// <summary>Palette index 8, conventionally rendered as a dark grey.</summary>
    BrightBlack = 8,

    /// <summary>Palette index 9.</summary>
    BrightRed = 9,

    /// <summary>Palette index 10.</summary>
    BrightGreen = 10,

    /// <summary>Palette index 11.</summary>
    BrightYellow = 11,

    /// <summary>Palette index 12.</summary>
    BrightBlue = 12,

    /// <summary>Palette index 13.</summary>
    BrightMagenta = 13,

    /// <summary>Palette index 14.</summary>
    BrightCyan = 14,

    /// <summary>Palette index 15.</summary>
    BrightWhite = 15,
}
