using NovaTerminal.Core.Configuration;

namespace NovaTerminal.Core.Tests;

/// <summary>
/// Loading settings. The governing rule is that a bad settings file must never stop the terminal
/// starting: the terminal is the tool the user would need in order to fix it.
/// </summary>
public sealed class ConfigurationStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "nova-config-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void AMissingFileYieldsDefaults()
    {
        var result = ConfigurationStore.Load(Path.Combine(_directory, "absent.json"));

        Assert.False(result.Existed);
        Assert.True(result.IsClean);
        Assert.Equal(TerminalSize.Default, result.Options.Terminal.InitialSize);
    }

    [Fact]
    public void SettingsRoundTripThroughTheFile()
    {
        var options = new NovaTerminalOptions();
        options.Appearance.FontSize = 18;
        options.Appearance.ThemeName = "NovaLight";
        options.Appearance.CursorStyle = CursorStyle.Bar;
        options.Terminal.ScrollbackLines = 1234;
        options.Shell.Executable = "pwsh.exe";

        var path = Write(options);
        var result = ConfigurationStore.Load(path);

        Assert.True(result.IsClean);
        Assert.Equal(18, result.Options.Appearance.FontSize);
        Assert.Equal("NovaLight", result.Options.Appearance.ThemeName);
        Assert.Equal(CursorStyle.Bar, result.Options.Appearance.CursorStyle);
        Assert.Equal(1234, result.Options.Terminal.ScrollbackLines);
        Assert.Equal("pwsh.exe", result.Options.Shell.Executable);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        // The file is meant to be edited by a person, and JSON's strictness about both is a common
        // source of frustration.
        var path = WriteText("""
            {
              // The font this terminal uses.
              "Appearance": { "FontSize": 16, },
            }
            """);

        var result = ConfigurationStore.Load(path);

        Assert.True(result.IsClean);
        Assert.Equal(16, result.Options.Appearance.FontSize);
    }

    [Fact]
    public void MalformedJsonFallsBackToDefaultsAndReportsWhy()
    {
        var path = WriteText("{ this is not json");

        var result = ConfigurationStore.Load(path);

        Assert.True(result.Existed);
        Assert.NotEmpty(result.Problems);
        Assert.Equal(new NovaTerminalOptions().Appearance.FontSize, result.Options.Appearance.FontSize);
    }

    [Fact]
    public void AnInvalidValueIsReplacedWithoutDiscardingTheRestOfTheFile()
    {
        // A mistyped font size should not also cost the user their shell setting.
        var path = WriteText("""
            {
              "Appearance": { "FontSize": 9999 },
              "Shell": { "Executable": "pwsh.exe" }
            }
            """);

        var result = ConfigurationStore.Load(path);

        Assert.NotEmpty(result.Problems);
        Assert.Equal(new NovaTerminalOptions().Appearance.FontSize, result.Options.Appearance.FontSize);
        Assert.Equal("pwsh.exe", result.Options.Shell.Executable);
    }

    [Fact]
    public void UnknownPropertiesAreIgnored()
    {
        // Settings written by a newer version must not stop an older one from starting.
        var path = WriteText("""
            { "Appearance": { "FontSize": 15 }, "SomethingFromTheFuture": { "x": 1 } }
            """);

        var result = ConfigurationStore.Load(path);

        Assert.Equal(15, result.Options.Appearance.FontSize);
    }

    [Fact]
    public void AThemeCanBeDefinedInTheFile()
    {
        var path = WriteText("""
            {
              "Appearance": { "ThemeName": "Mine" },
              "Themes": {
                "Mine": { "Background": "#101010", "Foreground": "#EEEEEE" }
              }
            }
            """);

        var result = ConfigurationStore.Load(path);

        Assert.True(result.IsClean);
        var theme = Assert.Contains("Mine", result.Options.Themes);
        Assert.Equal("#101010", theme.Background);
    }

    [Fact]
    public void AMalformedColourIsReportedAgainstItsProperty()
    {
        var path = WriteText("""
            { "Themes": { "Mine": { "Background": "not-a-colour" } } }
            """);

        var result = ConfigurationStore.Load(path);

        var problem = Assert.Single(result.Problems);
        Assert.Contains("Themes.Mine.Background", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void APaletteMustDefineEverySlot()
    {
        var path = WriteText("""
            { "Themes": { "Mine": { "Palette": ["#000000", "#FFFFFF"] } } }
            """);

        var result = ConfigurationStore.Load(path);

        Assert.Contains(
            result.Problems,
            problem => problem.Contains("exactly 16", StringComparison.Ordinal));
    }

    [Fact]
    public void SavingCreatesTheDirectory()
    {
        var path = Path.Combine(_directory, "nested", ConfigurationStore.FileName);

        ConfigurationStore.Save(new NovaTerminalOptions(), path);

        Assert.True(File.Exists(path));
        Assert.True(ConfigurationStore.Load(path).IsClean);
    }

    [Fact]
    public void EnumsAreWrittenByNameSoTheFileStaysReadable()
    {
        var options = new NovaTerminalOptions();
        options.Appearance.CursorStyle = CursorStyle.Underline;
        var path = Write(options);

        Assert.Contains("Underline", File.ReadAllText(path), StringComparison.Ordinal);
    }

    private string Write(NovaTerminalOptions options)
    {
        var path = Path.Combine(_directory, ConfigurationStore.FileName);
        ConfigurationStore.Save(options, path);
        return path;
    }

    private string WriteText(string json)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, ConfigurationStore.FileName);
        File.WriteAllText(path, json);
        return path;
    }
}
