using NovaTerminal.Core;

namespace NovaTerminal.Core.Tests;

public sealed class TerminalColorTests
{
    [Fact]
    public void Default_IsTheDefaultStructValue()
    {
        // A freshly allocated screen buffer is all zero bytes; that must mean "use the theme's
        // colours", so no explicit initialisation pass over a million cells is ever needed.
        TerminalColor uninitialised = default;

        Assert.True(uninitialised.IsDefault);
        Assert.Equal(TerminalColorKind.Default, uninitialised.Kind);
        Assert.Equal(TerminalColor.Default, uninitialised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(231)]
    [InlineData(255)]
    public void FromIndex_RoundTripsThePaletteIndex(int index)
    {
        var color = TerminalColor.FromIndex((byte)index);

        Assert.Equal(TerminalColorKind.Indexed, color.Kind);
        Assert.Equal((byte)index, color.Index);
        Assert.False(color.IsDefault);
    }

    [Fact]
    public void FromAnsi_MapsNamedColoursToTheirPaletteIndices()
    {
        Assert.Equal(1, TerminalColor.FromAnsi(AnsiColor.Red).Index);
        Assert.Equal(9, TerminalColor.FromAnsi(AnsiColor.BrightRed).Index);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    [InlineData(18, 52, 86)]
    public void FromRgb_RoundTripsEveryComponent(int red, int green, int blue)
    {
        var color = TerminalColor.FromRgb((byte)red, (byte)green, (byte)blue);

        Assert.True(color.TryGetRgb(out var r, out var g, out var b));
        Assert.Equal((byte)red, r);
        Assert.Equal((byte)green, g);
        Assert.Equal((byte)blue, b);
    }

    [Fact]
    public void TryGetRgb_FailsForColoursThatNeedThemeResolution()
    {
        Assert.False(TerminalColor.Default.TryGetRgb(out _, out _, out _));
        Assert.False(TerminalColor.FromIndex(3).TryGetRgb(out _, out _, out _));
    }

    [Fact]
    public void Index_ThrowsWhenTheColourIsNotIndexed()
    {
        var rgb = TerminalColor.FromRgb(1, 2, 3);

        Assert.Throws<InvalidOperationException>(() => rgb.Index);
    }

    [Fact]
    public void Kinds_WithIdenticalPayloadsAreNotEqual()
    {
        // Palette index 1 and RGB #000001 share a payload; the kind tag must keep them distinct.
        Assert.NotEqual(TerminalColor.FromIndex(1), TerminalColor.FromRgb(0, 0, 1));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(TerminalColor.FromRgb(10, 20, 30), TerminalColor.FromRgb(10, 20, 30));
        Assert.True(TerminalColor.FromIndex(4) == TerminalColor.FromIndex(4));
        Assert.True(TerminalColor.FromIndex(4) != TerminalColor.FromIndex(5));
    }

    [Fact]
    public void Colour_FitsInFourBytes()
    {
        // Cell size drives the memory cost of the scrollback buffer; guard the packing.
        Assert.Equal(4, System.Runtime.InteropServices.Marshal.SizeOf<TerminalColor>());
    }

    [Fact]
    public void ToString_DescribesEachKind()
    {
        Assert.Equal("default", TerminalColor.Default.ToString());
        Assert.Equal("index:200", TerminalColor.FromIndex(200).ToString());
        Assert.Equal("#0A141E", TerminalColor.FromRgb(10, 20, 30).ToString());
    }
}
