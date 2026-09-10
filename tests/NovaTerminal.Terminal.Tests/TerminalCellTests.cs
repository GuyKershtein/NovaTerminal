using System.Runtime.CompilerServices;
using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Terminal;

namespace NovaTerminal.Terminal.Tests;

public sealed class TerminalCellTests
{
    private static readonly CellStyle RedOnBlue = new(
        TerminalColor.FromAnsi(AnsiColor.Red),
        TerminalColor.FromAnsi(AnsiColor.Blue),
        TextAttributes.Bold);

    [Fact]
    public void DefaultCell_IsBlankInDefaultColours()
    {
        // A zeroed buffer must already be a valid blank screen, or every allocation would need an
        // initialisation pass over a million cells.
        TerminalCell cell = default;

        Assert.True(cell.IsEmpty);
        Assert.Equal(CellStyle.Default, cell.Style);
        Assert.Equal(CellRole.Normal, cell.Role);
        Assert.Equal(TerminalCell.Blank, cell.DisplayCharacter);
    }

    [Fact]
    public void Constructor_RoundTripsCharacterAndStyle()
    {
        var cell = new TerminalCell(new Rune('X'), RedOnBlue);

        Assert.Equal(new Rune('X'), cell.Character);
        Assert.Equal(RedOnBlue, cell.Style);
        Assert.Equal(RedOnBlue.Foreground, cell.Foreground);
        Assert.Equal(RedOnBlue.Background, cell.Background);
        Assert.Equal(TextAttributes.Bold, cell.Attributes);
        Assert.False(cell.IsEmpty);
    }

    [Fact]
    public void Empty_CarriesTheStyleSoErasedRegionsKeepTheirBackground()
    {
        var cell = TerminalCell.Empty(RedOnBlue);

        Assert.True(cell.IsEmpty);
        Assert.Equal(RedOnBlue.Background, cell.Background);
    }

    [Fact]
    public void WideTrailing_IsAPlaceholderWithNoCharacter()
    {
        var cell = TerminalCell.WideTrailing(CellStyle.Default);

        Assert.True(cell.IsWideTrailing);
        Assert.True(cell.IsWidePart);
        Assert.False(cell.IsWideLeading);
        Assert.True(cell.IsEmpty);
    }

    [Fact]
    public void WithStyle_ReplacesStylingAndKeepsEverythingElse()
    {
        var original = new TerminalCell(new Rune(0x4E2D), CellStyle.Default, CellRole.WideLeading);

        var restyled = original.WithStyle(RedOnBlue);

        Assert.Equal(original.Character, restyled.Character);
        Assert.Equal(CellRole.WideLeading, restyled.Role);
        Assert.Equal(RedOnBlue, restyled.Style);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        var first = new TerminalCell(new Rune('A'), RedOnBlue);
        var second = new TerminalCell(new Rune('A'), RedOnBlue);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.True(first != new TerminalCell(new Rune('B'), RedOnBlue));
        Assert.True(first != new TerminalCell(new Rune('A'), CellStyle.Default));
    }

    [Fact]
    public void Cell_FitsInSixteenBytes()
    {
        // Cell size multiplies by every cell of every scrollback line, so the flat field layout
        // that achieves this is a deliberate design decision worth guarding.
        Assert.True(
            Unsafe.SizeOf<TerminalCell>() <= 16,
            $"TerminalCell grew to {Unsafe.SizeOf<TerminalCell>()} bytes.");
    }
}
