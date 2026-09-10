using System.Collections.Immutable;
using NovaTerminal.Core;
using NovaTerminal.Core.Configuration;

namespace NovaTerminal.Rendering;

/// <summary>
/// The themes available to the application: the built-in ones, plus anything the user defined.
/// </summary>
/// <remarks>
/// A user theme with the same name as a built-in one replaces it. That is what lets someone change
/// one colour of a shipped theme without having to restate the other twenty.
/// </remarks>
public sealed class ThemeCatalog
{
    private readonly Dictionary<string, TerminalTheme> _themes;

    /// <summary>Builds a catalog from the built-in themes and any user definitions.</summary>
    public ThemeCatalog(IReadOnlyDictionary<string, ThemeDefinition>? definitions = null)
    {
        _themes = new Dictionary<string, TerminalTheme>(BuiltInThemes.All, StringComparer.OrdinalIgnoreCase);

        if (definitions is null)
        {
            return;
        }

        foreach (var (name, definition) in definitions)
        {
            // A definition builds on the theme it replaces when there is one, so omitted colours
            // keep their existing values rather than reverting to an unrelated default.
            var baseline = _themes.TryGetValue(name, out var existing) ? existing : BuiltInThemes.NovaDark;
            _themes[name] = Apply(definition, name, baseline);
        }
    }

    /// <summary>Every theme, keyed by name.</summary>
    public IReadOnlyDictionary<string, TerminalTheme> Themes => _themes;

    /// <summary>The theme names, in a stable order suitable for a menu.</summary>
    public IReadOnlyList<string> Names => [.. _themes.Keys.OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>
    /// Returns a theme by name, falling back to the default when it is not recognised.
    /// </summary>
    /// <remarks>
    /// A misspelled theme name must not stop the terminal starting; the user needs the terminal to
    /// fix the typo.
    /// </remarks>
    public TerminalTheme GetOrDefault(string? name)
        => name is not null && _themes.TryGetValue(name, out var theme) ? theme : BuiltInThemes.NovaDark;

    /// <summary>
    /// Returns the theme after the named one, for cycling through them from the keyboard.
    /// </summary>
    public TerminalTheme GetNext(string? current)
    {
        var names = Names;

        if (names.Count == 0)
        {
            return BuiltInThemes.NovaDark;
        }

        var index = -1;

        for (var candidate = 0; candidate < names.Count; candidate++)
        {
            if (string.Equals(names[candidate], current, StringComparison.OrdinalIgnoreCase))
            {
                index = candidate;
                break;
            }
        }

        return _themes[names[(index + 1) % names.Count]];
    }

    /// <summary>Converts a configured definition into a theme, filling gaps from a baseline.</summary>
    private static TerminalTheme Apply(ThemeDefinition definition, string name, TerminalTheme baseline)
    {
        var palette = baseline.Palette;

        if (definition.Palette is { Count: TerminalTheme.NamedColorCount } configured)
        {
            var builder = ImmutableArray.CreateBuilder<RgbColor>(TerminalTheme.NamedColorCount);

            for (var index = 0; index < configured.Count; index++)
            {
                builder.Add(Parse(configured[index], baseline.Palette[index]));
            }

            palette = builder.ToImmutable();
        }

        return baseline with
        {
            Name = name,
            Background = Parse(definition.Background, baseline.Background),
            Foreground = Parse(definition.Foreground, baseline.Foreground),
            Cursor = Parse(definition.Cursor, baseline.Cursor),
            CursorText = Parse(definition.CursorText, baseline.CursorText),
            SelectionBackground = Parse(definition.SelectionBackground, baseline.SelectionBackground),
            Palette = palette,
        };
    }

    /// <summary>
    /// Parses a colour, keeping the fallback when it is missing or malformed.
    /// </summary>
    /// <remarks>
    /// Validation reports malformed colours separately. Silently keeping the old value here means a
    /// typo costs one colour rather than the whole theme.
    /// </remarks>
    private static RgbColor Parse(string? value, RgbColor fallback)
        => RgbColor.TryParse(value, out var parsed) ? parsed : fallback;
}
