using NovaTerminal.Core;
using NovaTerminal.Core.Configuration;

namespace NovaTerminal.Core.Tests;

public sealed class NovaTerminalOptionsTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        Assert.Empty(new NovaTerminalOptions().Validate());
    }

    [Fact]
    public void Defaults_DescribeAUsableTerminal()
    {
        var options = new NovaTerminalOptions();

        Assert.Null(options.Shell.Executable);
        Assert.Equal(TerminalSize.Default, options.Terminal.InitialSize);
        Assert.Equal("xterm-256color", options.Terminal.TermName);
        Assert.True(options.Terminal.ScrollbackLines > 0);
        Assert.Equal(CursorStyle.Block, options.Appearance.CursorStyle);
    }

    [Fact]
    public void ThrowIfInvalid_IsSilentForValidOptions()
    {
        var exception = Record.Exception(() => new NovaTerminalOptions().ThrowIfInvalid());

        Assert.Null(exception);
    }

    [Fact]
    public void ThrowIfInvalid_ReportsEveryProblemAtOnce()
    {
        var options = new NovaTerminalOptions();
        options.Appearance.FontSize = 1_000;
        options.Terminal.ScrollbackLines = -1;

        var exception = Assert.Throws<InvalidOperationException>(options.ThrowIfInvalid);

        Assert.Contains(nameof(AppearanceOptions.FontSize), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(TerminalBehaviorOptions.ScrollbackLines), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(TerminalBehaviorOptions.MaxScrollbackLines + 1)]
    public void Scrollback_IsBounded(int lines)
    {
        // Scrollback is the one setting that can turn untrusted output into unbounded memory use,
        // so the ceiling is part of the contract rather than a suggestion.
        var options = new NovaTerminalOptions();
        options.Terminal.ScrollbackLines = lines;

        Assert.Single(options.Validate());
    }

    [Fact]
    public void Scrollback_MayBeDisabled()
    {
        var options = new NovaTerminalOptions();
        options.Terminal.ScrollbackLines = 0;

        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(AppearanceOptions.MaxFontSize + 1)]
    public void FontSize_IsBounded(double fontSize)
    {
        var options = new NovaTerminalOptions();
        options.Appearance.FontSize = fontSize;

        Assert.Single(options.Validate());
    }

    [Fact]
    public void CursorStyle_MustBeAKnownValue()
    {
        var options = new NovaTerminalOptions();
        options.Appearance.CursorStyle = (CursorStyle)99;

        Assert.Single(options.Validate());
    }

    [Fact]
    public void WorkingDirectory_MustExistWhenSpecified()
    {
        var options = new NovaTerminalOptions();
        options.Shell.WorkingDirectory = Path.Combine(Path.GetTempPath(), "nova-terminal-missing-" + Guid.NewGuid());

        Assert.Single(options.Validate());
    }

    [Fact]
    public void InitialSize_ClampsRatherThanThrowing()
    {
        // Validation reports the problem; the property still has to return something usable so a
        // bad configuration file degrades instead of crashing the renderer.
        var options = new NovaTerminalOptions();
        options.Terminal.InitialColumns = 0;

        Assert.NotEmpty(options.Validate());
        Assert.Equal(TerminalSize.MinColumns, options.Terminal.InitialSize.Columns);
    }
}
