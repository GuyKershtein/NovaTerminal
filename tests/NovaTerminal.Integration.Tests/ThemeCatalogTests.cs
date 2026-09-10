using NovaTerminal.Core;
using NovaTerminal.Core.Configuration;
using NovaTerminal.Rendering;

namespace NovaTerminal.Integration.Tests;

public sealed class ThemeCatalogTests
{
    [Fact]
    public void TheBuiltInThemesAreAlwaysAvailable()
    {
        var catalog = new ThemeCatalog();

        Assert.Contains("NovaDark", catalog.Names, StringComparer.Ordinal);
        Assert.Contains("NovaLight", catalog.Names, StringComparer.Ordinal);
        Assert.Contains("HighContrast", catalog.Names, StringComparer.Ordinal);
    }

    [Fact]
    public void AUserThemeIsAdded()
    {
        var catalog = new ThemeCatalog(new Dictionary<string, ThemeDefinition>
        {
            ["Mine"] = new() { Background = "#101010", Foreground = "#EEEEEE" },
        });

        var theme = catalog.GetOrDefault("Mine");

        Assert.Equal("Mine", theme.Name);
        Assert.Equal(RgbColor.Parse("#101010"), theme.Background);
        Assert.Equal(RgbColor.Parse("#EEEEEE"), theme.Foreground);
    }

    [Fact]
    public void AUserThemeInheritsWhateverItDoesNotDefine()
    {
        // Changing one colour of a shipped theme should not mean restating the other twenty.
        var catalog = new ThemeCatalog(new Dictionary<string, ThemeDefinition>
        {
            ["NovaDark"] = new() { Background = "#000000" },
        });

        var theme = catalog.GetOrDefault("NovaDark");

        Assert.Equal(RgbColor.Parse("#000000"), theme.Background);
        Assert.Equal(BuiltInThemes.NovaDark.Foreground, theme.Foreground);
        Assert.Equal(BuiltInThemes.NovaDark.Palette, theme.Palette);
    }

    [Fact]
    public void ACompletePaletteReplacesTheDefaultOne()
    {
        var palette = Enumerable.Range(0, TerminalTheme.NamedColorCount)
            .Select(index => $"#{index:X2}{index:X2}{index:X2}")
            .ToList();

        var catalog = new ThemeCatalog(new Dictionary<string, ThemeDefinition>
        {
            ["Grey"] = new() { Palette = palette },
        });

        var theme = catalog.GetOrDefault("Grey");

        Assert.Equal(RgbColor.Parse("#050505"), theme.Palette[5]);
        Assert.Equal(RgbColor.Parse("#050505"), theme.Resolve(TerminalColor.FromIndex(5), false));
    }

    [Fact]
    public void AMalformedColourKeepsTheOneItWouldHaveReplaced()
    {
        // Validation reports the problem separately; here a typo costs one colour, not the theme.
        var catalog = new ThemeCatalog(new Dictionary<string, ThemeDefinition>
        {
            ["Broken"] = new() { Background = "not-a-colour", Foreground = "#123456" },
        });

        var theme = catalog.GetOrDefault("Broken");

        Assert.Equal(BuiltInThemes.NovaDark.Background, theme.Background);
        Assert.Equal(RgbColor.Parse("#123456"), theme.Foreground);
    }

    [Fact]
    public void AnUnknownNameFallsBackToTheDefaultTheme()
    {
        var catalog = new ThemeCatalog();

        Assert.Equal(BuiltInThemes.NovaDark, catalog.GetOrDefault("does-not-exist"));
        Assert.Equal(BuiltInThemes.NovaDark, catalog.GetOrDefault(null));
    }

    [Fact]
    public void CyclingVisitsEveryThemeAndComesBack()
    {
        var catalog = new ThemeCatalog();
        var names = catalog.Names;
        var current = names[0];

        var visited = new List<string> { current };

        for (var step = 1; step < names.Count; step++)
        {
            current = catalog.GetNext(current).Name;
            visited.Add(current);
        }

        Assert.Equal(names.Count, visited.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(names[0], catalog.GetNext(current).Name);
    }

    [Fact]
    public void CyclingFromAnUnknownNameStartsAtTheBeginning()
    {
        var catalog = new ThemeCatalog();

        Assert.Equal(catalog.Names[0], catalog.GetNext("nonsense").Name);
    }
}
