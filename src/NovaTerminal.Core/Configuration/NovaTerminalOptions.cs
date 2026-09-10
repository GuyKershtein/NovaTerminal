namespace NovaTerminal.Core.Configuration;

/// <summary>
/// Root of the strongly typed configuration model. Bound from configuration sources at startup and
/// injected wherever settings are needed; nothing in the codebase reads configuration ad hoc.
/// </summary>
public sealed class NovaTerminalOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "NovaTerminal";

    /// <summary>The shell process to launch.</summary>
    public ShellOptions Shell { get; set; } = new();

    /// <summary>Fonts, colours and cursor presentation.</summary>
    public AppearanceOptions Appearance { get; set; } = new();

    /// <summary>Engine behaviour such as scrollback retention and initial size.</summary>
    public TerminalBehaviorOptions Terminal { get; set; } = new();

    /// <summary>
    /// Checks every section and returns a human-readable description of each problem found.
    /// Returning the errors rather than throwing lets the caller choose between failing fast and
    /// reporting every problem at once, and makes the rules straightforward to unit test.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        Shell.Validate(nameof(Shell), errors);
        Appearance.Validate(nameof(Appearance), errors);
        Terminal.Validate(nameof(Terminal), errors);
        return errors;
    }

    /// <summary>Validates the options and throws if any rule is violated.</summary>
    /// <exception cref="InvalidOperationException">One or more settings are invalid.</exception>
    public void ThrowIfInvalid()
    {
        var errors = Validate();
        if (errors.Count == 0)
        {
            return;
        }

        var separator = System.Environment.NewLine + " - ";
        throw new InvalidOperationException(
            "NovaTerminal configuration is invalid:" + separator + string.Join(separator, errors));
    }
}
