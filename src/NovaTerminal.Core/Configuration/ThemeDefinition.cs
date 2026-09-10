namespace NovaTerminal.Core.Configuration;

/// <summary>
/// A theme as written in a configuration file: colours as text, before anything has been resolved.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a dumb data shape and lives in Core rather than in the rendering layer.
/// Configuration is read long before there is anything to draw, and keeping the file format
/// separate from the type the renderer uses means a malformed colour is a validation error rather
/// than an exception in the middle of a paint.
/// </para>
/// <para>
/// Every colour is optional. An omitted one falls back to the corresponding entry of the default
/// theme, so a user who only wants a different background writes only a different background.
/// </para>
/// </remarks>
public sealed class ThemeDefinition
{
    /// <summary>Number of named palette entries a complete palette defines.</summary>
    public const int PaletteSize = 16;

    /// <summary>Colour behind cells that use the default background, as <c>#RRGGBB</c>.</summary>
    public string? Background { get; set; }

    /// <summary>Colour of text that uses the default foreground.</summary>
    public string? Foreground { get; set; }

    /// <summary>Colour of the cursor.</summary>
    public string? Cursor { get; set; }

    /// <summary>Colour of the character under a block cursor.</summary>
    public string? CursorText { get; set; }

    /// <summary>Background of selected text.</summary>
    public string? SelectionBackground { get; set; }

    /// <summary>
    /// The sixteen named palette entries in ANSI order, or null to keep the default palette.
    /// </summary>
    public IList<string>? Palette { get; set; }

    /// <summary>
    /// Checks the colours parse and the palette is the right length, describing each problem found.
    /// </summary>
    public IReadOnlyList<string> Validate(string name)
    {
        var errors = new List<string>();

        ValidateColor(Background, $"{name}.{nameof(Background)}", errors);
        ValidateColor(Foreground, $"{name}.{nameof(Foreground)}", errors);
        ValidateColor(Cursor, $"{name}.{nameof(Cursor)}", errors);
        ValidateColor(CursorText, $"{name}.{nameof(CursorText)}", errors);
        ValidateColor(SelectionBackground, $"{name}.{nameof(SelectionBackground)}", errors);

        if (Palette is null)
        {
            return errors;
        }

        if (Palette.Count != PaletteSize)
        {
            errors.Add($"{name}.{nameof(Palette)} must list exactly {PaletteSize} colours, not {Palette.Count}.");
        }

        for (var index = 0; index < Palette.Count; index++)
        {
            ValidateColor(Palette[index], $"{name}.{nameof(Palette)}[{index}]", errors);
        }

        return errors;
    }

    private static void ValidateColor(string? value, string path, List<string> errors)
    {
        if (value is not null && !RgbColor.TryParse(value, out _))
        {
            errors.Add($"{path} is '{value}', which is not a colour of the form #RRGGBB.");
        }
    }
}
