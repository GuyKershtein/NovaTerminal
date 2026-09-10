namespace NovaTerminal.Core.Diagnostics;

/// <summary>
/// Stable numeric identifiers for the events NovaTerminal logs.
/// </summary>
/// <remarks>
/// <para>
/// Declaring event IDs centrally keeps them unique and makes log output filterable by ID rather than
/// by message text, which is what makes the logs useful to a tool and not only to a human.
/// </para>
/// <para>
/// They are compile-time constants so that they can be used directly in
/// <c>[LoggerMessage]</c> attributes, which is how every log call site in the codebase is written.
/// IDs are grouped by subsystem in blocks of one thousand, so a new event never has to renumber an
/// existing one.
/// </para>
/// </remarks>
public static class LogEvents
{
    /// <summary>The application has begun start-up.</summary>
    public const int ApplicationStarting = 1_001;

    /// <summary>The application is shutting down normally.</summary>
    public const int ApplicationStopping = 1_002;

    /// <summary>An exception escaped to a top-level handler.</summary>
    public const int UnhandledException = 1_003;

    /// <summary>The main window finished initialising.</summary>
    public const int WindowInitialised = 1_004;

    /// <summary>A terminal session was created.</summary>
    public const int SessionCreated = 2_001;

    /// <summary>A terminal session was closed.</summary>
    public const int SessionClosed = 2_002;

    /// <summary>A shell process was launched inside a pseudo-terminal.</summary>
    public const int ShellStarted = 2_003;

    /// <summary>A shell process exited.</summary>
    public const int ShellExited = 2_004;

    /// <summary>A shell process could not be launched.</summary>
    public const int ShellStartFailed = 2_005;

    /// <summary>The pseudo-terminal was resized.</summary>
    public const int ShellResized = 2_006;

    /// <summary>
    /// The parser encountered a sequence it does not implement. Logged at debug level: hostile or
    /// merely exotic output must not be able to flood the log.
    /// </summary>
    public const int UnsupportedSequence = 3_001;

    /// <summary>The parser rejected a malformed or out-of-range sequence parameter.</summary>
    public const int MalformedSequence = 3_002;

    /// <summary>The terminal viewport was resized in response to a window layout change.</summary>
    public const int ViewportResized = 4_001;

    /// <summary>The active theme changed.</summary>
    public const int ThemeChanged = 4_002;

    /// <summary>Configuration was loaded successfully.</summary>
    public const int ConfigurationLoaded = 5_001;

    /// <summary>Configuration was invalid and defaults were substituted.</summary>
    public const int ConfigurationInvalid = 5_002;
}
