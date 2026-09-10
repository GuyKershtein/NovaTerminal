using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovaTerminal.App.Logging;
using NovaTerminal.Core;
using NovaTerminal.Core.Configuration;
using NovaTerminal.Rendering;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.App.Views;

/// <summary>
/// The application's main window: a terminal surface and a status line.
/// </summary>
/// <remarks>
/// Dependencies arrive through the constructor rather than being fetched from a static provider,
/// which is what lets the terminal view be constructed in tests and, later, once per tab.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly NovaTerminalOptions _options;
    private readonly ILogger<MainWindow> _logger;
    private readonly TerminalState _terminal;
    private readonly TerminalInterpreter _interpreter;
    private readonly AnsiParser _parser;
    private readonly TerminalView _view;

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

        var theme = BuiltInThemes.GetOrDefault(options.Appearance.ThemeName);

        _terminal = new TerminalState(options.Terminal.InitialSize);
        _interpreter = new TerminalInterpreter(_terminal);
        _parser = new AnsiParser(_interpreter);

        _view = new TerminalView(_terminal, theme, options.Appearance);
        _view.ViewportSizeChanged += OnViewportSizeChanged;
        TerminalHost.Child = _view;

        Background = new SolidColorBrush(
            Color.FromRgb(theme.Background.Red, theme.Background.Green, theme.Background.Blue));

        _interpreter.TitleChanged += OnTitleChanged;

        Title = $"{AppInfo.Name} {AppInfo.Version}";
        ShowStartupScreen();
        UpdateStatus();

        var size = _terminal.Size;
        _logger.WindowInitialised(size.Columns, size.Rows, options.Terminal.ScrollbackLines);
    }

    /// <summary>The terminal being displayed. Exposed so later milestones can attach a shell.</summary>
    public TerminalState Terminal => _terminal;

    /// <summary>Feeds bytes to the terminal exactly as a shell's output would arrive.</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        _parser.Parse(data);
        _view.InvalidateDamagedRows();
    }

    private void OnTitleChanged(object? sender, string title)
        => Title = string.IsNullOrWhiteSpace(title) ? AppInfo.Name : $"{title} — {AppInfo.Name}";

    private void OnViewportSizeChanged(object? sender, TerminalSize size)
    {
        if (size == _terminal.Size)
        {
            return;
        }

        _terminal.Resize(size);
        _view.InvalidateDamagedRows();
        UpdateStatus();
    }

    /// <summary>
    /// Writes a demonstration screen through the real parser.
    /// </summary>
    /// <remarks>
    /// Until a shell is attached, this is how the pipeline is exercised end to end: the bytes below
    /// travel through exactly the same parser, engine and renderer that a shell's output will.
    /// </remarks>
    private void ShowStartupScreen()
    {
        // "CSI" is the two-byte introducer every control sequence below starts with. Writing the
        // banner as raw sequences keeps this honest: it is exercising the real parser, not a
        // shortcut into the buffer.
        const string Csi = "\u001b[";

        // A shell ends its lines with a carriage return and a line feed, and so does this.
        const string NewLine = "\r\n";

        var banner =
            $"{Csi}1;34mNovaTerminal {AppInfo.Version}{Csi}0m  a terminal emulator in C#" + NewLine + NewLine +
            $"  colours      {Csi}31m red {Csi}32m green {Csi}33m yellow {Csi}34m blue " +
            $"{Csi}35m magenta {Csi}36m cyan {Csi}0m" + NewLine +
            $"  bright       {Csi}91m red {Csi}92m green {Csi}93m yellow {Csi}94m blue {Csi}0m" + NewLine +
            $"  true colour  {Csi}38;2;255;110;60m gradient {Csi}38;2;120;200;255m across " +
            $"{Csi}38;2;160;255;160m rgb {Csi}0m" + NewLine +
            $"  attributes   {Csi}1m bold {Csi}0m{Csi}3m italic {Csi}0m{Csi}4m underline {Csi}0m" +
            $"{Csi}7m inverse {Csi}0m{Csi}2m faint {Csi}0m{Csi}9m struck {Csi}0m" + NewLine +
            "  wide glyphs  中文 こんにちは 🚀" + NewLine + NewLine +
            $"{Csi}90m  Milestone 4: engine, parser and renderer are connected." + NewLine +
            $"  A real shell arrives in Milestone 5.{Csi}0m" + NewLine;

        Write(Encoding.UTF8.GetBytes(banner));
    }

    private void UpdateStatus()
    {
        var size = _terminal.Size;
        var metrics = _view.CellMetrics;

        StatusText.Text =
            $"{size.Columns}x{size.Rows} cells   " +
            $"cell {metrics.Width:0.##}x{metrics.Height:0.##} px   " +
            $"theme {_options.Appearance.ThemeName}   " +
            $"font {_options.Appearance.FontSize:0.#}pt";
    }
}
