using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaTerminal.Core.Configuration;

/// <summary>
/// The result of loading configuration: the settings to use, and anything that went wrong.
/// </summary>
/// <param name="Options">The settings, which are always usable even when problems were found.</param>
/// <param name="Path">The file that was read, or would have been.</param>
/// <param name="Existed">Whether the file was there at all.</param>
/// <param name="Problems">Human-readable descriptions of anything invalid.</param>
public sealed record ConfigurationResult(
    NovaTerminalOptions Options,
    string Path,
    bool Existed,
    IReadOnlyList<string> Problems)
{
    /// <summary>Whether the file loaded without complaint.</summary>
    public bool IsClean => Problems.Count == 0;
}

/// <summary>
/// Reads and writes NovaTerminal's settings file.
/// </summary>
/// <remarks>
/// <para>
/// <b>A bad configuration file must never stop the terminal starting.</b> A user editing settings by
/// hand will eventually leave a trailing comma or misspell a colour, and losing their terminal over
/// it - the very tool they would use to fix it - is the wrong outcome. So every failure here
/// degrades: unparseable files fall back to defaults, invalid values fall back individually, and
/// everything that went wrong is reported rather than thrown.
/// </para>
/// <para>
/// The reader accepts comments and trailing commas for the same reason. The file is meant to be
/// edited by a person, and JSON's strictness about both is a common source of frustration.
/// </para>
/// </remarks>
public static class ConfigurationStore
{
    /// <summary>Name of the settings file.</summary>
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// The directory NovaTerminal keeps its settings in, following the platform's convention.
    /// </summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify),
        "NovaTerminal");

    /// <summary>The default settings file path.</summary>
    public static string DefaultPath => Path.Combine(DefaultDirectory, FileName);

    /// <summary>
    /// Loads settings, falling back to defaults for anything missing or invalid.
    /// </summary>
    public static ConfigurationResult Load(string? path = null)
    {
        var resolved = path ?? DefaultPath;

        if (!File.Exists(resolved))
        {
            return new ConfigurationResult(new NovaTerminalOptions(), resolved, Existed: false, []);
        }

        NovaTerminalOptions? loaded;

        try
        {
            var json = File.ReadAllText(resolved);
            loaded = JsonSerializer.Deserialize<NovaTerminalOptions>(json, ReadOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable settings file is a problem to report, not a reason to refuse to start.
            return new ConfigurationResult(
                new NovaTerminalOptions(),
                resolved,
                Existed: true,
                [$"Could not read {resolved}: {exception.Message}"]);
        }

        if (loaded is null)
        {
            return new ConfigurationResult(
                new NovaTerminalOptions(), resolved, Existed: true, [$"{resolved} is empty."]);
        }

        var problems = new List<string>(loaded.Validate());

        foreach (var (name, definition) in loaded.Themes)
        {
            problems.AddRange(definition.Validate($"Themes.{name}"));
        }

        // Individual invalid values are replaced with their defaults rather than rejecting the whole
        // file: a mistyped font size should not also cost the user their colour scheme.
        if (problems.Count > 0)
        {
            loaded = Sanitise(loaded);
        }

        return new ConfigurationResult(loaded, resolved, Existed: true, problems);
    }

    /// <summary>
    /// Writes a settings file, creating the directory if needed.
    /// </summary>
    /// <remarks>
    /// Used to lay down a commented starting point on first run, so that a user who wants to
    /// configure something has a file to edit rather than having to invent one.
    /// </remarks>
    public static void Save(NovaTerminalOptions options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var resolved = path ?? DefaultPath;
        var directory = Path.GetDirectoryName(resolved);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(resolved, JsonSerializer.Serialize(options, WriteOptions));
    }

    /// <summary>
    /// Replaces values that failed validation with their defaults, keeping everything valid.
    /// </summary>
    private static NovaTerminalOptions Sanitise(NovaTerminalOptions options)
    {
        var defaults = new NovaTerminalOptions();

        if (HasProblems(options.Appearance, nameof(options.Appearance)))
        {
            options.Appearance = defaults.Appearance;
        }

        if (HasProblems(options.Terminal, nameof(options.Terminal)))
        {
            options.Terminal = defaults.Terminal;
        }

        if (HasProblems(options.Shell, nameof(options.Shell)))
        {
            options.Shell = defaults.Shell;
        }

        return options;
    }

    private static bool HasProblems(AppearanceOptions appearance, string path)
    {
        var errors = new List<string>();
        appearance.Validate(path, errors);
        return errors.Count > 0;
    }

    private static bool HasProblems(TerminalBehaviorOptions terminal, string path)
    {
        var errors = new List<string>();
        terminal.Validate(path, errors);
        return errors.Count > 0;
    }

    private static bool HasProblems(ShellOptions shell, string path)
    {
        var errors = new List<string>();
        shell.Validate(path, errors);
        return errors.Count > 0;
    }
}
