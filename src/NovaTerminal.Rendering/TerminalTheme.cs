using System.Collections.Immutable;
using NovaTerminal.Core;

namespace NovaTerminal.Rendering;

/// <summary>
/// Resolves the colours a data stream asks for into the colours actually painted.
/// </summary>
/// <remarks>
/// <para>
/// A terminal stream never names a real colour. It says "palette entry one" or "the default
/// foreground", and what those look like is the user's choice. A theme is that choice, and it is
/// the only thing in the system that turns a <see cref="TerminalColor"/> into an
/// <see cref="RgbColor"/>.
/// </para>
/// <para>
/// Keeping the mapping here - rather than letting the renderer decide - is what makes a theme
/// switch a one-line change that repaints correctly, including cells that were coloured "default"
/// rather than explicitly.
/// </para>
/// <para>
/// Only the sixteen named entries are stored. Palette entries 16 to 255 are defined by the standard
/// as a 6×6×6 colour cube followed by a greyscale ramp, so they are computed rather than listed:
/// they are the same in every terminal and a theme that changed them would be wrong.
/// </para>
/// </remarks>
public sealed record TerminalTheme
{
    /// <summary>Number of directly specified palette entries.</summary>
    public const int NamedColorCount = 16;

    private const int CubeStart = 16;
    private const int CubeSize = 6;
    private const int CubeEnd = CubeStart + (CubeSize * CubeSize * CubeSize) - 1;
    private const int GreyscaleStart = CubeEnd + 1;
    private const int GreyscaleBase = 8;
    private const int GreyscaleStep = 10;
    private const int CubeFirstStep = 55;
    private const int CubeStep = 40;

    /// <summary>The theme's name, as used in configuration.</summary>
    public required string Name { get; init; }

    /// <summary>Colour painted behind cells that use the default background.</summary>
    public required RgbColor Background { get; init; }

    /// <summary>Colour used for text that uses the default foreground.</summary>
    public required RgbColor Foreground { get; init; }

    /// <summary>Colour of the cursor.</summary>
    public required RgbColor Cursor { get; init; }

    /// <summary>Colour of the text under the cursor, so it stays legible.</summary>
    public required RgbColor CursorText { get; init; }

    /// <summary>Background of selected text.</summary>
    public required RgbColor SelectionBackground { get; init; }

    /// <summary>The sixteen named palette entries, in <see cref="AnsiColor"/> order.</summary>
    public required ImmutableArray<RgbColor> Palette { get; init; }

    /// <summary>Resolves a colour from the data stream into one that can be painted.</summary>
    /// <param name="color">The colour the stream asked for.</param>
    /// <param name="isBackground">
    /// Which default applies when the stream did not specify a colour at all.
    /// </param>
    public RgbColor Resolve(TerminalColor color, bool isBackground)
    {
        switch (color.Kind)
        {
            case TerminalColorKind.Rgb:
                color.TryGetRgb(out var red, out var green, out var blue);
                return new RgbColor(red, green, blue);

            case TerminalColorKind.Indexed:
                return ResolveIndex(color.Index);

            default:
                return isBackground ? Background : Foreground;
        }
    }

    /// <summary>Resolves one of the 256 palette entries.</summary>
    public RgbColor ResolveIndex(int index)
    {
        if (index < NamedColorCount)
        {
            return Palette[index];
        }

        if (index <= CubeEnd)
        {
            var offset = index - CubeStart;
            return new RgbColor(
                CubeComponent(offset / (CubeSize * CubeSize)),
                CubeComponent(offset / CubeSize % CubeSize),
                CubeComponent(offset % CubeSize));
        }

        // The greyscale ramp: twenty-four steps that deliberately skip pure black and pure white,
        // which are already available as palette entries zero and fifteen.
        var level = (byte)Math.Clamp(GreyscaleBase + ((index - GreyscaleStart) * GreyscaleStep), 0, 255);
        return new RgbColor(level, level, level);
    }

    /// <summary>
    /// Validates that the theme is usable, returning a description of each problem found.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("A theme must have a name.");
        }

        if (Palette.IsDefaultOrEmpty || Palette.Length != NamedColorCount)
        {
            errors.Add($"A theme must define exactly {NamedColorCount} palette entries.");
        }

        return errors;
    }

    private static byte CubeComponent(int step)
        => step == 0 ? (byte)0 : (byte)(CubeFirstStep + (step * CubeStep));
}
