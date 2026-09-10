using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovaTerminal.App.Logging;
using NovaTerminal.App.Sessions;
using NovaTerminal.Core;
using NovaTerminal.Core.Configuration;
using NovaTerminal.Platform;
using NovaTerminal.Process;
using NovaTerminal.Rendering;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.App.Views;

/// <summary>
/// The application's main window: a terminal surface, a shell session and a status line.
/// </summary>
/// <remarks>
/// Dependencies arrive through the constructor rather than being fetched from a static provider,
/// which is what lets the terminal view be constructed in tests and, later, once per tab.
/// </remarks>
public partial class MainWindow : Window, IDisposable
{
    private readonly NovaTerminalOptions _options;
    private readonly ILogger<MainWindow> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TerminalState _terminal;
    private readonly TerminalView _view;

    private TerminalSession? _session;

    /// <summary>
    /// Design-time constructor. The XAML previewer and the runtime XAML loader need a public
    /// parameterless constructor; at run time the application always uses the injecting overload.
    /// </summary>
    public MainWindow()
        : this(new NovaTerminalOptions(), NullLogger<MainWindow>.Instance, NullLoggerFactory.Instance)
    {
    }

    /// <summary>Creates the main window with its dependencies.</summary>
    [ActivatorUtilitiesConstructor]
    public MainWindow(NovaTerminalOptions options, ILogger<MainWindow> logger, ILoggerFactory loggerFactory)
    {
        _options = options;
        _logger = logger;
        _loggerFactory = loggerFactory;

        InitializeComponent();

        var theme = BuiltInThemes.GetOrDefault(options.Appearance.ThemeName);

        _terminal = new TerminalState(options.Terminal.InitialSize);

        _view = new TerminalView(_terminal, theme, options.Appearance);
        _view.ViewportSizeChanged += OnViewportSizeChanged;
        _view.InputProduced += OnInputProduced;
        TerminalHost.Child = _view;

        Background = new SolidColorBrush(
            Color.FromRgb(theme.Background.Red, theme.Background.Green, theme.Background.Blue));

        Title = $"{AppInfo.Name} {AppInfo.Version}";
        UpdateStatus();

        Opened += OnOpened;
        Closing += OnClosing;

        var size = _terminal.Size;
        _logger.WindowInitialised(size.Columns, size.Rows, options.Terminal.ScrollbackLines);
    }

    /// <summary>The terminal being displayed.</summary>
    public TerminalState Terminal => _terminal;

    /// <summary>The running session, once a shell has been started.</summary>
    public TerminalSession? Session => _session;

    /// <summary>Disposes the session if the window is torn down without closing normally.</summary>
    public void Dispose()
    {
        _session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _session = null;
        GC.SuppressFinalize(this);
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            await StartShellAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.SessionStartFailed(exception);
            ShowFailure(exception);
        }
    }

    private async Task StartShellAsync()
    {
        var backend = ShellBackendFactory.Create(_loggerFactory);
        var executable = _options.Shell.Executable ?? backend.GetDefaultShellExecutable();

        var environment = new Dictionary<string, string>(_options.Shell.Environment, StringComparer.Ordinal)
        {
            // TERM is how a program discovers what the terminal can do. Claiming xterm-256color is
            // a promise: everything that name implies has to actually work.
            ["TERM"] = _options.Terminal.TermName,
        };

        var sessionOptions = new ShellSessionOptions(
            executable,
            [.. _options.Shell.Arguments],
            _options.Shell.WorkingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            environment,
            _terminal.Size);

        var shell = backend.CreateSession(sessionOptions);

        _session = new TerminalSession(shell, _terminal, _loggerFactory.CreateLogger<TerminalSession>());
        _session.OutputApplied += OnOutputApplied;
        _session.TitleChanged += OnTitleChanged;
        _session.Exited += OnSessionExited;

        await _session.StartAsync().ConfigureAwait(true);

        _logger.SessionCreated(shell.Id, executable, _terminal.Size.Columns, _terminal.Size.Rows);
        _view.Focus();
        UpdateStatus();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        await session.DisposeAsync().ConfigureAwait(true);
        _logger.SessionClosed(session.Shell.Id);
    }

    private void OnOutputApplied(object? sender, EventArgs e)
    {
        _view.InvalidateDamagedRows();
        UpdateStatus();
    }

    private void OnSessionExited(object? sender, ShellExitedEventArgs e)
    {
        var reason = e.ExitCode is { } code
            ? $"shell exited with code {code}"
            : e.Error?.Message ?? "shell ended";

        StatusText.Text = $"{reason}   close the window to finish";
        _view.InvalidateDamagedRows();
    }

    private async void OnInputProduced(object? sender, ReadOnlyMemory<byte> data)
    {
        if (_session is not { IsRunning: true } session)
        {
            return;
        }

        await session.WriteAsync(data).ConfigureAwait(true);
    }

    private void OnTitleChanged(object? sender, string title)
        => Title = string.IsNullOrWhiteSpace(title) ? AppInfo.Name : $"{title} - {AppInfo.Name}";

    private async void OnViewportSizeChanged(object? sender, TerminalSize size)
    {
        if (size == _terminal.Size)
        {
            return;
        }

        if (_session is { } session)
        {
            // The engine and the pseudo console have to be resized together, and the session owns
            // the ordering that makes that safe.
            await session.ResizeAsync(size).ConfigureAwait(true);
        }
        else
        {
            _terminal.Resize(size);
        }

        _view.InvalidateDamagedRows();
        UpdateStatus();
    }

    /// <summary>
    /// Writes a failure onto the terminal itself, in red.
    /// </summary>
    /// <remarks>
    /// A terminal that cannot start a shell should explain itself on its own screen rather than
    /// closing silently: the message is the only thing the user has to go on.
    /// </remarks>
    private void ShowFailure(Exception exception)
    {
        const string Csi = "\u001b[";
        const string NewLine = "\r\n";

        var message =
            $"{Csi}1;31mNovaTerminal could not start a shell.{Csi}0m" + NewLine + NewLine +
            $"  {exception.Message}" + NewLine + NewLine +
            $"{Csi}90m  See the log for details.{Csi}0m" + NewLine;

        var interpreter = new TerminalInterpreter(_terminal);
        new AnsiParser(interpreter).Parse(Encoding.UTF8.GetBytes(message));
        _view.InvalidateDamagedRows();
    }

    private void UpdateStatus()
    {
        var size = _terminal.Size;
        var metrics = _view.CellMetrics;
        var shell = _session is { } session
            ? $"shell {session.Shell.Id} {(session.IsRunning ? "running" : "stopped")}"
            : "no shell";

        StatusText.Text =
            $"{size.Columns}x{size.Rows} cells   " +
            $"cell {metrics.Width:0.##}x{metrics.Height:0.##} px   " +
            $"theme {_options.Appearance.ThemeName}   " +
            $"{shell}";
    }
}
