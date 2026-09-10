using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovaTerminal.App.Logging;
using NovaTerminal.Core;
using NovaTerminal.Core.Configuration;
using NovaTerminal.Input;
using NovaTerminal.Process;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.App.Sessions;

/// <summary>
/// One terminal: a shell, the engine displaying its output, and the pump that connects them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The concurrency model.</b> The shell writes output on its own schedule, the user types, the
/// window resizes and the renderer paints - all at once. Rather than protect the engine with locks,
/// this class gives it a single owner: every mutation of terminal state happens on the UI thread.
/// The background pump only moves bytes; it never touches the engine.
/// </para>
/// <para>
/// That choice removes a whole class of bugs. The renderer cannot observe a half-applied escape
/// sequence, a resize cannot interleave with a partially parsed one, and the hot path needs no
/// lock at all.
/// </para>
/// <para>
/// <b>Batching.</b> Output is drained from the channel in batches rather than posted chunk by
/// chunk. Under a flood of output the difference is large: a hundred small chunks become one
/// dispatcher round-trip and one repaint, instead of a hundred of each.
/// </para>
/// </remarks>
public sealed class TerminalSession : IAsyncDisposable
{
    /// <summary>Most chunks moved to the UI thread in one batch.</summary>
    private const int MaxChunksPerBatch = 32;

    private readonly IShellSession _shell;
    private readonly TerminalState _terminal;
    private readonly TerminalInterpreter _interpreter;
    private readonly AnsiParser _parser;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<byte[]> _batch = new(MaxChunksPerBatch);

    private Task? _pumpTask;
    private bool _disposed;

    /// <summary>Creates a session over an already-created shell.</summary>
    public TerminalSession(
        IShellSession shell,
        TerminalState terminal,
        ILogger<TerminalSession>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(terminal);

        _shell = shell;
        _terminal = terminal;
        _logger = logger ?? NullLogger<TerminalSession>.Instance;

        _interpreter = new TerminalInterpreter(terminal);
        _parser = new AnsiParser(_interpreter);

        // A query such as "where is the cursor?" is answered by writing back to the shell exactly
        // as though the user had typed the answer.
        _interpreter.ResponseRequested += OnResponseRequested;
        _interpreter.TitleChanged += (_, title) => TitleChanged?.Invoke(this, title);
        _interpreter.BellRequested += (_, _) => BellRequested?.Invoke(this, EventArgs.Empty);
        _shell.Exited += OnShellExited;
    }

    /// <summary>Raised on the UI thread after a batch of output has been applied.</summary>
    public event EventHandler? OutputApplied;

    /// <summary>Raised when the shell sets the window title.</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>Raised when the shell rings the bell.</summary>
    public event EventHandler? BellRequested;

    /// <summary>Raised when the shell process ends.</summary>
    public event EventHandler<ShellExitedEventArgs>? Exited;

    /// <summary>The screen this session displays.</summary>
    public TerminalState Terminal => _terminal;

    /// <summary>The shell process.</summary>
    public IShellSession Shell => _shell;

    /// <summary>Whether the shell is still running.</summary>
    public bool IsRunning => _shell.Status == ShellSessionStatus.Running;

    /// <summary>The input modes the engine is currently in, for the key encoder.</summary>
    public TerminalInputModes InputModes => new(
        _terminal.ApplicationCursorKeys,
        _terminal.ApplicationKeypad,
        BracketedPaste: false);

    /// <summary>Starts the shell and begins pumping its output.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _shell.StartAsync(cancellationToken).ConfigureAwait(false);

        _pumpTask = PumpOutputAsync(_shutdown.Token);
    }

    /// <summary>Sends bytes to the shell as though the user had typed them.</summary>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> data)
        => _shell.WriteAsync(data, _shutdown.Token);

    /// <summary>
    /// Resizes the terminal and the pseudo console together.
    /// </summary>
    /// <remarks>
    /// The order matters. The engine is resized first so that whatever the shell sends in response
    /// to the size change arrives at a screen that is already the right shape; doing it the other
    /// way round can leave a full-screen program painting into a buffer that no longer matches.
    /// </remarks>
    public async ValueTask ResizeAsync(TerminalSize size)
    {
        if (size == _terminal.Size)
        {
            return;
        }

        _terminal.Resize(size);

        try
        {
            await _shell.ResizeAsync(size, _shutdown.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The shell exited while the window was being dragged; nothing to resize.
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

        await _shutdown.CancelAsync().ConfigureAwait(false);
        _shell.Exited -= OnShellExited;
        _interpreter.ResponseRequested -= OnResponseRequested;

        await _shell.DisposeAsync().ConfigureAwait(false);

        if (_pumpTask is not null)
        {
            try
            {
                await _pumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        _shutdown.Dispose();
    }

    /// <summary>
    /// Moves output from the shell to the engine, batching whatever has already arrived.
    /// </summary>
    private async Task PumpOutputAsync(CancellationToken cancellationToken)
    {
        var reader = _shell.Output;

        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                _batch.Clear();

                while (_batch.Count < MaxChunksPerBatch && reader.TryRead(out var chunk))
                {
                    _batch.Add(chunk);
                }

                if (_batch.Count == 0)
                {
                    continue;
                }

                // Awaiting the UI thread is what applies backpressure: while a repaint is in
                // progress, nothing is drained, so the bounded channel fills and the reader waits.
                await Dispatcher.UIThread.InvokeAsync(ApplyBatch).GetTask().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception exception)
        {
            _logger.SessionPumpFailed(exception, _shell.Id);
        }
    }

    /// <summary>Applies a batch of output. Always runs on the UI thread.</summary>
    private void ApplyBatch()
    {
        foreach (var chunk in _batch)
        {
            _parser.Parse(chunk);
        }

        OutputApplied?.Invoke(this, EventArgs.Empty);
    }

    private void OnResponseRequested(object? sender, ReadOnlyMemory<byte> response)
    {
        // Fire and forget: a reply is small, and blocking the parser on a pipe write could deadlock
        // against a shell that is itself blocked writing output.
        _ = WriteAsync(response).AsTask();
    }

    private void OnShellExited(object? sender, ShellExitedEventArgs e)
        => Dispatcher.UIThread.Post(() => Exited?.Invoke(this, e));
}
