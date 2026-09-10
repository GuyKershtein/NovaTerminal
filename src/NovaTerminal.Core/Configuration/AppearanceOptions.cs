namespace NovaTerminal.Core.Configuration;

/// <summary>Font, theme and cursor presentation settings.</summary>
public sealed class AppearanceOptions
{
    /// <summary>Smallest font size the renderer will accept, in device-independent pixels.</summary>
    public const double MinFontSize = 6.0;

    /// <summary>Largest font size the renderer will accept, in device-independent pixels.</summary>
    public const double MaxFontSize = 96.0;

    /// <summary>Smallest permitted multiplier applied to the font's natural line height.</summary>
    public const double MinLineHeightFactor = 0.8;

    /// <summary>Largest permitted multiplier applied to the font's natural line height.</summary>
    public const double MaxLineHeightFactor = 3.0;

    /// <summary>
    /// A comma-separated font stack. The first family that resolves wins; a terminal grid assumes a
    /// uniform advance width, so every family listed should be monospaced.
    /// </summary>
    public string FontFamily { get; set; } = "Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace";

    /// <summary>Font size in device-independent pixels.</summary>
    public double FontSize { get; set; } = 14.0;

    /// <summary>Multiplier applied to the font's natural line height to derive cell height.</summary>
    public double LineHeightFactor { get; set; } = 1.2;

    /// <summary>Name of the theme to activate at startup.</summary>
    public string ThemeName { get; set; } = "NovaDark";

    /// <summary>Shape of the cursor, unless the shell overrides it via DECSCUSR.</summary>
    public CursorStyle CursorStyle { get; set; } = CursorStyle.Block;

    /// <summary>Whether the cursor blinks when the terminal has focus.</summary>
    public bool CursorBlink { get; set; } = true;

    internal void Validate(string path, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(FontFamily))
        {
            errors.Add($"{path}.{nameof(FontFamily)} must name at least one font family.");
        }

        if (double.IsNaN(FontSize) || FontSize is < MinFontSize or > MaxFontSize)
        {
            errors.Add($"{path}.{nameof(FontSize)} must be between {MinFontSize} and {MaxFontSize}.");
        }

        if (double.IsNaN(LineHeightFactor) || LineHeightFactor is < MinLineHeightFactor or > MaxLineHeightFactor)
        {
            errors.Add(
                $"{path}.{nameof(LineHeightFactor)} must be between " +
                $"{MinLineHeightFactor} and {MaxLineHeightFactor}.");
        }

        if (string.IsNullOrWhiteSpace(ThemeName))
        {
            errors.Add($"{path}.{nameof(ThemeName)} must not be empty.");
        }

        if (!Enum.IsDefined(CursorStyle))
        {
            errors.Add($"{path}.{nameof(CursorStyle)} '{(int)CursorStyle}' is not a known cursor style.");
        }
    }
}
