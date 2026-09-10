using Microsoft.Extensions.Logging;
using NovaTerminal.Core.Diagnostics;

namespace NovaTerminal.Platform;

/// <summary>Source-generated log methods for the platform layer.</summary>
/// <remarks>
/// Session lifetime is logged, session <em>traffic</em> is not. A terminal moves megabytes a second
/// and logging any of it per byte would make the log useless and the terminal slow.
/// </remarks>
internal static partial class PlatformLog
{
    [LoggerMessage(
        EventId = LogEvents.ShellStarted,
        EventName = nameof(LogEvents.ShellStarted),
        Level = LogLevel.Information,
        Message = "Session {SessionId}: started '{Executable}' as process {ProcessId} at {Columns}x{Rows}.")]
    public static partial void ShellStarted(
        ILogger logger, string sessionId, string executable, int processId, int columns, int rows);

    [LoggerMessage(
        EventId = LogEvents.ShellExited,
        EventName = nameof(LogEvents.ShellExited),
        Level = LogLevel.Information,
        Message = "Session {SessionId}: shell exited with code {ExitCode}.")]
    public static partial void ShellExited(ILogger logger, string sessionId, int exitCode);

    [LoggerMessage(
        EventId = LogEvents.ShellStartFailed,
        EventName = nameof(LogEvents.ShellStartFailed),
        Level = LogLevel.Error,
        Message = "Session {SessionId}: could not start '{Executable}'.")]
    public static partial void ShellStartFailed(
        ILogger logger, Exception exception, string sessionId, string executable);

    [LoggerMessage(
        EventId = LogEvents.ShellResized,
        EventName = nameof(LogEvents.ShellResized),
        Level = LogLevel.Debug,
        Message = "Session {SessionId}: resized to {Columns}x{Rows}.")]
    public static partial void ShellResized(ILogger logger, string sessionId, int columns, int rows);
}
