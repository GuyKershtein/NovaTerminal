using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovaTerminal.Core.Configuration;
using Serilog;
using Serilog.Events;

namespace NovaTerminal.App.Composition;

/// <summary>
/// The composition root: the single place where concrete implementations are chosen and wired
/// together. Every other type receives what it needs through its constructor and never reaches for
/// a service locator.
/// </summary>
internal static class AppServices
{
    private const string LogFileTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}";

    private const int RetainedLogFileCount = 5;
    private const long LogFileSizeLimitBytes = 16L * 1024 * 1024;

    /// <summary>Builds the application's service provider.</summary>
    public static ServiceProvider Build()
    {
        var options = LoadOptions();

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton(Configuration);
        services.AddSingleton(options.Appearance);
        services.AddSingleton(options.Terminal);
        services.AddSingleton(options.Shell);

        AddLogging(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    /// <summary>The directory NovaTerminal writes its logs to.</summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovaTerminal",
        "logs");

    /// <summary>What happened when settings were loaded, for logging once a logger exists.</summary>
    public static ConfigurationResult Configuration { get; private set; } =
        new(new NovaTerminalOptions(), ConfigurationStore.DefaultPath, Existed: false, []);

    /// <summary>
    /// Loads the settings file, falling back to defaults for anything missing or invalid.
    /// </summary>
    /// <remarks>
    /// A first run leaves a settings file behind, so a user who wants to change something has a
    /// file to edit rather than having to invent one from the documentation.
    /// </remarks>
    private static NovaTerminalOptions LoadOptions()
    {
        Configuration = ConfigurationStore.Load();

        if (!Configuration.Existed)
        {
            try
            {
                ConfigurationStore.Save(Configuration.Options);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A read-only or unwritable profile is not a reason to refuse to start.
            }
        }

        return Configuration.Options;
    }

    private static void AddLogging(IServiceCollection services)
    {
        Directory.CreateDirectory(LogDirectory);

        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Debug)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: LogFileTemplate, formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                Path.Combine(LogDirectory, "novaterminal-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedLogFileCount,
                fileSizeLimitBytes: LogFileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                outputTemplate: LogFileTemplate,
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

        Log.Logger = logger;

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(logger, dispose: true);
        });
    }
}
