namespace NovaTerminal.Core.Configuration;

/// <summary>Settings that govern the terminal engine itself rather than its presentation.</summary>
public sealed class TerminalBehaviorOptions
{
    /// <summary>
    /// Hard ceiling on scrollback retention. Terminal output is untrusted and potentially infinite,
    /// so the engine must never be configurable into unbounded memory growth.
    /// </summary>
    public const int MaxScrollbackLines = 1_000_000;

    /// <summary>Columns used before the GUI reports a real viewport size.</summary>
    public int InitialColumns { get; set; } = 80;

    /// <summary>Rows used before the GUI reports a real viewport size.</summary>
    public int InitialRows { get; set; } = 24;

    /// <summary>
    /// How many lines that have scrolled off the top of the screen are retained. Zero disables
    /// scrollback entirely.
    /// </summary>
    public int ScrollbackLines { get; set; } = 5_000;

    /// <summary>Number of columns a horizontal tab advances by default.</summary>
    public int TabWidth { get; set; } = 8;

    /// <summary>The <c>TERM</c> value advertised to the child process.</summary>
    public string TermName { get; set; } = "xterm-256color";

    /// <summary>The startup dimensions expressed as a validated <see cref="TerminalSize"/>.</summary>
    public TerminalSize InitialSize => TerminalSize.Clamp(InitialColumns, InitialRows);

    internal void Validate(string path, ICollection<string> errors)
    {
        if (InitialColumns is < TerminalSize.MinColumns or > TerminalSize.MaxColumns)
        {
            errors.Add(
                $"{path}.{nameof(InitialColumns)} must be between " +
                $"{TerminalSize.MinColumns} and {TerminalSize.MaxColumns}.");
        }

        if (InitialRows is < TerminalSize.MinRows or > TerminalSize.MaxRows)
        {
            errors.Add(
                $"{path}.{nameof(InitialRows)} must be between " +
                $"{TerminalSize.MinRows} and {TerminalSize.MaxRows}.");
        }

        if (ScrollbackLines is < 0 or > MaxScrollbackLines)
        {
            errors.Add($"{path}.{nameof(ScrollbackLines)} must be between 0 and {MaxScrollbackLines}.");
        }

        if (TabWidth is < 1 or > TerminalSize.MaxColumns)
        {
            errors.Add($"{path}.{nameof(TabWidth)} must be between 1 and {TerminalSize.MaxColumns}.");
        }

        if (string.IsNullOrWhiteSpace(TermName))
        {
            errors.Add($"{path}.{nameof(TermName)} must not be empty.");
        }
    }
}
