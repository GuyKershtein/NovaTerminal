using Microsoft.Extensions.Logging;
using NovaTerminal.Platform.Windows;
using NovaTerminal.Process;

namespace NovaTerminal.Platform;

/// <summary>
/// Chooses the pseudo-terminal backend for the machine the application is running on.
/// </summary>
/// <remarks>
/// This is the one place that knows which backends exist. Everything above it receives an
/// <see cref="IShellBackend"/> and never asks what kind it is, which is what will let a Unix
/// backend be added by changing this method and nothing else.
/// </remarks>
public static class ShellBackendFactory
{
    /// <summary>Returns a backend suitable for this machine.</summary>
    /// <exception cref="PlatformNotSupportedException">No backend is available here.</exception>
    public static IShellBackend Create(ILoggerFactory? loggerFactory = null)
    {
        if (PlatformSupport.IsConPtyAvailable)
        {
            return new WindowsShellBackend(loggerFactory);
        }

        PlatformSupport.ThrowIfNoPtyAvailable();

        // Reached only on a Unix host, where the capability probe reports a pseudo-terminal is
        // available but the backend has not been written yet.
        throw new PlatformNotSupportedException(
            "A Unix pseudo-terminal backend is not implemented yet. " +
            "The abstraction is in place; only the platform layer is missing.");
    }
}
