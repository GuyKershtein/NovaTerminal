namespace NovaTerminal.Core;

/// <summary>Discriminates the three ways a terminal can specify a colour.</summary>
public enum TerminalColorKind : byte
{
    /// <summary>
    /// The theme's default foreground or background. Distinct from an explicit colour because
    /// SGR 39/49 reset to it, and because a theme change must repaint default-coloured cells.
    /// </summary>
    Default = 0,

    /// <summary>An index into the 256-colour palette (0-15 named, 16-231 cube, 232-255 greyscale).</summary>
    Indexed = 1,

    /// <summary>A direct 24-bit RGB triple, as produced by <c>SGR 38;2;r;g;b</c>.</summary>
    Rgb = 2,
}
