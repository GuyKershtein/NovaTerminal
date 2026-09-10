namespace NovaTerminal.Process;

/// <summary>Reports how a shell session ended.</summary>
/// <param name="exitCode">
/// The process exit code, or <see langword="null"/> when the process never started or was killed
/// before a code could be observed.
/// </param>
/// <param name="error">
/// The failure that ended the session, or <see langword="null"/> for a normal exit.
/// </param>
public sealed class ShellExitedEventArgs(int? exitCode, Exception? error = null) : EventArgs
{
    /// <summary>The process exit code, when one was observed.</summary>
    public int? ExitCode { get; } = exitCode;

    /// <summary>The failure that ended the session, if it did not end normally.</summary>
    public Exception? Error { get; } = error;

    /// <summary>True when the session ended because of a failure rather than a normal exit.</summary>
    public bool IsFailure => Error is not null;
}
