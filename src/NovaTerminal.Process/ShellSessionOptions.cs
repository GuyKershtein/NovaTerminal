using NovaTerminal.Core;

namespace NovaTerminal.Process;

/// <summary>
/// Everything a backend needs in order to launch one shell inside a pseudo-terminal.
/// </summary>
/// <remarks>
/// This is deliberately not the application's configuration type. The composition root translates
/// user configuration into a <see cref="ShellSessionOptions"/>, which keeps this layer independent
/// of how settings happen to be stored.
/// </remarks>
/// <param name="Executable">Absolute or resolvable path of the program to launch.</param>
/// <param name="Arguments">Arguments passed to the program.</param>
/// <param name="WorkingDirectory">Initial working directory, or <see langword="null"/> to inherit.</param>
/// <param name="Environment">Environment entries added to or overriding the inherited environment.</param>
/// <param name="InitialSize">Size the pseudo-terminal is created with.</param>
public sealed record ShellSessionOptions(
    string Executable,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    TerminalSize InitialSize)
{
    /// <summary>Creates options for a shell with no arguments and an inherited environment.</summary>
    public ShellSessionOptions(string executable, TerminalSize initialSize)
        : this(executable, [], null, new Dictionary<string, string>(StringComparer.Ordinal), initialSize)
    {
    }
}
