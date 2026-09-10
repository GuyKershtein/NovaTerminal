namespace NovaTerminal.Process;

/// <summary>Lifecycle states of an <see cref="IShellSession"/>.</summary>
/// <remarks>
/// The progression is strictly forward: <see cref="NotStarted"/> to <see cref="Running"/> to
/// <see cref="Exited"/> or <see cref="Failed"/>. A session is never restarted; a new shell means a
/// new session object, which keeps the state machine trivial and free of resurrection bugs.
/// </remarks>
public enum ShellSessionStatus
{
    /// <summary>Created but not yet started.</summary>
    NotStarted = 0,

    /// <summary>The pseudo-terminal is open and the child process is running.</summary>
    Running = 1,

    /// <summary>The child process has exited and its output has been fully drained.</summary>
    Exited = 2,

    /// <summary>The session could not be started, or failed irrecoverably while running.</summary>
    Failed = 3,
}
