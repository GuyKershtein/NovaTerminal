using System.Reflection;

namespace NovaTerminal.App;

/// <summary>Identity of the running build, read from assembly metadata rather than duplicated.</summary>
internal static class AppInfo
{
    /// <summary>The product name shown in window titles and logs.</summary>
    public const string Name = "NovaTerminal";

    /// <summary>
    /// The informational version produced by the build. Any build metadata suffix (the "+" and the
    /// commit hash appended by source-link style builds) is trimmed for display.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
