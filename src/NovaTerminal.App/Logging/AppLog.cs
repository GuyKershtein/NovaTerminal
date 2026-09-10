using Microsoft.Extensions.Logging;
using NovaTerminal.Core.Diagnostics;

namespace NovaTerminal.App.Logging;

/// <summary>
/// Source-generated log methods for the application layer.
/// </summary>
/// <remarks>
/// <para>
/// Every log call site in NovaTerminal goes through a <c>[LoggerMessage]</c> method rather than
/// calling <c>logger.LogInformation("...", args)</c> directly. The generator emits a cached
/// delegate and a level check, so when a level is disabled the arguments are never boxed, no
/// <c>object[]</c> is allocated, and the message template is never re-parsed.
/// </para>
/// <para>
/// That distinction stops being academic in a terminal: the render and parse paths run thousands of
/// times a second, and a disabled debug log on such a path must cost nothing.
/// </para>
/// </remarks>
internal static partial class AppLog
{
    [LoggerMessage(
        EventId = LogEvents.ApplicationStarting,
        EventName = nameof(LogEvents.ApplicationStarting),
        Level = LogLevel.Information,
        Message = "NovaTerminal {Version} starting on {Platform}. Logging to {LogDirectory}.")]
    public static partial void ApplicationStarting(
        this ILogger logger, string version, string platform, string logDirectory);

    [LoggerMessage(
        EventId = LogEvents.ApplicationStopping,
        EventName = nameof(LogEvents.ApplicationStopping),
        Level = LogLevel.Information,
        Message = "NovaTerminal exited normally.")]
    public static partial void ApplicationStopping(this ILogger logger);

    [LoggerMessage(
        EventId = LogEvents.UnhandledException,
        EventName = nameof(LogEvents.UnhandledException),
        Level = LogLevel.Critical,
        Message = "NovaTerminal terminated unexpectedly.")]
    public static partial void UnhandledException(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = LogEvents.WindowInitialised,
        EventName = nameof(LogEvents.WindowInitialised),
        Level = LogLevel.Debug,
        Message = "Main window initialised: {Columns}x{Rows} terminal, {ScrollbackLines} lines of scrollback.")]
    public static partial void WindowInitialised(
        this ILogger logger, int columns, int rows, int scrollbackLines);

    [LoggerMessage(
        EventId = LogEvents.ConfigurationLoaded,
        EventName = nameof(LogEvents.ConfigurationLoaded),
        Level = LogLevel.Debug,
        Message = "Configuration loaded and validated.")]
    public static partial void ConfigurationLoaded(this ILogger logger);

    [LoggerMessage(
        EventId = LogEvents.SessionCreated,
        EventName = nameof(LogEvents.SessionCreated),
        Level = LogLevel.Information,
        Message = "Session {SessionId} created running {Shell} at {Columns}x{Rows}.")]
    public static partial void SessionCreated(
        this ILogger logger, string sessionId, string shell, int columns, int rows);

    [LoggerMessage(
        EventId = LogEvents.SessionClosed,
        EventName = nameof(LogEvents.SessionClosed),
        Level = LogLevel.Information,
        Message = "Session {SessionId} closed.")]
    public static partial void SessionClosed(this ILogger logger, string sessionId);

    [LoggerMessage(
        EventId = LogEvents.UnhandledException,
        EventName = "SessionPumpFailed",
        Level = LogLevel.Error,
        Message = "Session {SessionId}: the output pump stopped unexpectedly.")]
    public static partial void SessionPumpFailed(this ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(
        EventId = LogEvents.ShellStartFailed,
        EventName = nameof(LogEvents.ShellStartFailed),
        Level = LogLevel.Error,
        Message = "Could not start a shell session.")]
    public static partial void SessionStartFailed(this ILogger logger, Exception exception);
}
