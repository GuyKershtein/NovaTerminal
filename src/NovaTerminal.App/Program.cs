using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovaTerminal.App.Composition;
using NovaTerminal.App.Logging;
using NovaTerminal.Platform;
using Serilog;

namespace NovaTerminal.App;

/// <summary>Application entry point.</summary>
internal static class Program
{
    /// <summary>
    /// Starts the GUI. Marked <see cref="STAThreadAttribute"/> because Windows requires the thread
    /// that owns windows and the clipboard to use the single-threaded apartment model.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        using var services = AppServices.Build();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("NovaTerminal.App");

        try
        {
            // Describe() builds a string, so it is kept behind the level check rather than
            // evaluated unconditionally at every start-up.
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.ApplicationStarting(
                    AppInfo.Version, PlatformSupport.Describe(), AppServices.LogDirectory);
            }

            BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);

            logger.ApplicationStopping();
            return 0;
        }
        catch (Exception ex)
        {
            // Anything escaping to here has already broken the GUI, so the log file is the only
            // remaining channel through which to explain what happened.
            logger.UnhandledException(ex);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Builds the Avalonia application. This parameterless overload exists because the XAML
    /// previewer looks for exactly this signature.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(AppServices.Build());

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services)
        => AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
