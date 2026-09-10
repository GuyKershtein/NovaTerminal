namespace NovaTerminal.Process;

/// <summary>
/// Creates shell sessions using a particular operating system's pseudo-terminal facility.
/// </summary>
/// <remarks>
/// Windows (ConPTY) and Unix (<c>forkpty</c>/<c>openpty</c>) expose fundamentally different APIs for
/// the same concept. This interface is the seam between them: the rest of NovaTerminal knows only
/// that it can obtain an <see cref="IShellSession"/>, and every platform-specific detail - handle
/// lifetimes, attribute lists, ioctl numbers - stays inside a backend implementation.
/// </remarks>
public interface IShellBackend
{
    /// <summary>A short name for the mechanism, used in logs and diagnostics (for example "ConPTY").</summary>
    string Name { get; }

    /// <summary>
    /// Whether this backend can run on the current machine. Availability is not the same as being
    /// on the right operating system: ConPTY, for example, requires Windows 10 1809 or later.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Returns the shell to launch when the user has not configured one explicitly.
    /// </summary>
    string GetDefaultShellExecutable();

    /// <summary>
    /// Creates a session. The pseudo-terminal is not opened and no process is launched until
    /// <see cref="IShellSession.StartAsync"/> is called, so construction cannot fail for
    /// environmental reasons.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// This backend is not usable on the current machine.
    /// </exception>
    IShellSession CreateSession(ShellSessionOptions options);
}
