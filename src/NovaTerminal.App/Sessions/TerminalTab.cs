using Microsoft.Extensions.Logging;
using NovaTerminal.App.Views;
using NovaTerminal.Core;
using NovaTerminal.Core.Configuration;
using NovaTerminal.Process;
using NovaTerminal.Rendering;
using NovaTerminal.Terminal;

namespace NovaTerminal.App.Sessions;

/// <summary>
/// One tab: an independent terminal, its own shell, and the view that draws it.
/// </summary>
/// <remarks>
/// Tabs share nothing but configuration. Each has its own engine, its own scrollback, its own shell
/// process and its own pump, so a program that floods one tab with output - or crashes its shell -
/// cannot affect another.
/// </remarks>
public sealed class TerminalTab : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;

    private TerminalSession? _session;
    private StartupWatchdog? _watchdog;
    private bool _disposed;

    /// <summary>Creates a tab and its terminal, but does not start a shell yet.</summary>
    public TerminalTab(NovaTerminalOptions options, TerminalTheme theme, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);

        Options = options;
        _loggerFactory = loggerFactory;

        Terminal = new TerminalState(options.Terminal.InitialSize);
        Terminal.ConfigureScrollback(options.Terminal.ScrollbackLines);

        View = new TerminalView(Terminal, theme, options.Appearance);
    }

    /// <summary>Raised when the tab's title changes and the tab strip should update.</summary>
    public event EventHandler? TitleChanged;

    /// <summary>Raised when this tab's shell exits.</summary>
    public event EventHandler<ShellExitedEventArgs>? Exited;

    /// <summary>Raised after output has been applied, so the window can refresh.</summary>
    public event EventHandler? OutputApplied;

    /// <summary>The configuration this tab was created with.</summary>
    public NovaTerminalOptions Options { get; }

    /// <summary>This tab's terminal.</summary>
    public TerminalState Terminal { get; }

    /// <summary>The control that draws it.</summary>
    public TerminalView View { get; }

    /// <summary>The running session, once a shell has started.</summary>
    public TerminalSession? Session => _session;

    /// <summary>What the tab strip shows for this tab.</summary>
    public string Title { get; private set; } = "Shell";

    /// <summary>Whether this tab's shell is still running.</summary>
    public bool IsRunning => _session is { IsRunning: true };

    /// <summary>Starts a shell in this tab.</summary>
    public async Task StartAsync(IShellBackend backend, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backend);

        var executable = Options.Shell.Executable ?? backend.GetDefaultShellExecutable();

        var environment = new Dictionary<string, string>(Options.Shell.Environment, StringComparer.Ordinal)
        {
            // TERM is how a program discovers what the terminal can do. Claiming xterm-256color is
            // a promise: everything that name implies has to actually work.
            ["TERM"] = Options.Terminal.TermName,
        };

        var sessionOptions = new ShellSessionOptions(
            executable,
            [.. Options.Shell.Arguments],
            Options.Shell.WorkingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            environment,
            Terminal.Size);

        var shell = backend.CreateSession(sessionOptions);

        _session = new TerminalSession(shell, Terminal, _loggerFactory.CreateLogger<TerminalSession>());
        _session.OutputApplied += (_, _) =>
        {
            _watchdog?.NoteOutput();
            OutputApplied?.Invoke(this, EventArgs.Empty);
        };
        _session.TitleChanged += OnSessionTitleChanged;
        _session.Exited += (_, e) => Exited?.Invoke(this, e);

        Title = Path.GetFileNameWithoutExtension(executable);
        TitleChanged?.Invoke(this, EventArgs.Empty);

        // A shell that starts but never speaks is a real failure mode with no error attached to
        // it; the watchdog turns that silence into an explanation on the screen.
        _watchdog = new StartupWatchdog(Terminal, () => OutputApplied?.Invoke(this, EventArgs.Empty));

        await _session.StartAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Sends bytes to this tab's shell.</summary>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> data)
        => _session is { IsRunning: true } session ? session.WriteAsync(data) : ValueTask.CompletedTask;

    /// <summary>Resizes this tab's terminal and pseudo console together.</summary>
    public async ValueTask ResizeAsync(TerminalSize size)
    {
        if (_session is { } session)
        {
            await session.ResizeAsync(size).ConfigureAwait(true);
        }
        else if (size != Terminal.Size)
        {
            Terminal.Resize(size);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _watchdog?.Dispose();
        _watchdog = null;

        if (_session is { } session)
        {
            _session = null;
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnSessionTitleChanged(object? sender, string title)
    {
        // A shell that sets no title keeps the executable name, which is more useful than a blank
        // tab.
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
