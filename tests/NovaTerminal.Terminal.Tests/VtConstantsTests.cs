using NovaTerminal.Terminal;

namespace NovaTerminal.Terminal.Tests;

public sealed class VtConstantsTests
{
    [Theory]
    [InlineData(VtConstants.Nul, 0x00)]
    [InlineData(VtConstants.Bel, 0x07)]
    [InlineData(VtConstants.Backspace, 0x08)]
    [InlineData(VtConstants.Tab, 0x09)]
    [InlineData(VtConstants.LineFeed, 0x0A)]
    [InlineData(VtConstants.CarriageReturn, 0x0D)]
    [InlineData(VtConstants.Escape, 0x1B)]
    [InlineData(VtConstants.Delete, 0x7F)]
    public void ControlCharacters_MatchTheC0Standard(byte actual, int expected)
    {
        // These values are fixed by ECMA-48. If one ever changes, the parser is wrong, not the test.
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0x00, true)]
    [InlineData(0x1F, true)]
    [InlineData(0x20, false)]
    [InlineData((int)'A', false)]
    [InlineData(0x7F, false)]
    public void IsC0Control_CoversExactlyTheControlRange(int value, bool expected)
    {
        Assert.Equal(expected, VtConstants.IsC0Control((byte)value));
    }

    [Fact]
    public void SequenceIntroducers_AreTheExpectedAsciiCharacters()
    {
        Assert.Equal((byte)'[', VtConstants.CsiIntroducer);
        Assert.Equal((byte)']', VtConstants.OscIntroducer);
        Assert.Equal((byte)'P', VtConstants.DcsIntroducer);
        Assert.Equal((byte)';', VtConstants.ParameterSeparator);
        Assert.Equal((byte)':', VtConstants.SubParameterSeparator);
    }

    [Fact]
    public void ParameterLimits_AreBoundedForUntrustedInput()
    {
        // Terminal output is untrusted; a sequence must not be able to request unbounded work.
        Assert.InRange(VtConstants.MaxParameterCount, 1, 256);
        Assert.InRange(VtConstants.MaxParameterValue, 1, ushort.MaxValue);
    }
}
