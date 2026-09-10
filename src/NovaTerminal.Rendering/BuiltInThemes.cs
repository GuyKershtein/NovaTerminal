using System.Collections.Immutable;
using NovaTerminal.Core;

namespace NovaTerminal.Rendering;

/// <summary>
/// The themes NovaTerminal ships with.
/// </summary>
/// <remarks>
/// Colours are defined once, here, and nowhere else. Nothing in the rendering path contains a
/// literal colour, which is what allows a theme change to repaint the screen correctly rather than
/// leaving hard-coded values behind.
/// </remarks>
public static class BuiltInThemes
{
    /// <summary>The default dark theme.</summary>
    public static TerminalTheme NovaDark { get; } = new()
    {
        Name = "NovaDark",
        Background = RgbColor.Parse("#11131A"),
        Foreground = RgbColor.Parse("#D7DAE0"),
        Cursor = RgbColor.Parse("#7AA2F7"),
        CursorText = RgbColor.Parse("#11131A"),
        SelectionBackground = RgbColor.Parse("#2E3C64"),
        Palette =
        [
            RgbColor.Parse("#1A1B26"), // black
            RgbColor.Parse("#F7768E"), // red
            RgbColor.Parse("#9ECE6A"), // green
            RgbColor.Parse("#E0AF68"), // yellow
            RgbColor.Parse("#7AA2F7"), // blue
            RgbColor.Parse("#BB9AF7"), // magenta
            RgbColor.Parse("#7DCFFF"), // cyan
            RgbColor.Parse("#A9B1D6"), // white
            RgbColor.Parse("#414868"), // bright black
            RgbColor.Parse("#FF7A93"), // bright red
            RgbColor.Parse("#B9F27C"), // bright green
            RgbColor.Parse("#FF9E64"), // bright yellow
            RgbColor.Parse("#8DB0FF"), // bright blue
            RgbColor.Parse("#C7A9FF"), // bright magenta
            RgbColor.Parse("#A4DAFF"), // bright cyan
            RgbColor.Parse("#C0CAF5"), // bright white
        ],
    };

    /// <summary>A light theme, for bright rooms and for printing screenshots.</summary>
    public static TerminalTheme NovaLight { get; } = new()
    {
        Name = "NovaLight",
        Background = RgbColor.Parse("#FAFAFA"),
        Foreground = RgbColor.Parse("#2C3038"),
        Cursor = RgbColor.Parse("#2B5FD9"),
        CursorText = RgbColor.Parse("#FAFAFA"),
        SelectionBackground = RgbColor.Parse("#BFD4F2"),
        Palette =
        [
            RgbColor.Parse("#2C3038"),
            RgbColor.Parse("#C4344B"),
            RgbColor.Parse("#3F8A3F"),
            RgbColor.Parse("#9A6A00"),
            RgbColor.Parse("#2B5FD9"),
            RgbColor.Parse("#8A3FBF"),
            RgbColor.Parse("#0F7C8C"),
            RgbColor.Parse("#6B7280"),
            RgbColor.Parse("#4B5563"),
            RgbColor.Parse("#E0576F"),
            RgbColor.Parse("#4FA84F"),
            RgbColor.Parse("#B98400"),
            RgbColor.Parse("#4C7DF0"),
            RgbColor.Parse("#A85FD6"),
            RgbColor.Parse("#159AAD"),
            RgbColor.Parse("#111827"),
        ],
    };

    /// <summary>
    /// A high-contrast theme, for users who need one and as a reminder that a terminal is read for
    /// hours at a time.
    /// </summary>
    public static TerminalTheme HighContrast { get; } = new()
    {
        Name = "HighContrast",
        Background = RgbColor.Black,
        Foreground = RgbColor.White,
        Cursor = RgbColor.Parse("#FFFF00"),
        CursorText = RgbColor.Black,
        SelectionBackground = RgbColor.Parse("#0000AA"),
        Palette =
        [
            RgbColor.Parse("#000000"),
            RgbColor.Parse("#FF3B30"),
            RgbColor.Parse("#00E676"),
            RgbColor.Parse("#FFEB3B"),
            RgbColor.Parse("#40A6FF"),
            RgbColor.Parse("#FF6EC7"),
            RgbColor.Parse("#00E5FF"),
            RgbColor.Parse("#E0E0E0"),
            RgbColor.Parse("#7F7F7F"),
            RgbColor.Parse("#FF7A70"),
            RgbColor.Parse("#7CFFB0"),
            RgbColor.Parse("#FFF59D"),
            RgbColor.Parse("#8CC8FF"),
            RgbColor.Parse("#FFA8DE"),
            RgbColor.Parse("#8CF3FF"),
            RgbColor.Parse("#FFFFFF"),
        ],
    };

    /// <summary>Every built-in theme, keyed by name and matched without regard to case.</summary>
    public static IReadOnlyDictionary<string, TerminalTheme> All { get; } =
        new Dictionary<string, TerminalTheme>(StringComparer.OrdinalIgnoreCase)
        {
            [NovaDark.Name] = NovaDark,
            [NovaLight.Name] = NovaLight,
            [HighContrast.Name] = HighContrast,
        };

    /// <summary>
    /// Returns the named theme, falling back to <see cref="NovaDark"/> when it is not recognised.
    /// A misspelled theme in a configuration file should not stop the terminal from starting.
    /// </summary>
    public static TerminalTheme GetOrDefault(string? name)
        => name is not null && All.TryGetValue(name, out var theme) ? theme : NovaDark;
}
