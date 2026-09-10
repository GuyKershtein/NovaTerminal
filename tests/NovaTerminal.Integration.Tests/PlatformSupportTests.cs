using NovaTerminal.Platform;

namespace NovaTerminal.Integration.Tests;

/// <summary>
/// Verifies the platform capability probe. These assertions are deliberately conditional on the
/// host: a capability check that claimed the same answer everywhere would not be checking anything.
/// </summary>
public sealed class PlatformSupportTests
{
    [Fact]
    public void Describe_ReportsTheHostAndItsPseudoTerminalMechanism()
    {
        var description = PlatformSupport.Describe();

        Assert.False(string.IsNullOrWhiteSpace(description));
        Assert.Contains("pseudo-terminal:", description, StringComparison.Ordinal);
    }

    [Fact]
    public void ConPty_IsReportedAvailableOnlyOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(PlatformSupport.IsConPtyAvailable);
            return;
        }

        // Every Windows build supported by .NET 10 is newer than 1809, so on any machine that can
        // run these tests at all, ConPTY must be reported as present.
        Assert.True(PlatformSupport.IsConPtyAvailable);
        Assert.True(Environment.OSVersion.Version.Build >= PlatformSupport.MinimumConPtyBuild);
    }

    [Fact]
    public void UnixPty_IsReportedAvailableOnlyOnUnix()
    {
        Assert.Equal(
            OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
            PlatformSupport.IsUnixPtyAvailable);
    }

    [Fact]
    public void SupportedHosts_DoNotThrowOnTheStartupCheck()
    {
        if (!PlatformSupport.IsAnyPtyAvailable)
        {
            Assert.Throws<PlatformNotSupportedException>(PlatformSupport.ThrowIfNoPtyAvailable);
            return;
        }

        Assert.Null(Record.Exception(PlatformSupport.ThrowIfNoPtyAvailable));
    }
}
