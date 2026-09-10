using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovaTerminal.App.Composition;
using NovaTerminal.App.Logging;
using NovaTerminal.Core.Configuration;
using NovaTerminal.Platform;

namespace NovaTerminal.App.Views;

/// <summary>
/// The application's main window.
/// </summary>
/// <remarks>
/// Dependencies arrive through the constructor rather than being fetched from a static provider,
/// which is what will let the terminal view be constructed in tests and in multiple tabs later on.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly NovaTerminalOptions _options;
    private readonly ILogger<MainWindow> _logger;

    /// <summary>
    /// Design-time constructor. The XAML previewer and the runtime XAML loader need a public
    /// parameterless constructor; at run time the application always uses the injecting overload.
    /// </summary>
    public MainWindow()
        : this(new NovaTerminalOptions(), NullLogger<MainWindow>.Instance)
    {
    }

    /// <summary>Creates the main window with its dependencies.</summary>
    [ActivatorUtilitiesConstructor]
    public MainWindow(NovaTerminalOptions options, ILogger<MainWindow> logger)
    {
        _options = options;
        _logger = logger;

        InitializeComponent();

        Title = $"{AppInfo.Name} {AppInfo.Version}";
        SubtitleText.Text = "A terminal emulator written from scratch in C#.";
        PlatformText.Text = PlatformSupport.Describe();
        LogPathText.Text = $"logs: {AppServices.LogDirectory}";
        ConfigurationText.Text =
            $"shell: {_options.Shell.Executable ?? "(auto-detect)"}   " +
            $"size: {_options.Terminal.InitialSize}   " +
            $"scrollback: {_options.Terminal.ScrollbackLines} lines   " +
            $"font: {_options.Appearance.FontSize:0.#}pt";

        var size = _options.Terminal.InitialSize;
        _logger.WindowInitialised(size.Columns, size.Rows, _options.Terminal.ScrollbackLines);
    }
}
