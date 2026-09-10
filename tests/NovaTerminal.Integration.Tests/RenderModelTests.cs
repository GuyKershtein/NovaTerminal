using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Rendering;
using NovaTerminal.Terminal;

namespace NovaTerminal.Integration.Tests;

/// <summary>
/// The rendering pipeline, tested without a window. This is the payoff for keeping the render model
/// free of any GUI toolkit.
/// </summary>
public sealed class RenderModelTests
{
    private static readonly CellStyle Red = CellStyle.Default
        .WithForeground(TerminalColor.FromAnsi(AnsiColor.Red));

    [Fact]
    public void AUniformRow_BecomesASingleRun()
    {
        // Eighty plain characters is one text-drawing call, not eighty. Text shaping dominates the
        // cost of drawing, so this is the difference that makes a terminal feel fast.
        var terminal = new TerminalState(new TerminalSize(20, 3));
        terminal.Print("hello world");

        var runs = new RowRunBuilder().Build(terminal.Buffer.GetRow(0));

        var run = Assert.Single(runs);
        Assert.Equal(0, run.Column);
        Assert.Equal(20, run.Length);
    }

    [Fact]
    public void AStyleChange_StartsANewRun()
    {
        var terminal = new TerminalState(new TerminalSize(12, 3));
        terminal.Print("ab");
        terminal.CurrentStyle = Red;
        terminal.Print("cd");
        terminal.CurrentStyle = CellStyle.Default;

        var runs = new RowRunBuilder().Build(terminal.Buffer.GetRow(0));

        Assert.Equal(3, runs.Count);
        Assert.Equal((0, 2), (runs[0].Column, runs[0].Length));
        Assert.Equal((2, 2), (runs[1].Column, runs[1].Length));
        Assert.Equal(Red, runs[1].Style);
        Assert.Equal(4, runs[2].Column);
    }

    [Fact]
    public void BlankRuns_AreMarkedSoTheyCanBeSkipped()
    {
        var terminal = new TerminalState(new TerminalSize(10, 2));
        terminal.Print("hi");

        var runs = new RowRunBuilder().Build(terminal.Buffer.GetRow(0));

        // Everything is one run here because nothing changed style; what matters is that a run of
        // untouched cells is not reported as blank when it contains text.
        Assert.False(Assert.Single(runs).IsBlank);
        Assert.True(Assert.Single(new RowRunBuilder().Build(terminal.Buffer.GetRow(1))).IsBlank);
    }

    [Fact]
    public void RunText_SkipsWideCharacterPlaceholders()
    {
        var terminal = new TerminalState(new TerminalSize(10, 2));
        terminal.Print("中x");

        var builder = new RowRunBuilder();
        var row = terminal.Buffer.GetRow(0);
        var run = builder.Build(row)[0];

        // The placeholder's glyph was already drawn by the leading cell and spills over it. Emitting
        // anything for it would draw a stray character.
        Assert.StartsWith("中x", builder.GetRunText(row, in run), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyRow_ProducesNoRuns()
    {
        Assert.Empty(new RowRunBuilder().Build(ReadOnlySpan<TerminalCell>.Empty));
    }

    [Fact]
    public void TheBuilderIsReusableAcrossRows()
    {
        // The builder reuses its buffers so that a frame allocates nothing; that also means a
        // caller must not hold on to the previous row's runs, which this documents.
        var terminal = new TerminalState(new TerminalSize(8, 2));
        terminal.Print("aaaa");
        terminal.MoveCursorTo(0, 1);
        terminal.Print("bb");

        var builder = new RowRunBuilder();
        builder.Build(terminal.Buffer.GetRow(0));
        var second = builder.Build(terminal.Buffer.GetRow(1));

        Assert.Single(second);
    }

    // ----- Theme resolution -----

    [Fact]
    public void DefaultColours_ResolveToTheThemeNotToBlackAndWhite()
    {
        var theme = BuiltInThemes.NovaDark;

        Assert.Equal(theme.Foreground, theme.Resolve(TerminalColor.Default, isBackground: false));
        Assert.Equal(theme.Background, theme.Resolve(TerminalColor.Default, isBackground: true));
    }

    [Fact]
    public void NamedColours_ResolveThroughThePalette()
    {
        var theme = BuiltInThemes.NovaDark;

        Assert.Equal(theme.Palette[1], theme.Resolve(TerminalColor.FromAnsi(AnsiColor.Red), false));
        Assert.Equal(theme.Palette[9], theme.Resolve(TerminalColor.FromAnsi(AnsiColor.BrightRed), false));
    }

    [Fact]
    public void TrueColour_BypassesTheThemeEntirely()
    {
        var color = TerminalColor.FromRgb(1, 2, 3);

        Assert.Equal(new RgbColor(1, 2, 3), BuiltInThemes.NovaDark.Resolve(color, false));
    }

    [Theory]
    [InlineData(16, 0, 0, 0)]        // First entry of the colour cube is black.
    [InlineData(21, 0, 0, 255)]      // Pure blue corner.
    [InlineData(196, 255, 0, 0)]     // Pure red corner.
    [InlineData(231, 255, 255, 255)] // Last entry of the cube is white.
    [InlineData(232, 8, 8, 8)]       // First step of the greyscale ramp.
    [InlineData(255, 238, 238, 238)] // Last step.
    public void TheStandardPaletteIsComputedNotStored(int index, int red, int green, int blue)
    {
        // Entries 16 to 255 are defined identically in every terminal, so a theme that redefined
        // them would be wrong rather than merely different.
        Assert.Equal(new RgbColor((byte)red, (byte)green, (byte)blue), BuiltInThemes.NovaDark.ResolveIndex(index));
    }

    [Fact]
    public void EveryBuiltInThemeIsValid()
    {
        foreach (var theme in BuiltInThemes.All.Values)
        {
            Assert.Empty(theme.Validate());
            Assert.Equal(TerminalTheme.NamedColorCount, theme.Palette.Length);
        }
    }

    [Fact]
    public void AnUnknownThemeNameFallsBackRatherThanFailing()
    {
        // A misspelled theme in a configuration file must not stop the terminal from starting.
        Assert.Equal(BuiltInThemes.NovaDark, BuiltInThemes.GetOrDefault("does-not-exist"));
        Assert.Equal(BuiltInThemes.NovaDark, BuiltInThemes.GetOrDefault(null));
        Assert.Equal(BuiltInThemes.NovaLight, BuiltInThemes.GetOrDefault("novalight"));
    }

    [Fact]
    public void ColoursRoundTripThroughHexText()
    {
        Assert.Equal(new RgbColor(0x11, 0x22, 0x33), RgbColor.Parse("#112233"));
        Assert.Equal(new RgbColor(0x11, 0x22, 0x33), RgbColor.Parse("112233"));
        Assert.Equal("#112233", new RgbColor(0x11, 0x22, 0x33).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("xyzxyz")]
    [InlineData("$112233")]
    public void MalformedColourTextIsRejected(string text)
    {
        Assert.False(RgbColor.TryParse(text, out _));
    }

    [Fact]
    public void BlendingProducesTheFaintAttribute()
    {
        // Faint means reduced intensity, which has to be computed against the actual background so
        // that it works on a light theme as well as a dark one.
        var blended = new RgbColor(200, 200, 200).Blend(new RgbColor(0, 0, 0), 0.5);

        Assert.Equal(new RgbColor(100, 100, 100), blended);
    }
}
