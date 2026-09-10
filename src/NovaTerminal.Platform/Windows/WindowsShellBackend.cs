using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovaTerminal.Process;

namespace NovaTerminal.Platform.Windows;

/// <summary>
/// Creates shell sessions backed by the Windows pseudo console.
/// </summary>
public sealed class WindowsShellBackend : IShellBackend
{
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Creates the backend.</summary>
    public WindowsShellBackend(ILoggerFactory? loggerFactory = null)
        => _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

    /// <inheritdoc />
    public string Name => "ConPTY";

    /// <inheritdoc />
    public bool IsSupported => PlatformSupport.IsConPtyAvailable;

    /// <inheritdoc />
    /// <remarks>
    /// PowerShell 7 is preferred, then Windows PowerShell, then the command prompt. The order is by
    /// capability: <c>cmd.exe</c> works but supports far less of what a modern terminal can display.
    /// </remarks>
    public string GetDefaultShellExecutable()
    {
        foreach (var candidate in EnumerateShellCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
    }

    /// <inheritdoc />
    public IShellSession CreateSession(ShellSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!IsSupported)
        {
            throw new PlatformNotSupportedException(
                $"ConPTY needs Windows 10 build {PlatformSupport.MinimumConPtyBuild} or later.");
        }

        return new ConPtyShellSession(options, _loggerFactory.CreateLogger<WindowsShellBackend>());
    }

    private static IEnumerable<string> EnumerateShellCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);

        yield return Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        yield return Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        yield return Path.Combine(system, "cmd.exe");
    }
}
