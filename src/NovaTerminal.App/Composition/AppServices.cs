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

    /// <summary>
    /// Produces the options the application runs with. A file-backed configuration system arrives in
    /// a later milestone; until then the defaults are the configuration, and they are still routed
    /// through validation so the startup path is the real one from the beginning.
    /// </summary>
    private static NovaTerminalOptions LoadOptions()
    {
        var options = new NovaTerminalOptions();
        options.ThrowIfInvalid();
        return options;
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
