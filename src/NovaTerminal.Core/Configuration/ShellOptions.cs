namespace NovaTerminal.Core.Configuration;

/// <summary>Describes the process NovaTerminal should launch inside a pseudo-terminal.</summary>
public sealed class ShellOptions
{
    /// <summary>
    /// The shell executable to launch. When <see langword="null"/> the platform backend picks a
    /// sensible default (PowerShell on Windows, the <c>SHELL</c> environment variable elsewhere).
    /// </summary>
    public string? Executable { get; set; }

    /// <summary>Arguments passed to <see cref="Executable"/>.</summary>
    public IList<string> Arguments { get; set; } = new List<string>();

    /// <summary>
    /// Working directory for the shell. When <see langword="null"/> the user's home directory is used.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Environment variables added to (or overriding) the inherited environment. NovaTerminal always
    /// sets <c>TERM</c> itself, so entries here should not attempt to.
    /// </summary>
    public IDictionary<string, string> Environment { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    internal void Validate(string path, ICollection<string> errors)
    {
        if (Executable is { Length: 0 })
        {
            errors.Add($"{path}.{nameof(Executable)} must be null (auto-detect) or a non-empty path.");
        }

        if (WorkingDirectory is { Length: > 0 } directory && !Directory.Exists(directory))
        {
            errors.Add($"{path}.{nameof(WorkingDirectory)} '{directory}' does not exist.");
        }

        foreach (var key in Environment.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add($"{path}.{nameof(Environment)} contains an entry with an empty name.");
                break;
            }
        }
    }
}
