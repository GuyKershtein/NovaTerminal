using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using NovaTerminal.Core;
using NovaTerminal.Process;

namespace NovaTerminal.Platform.Windows;

/// <summary>
/// A shell running inside a Windows pseudo console.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the output pump is a dedicated thread.</b> The pipes behind a pseudo console are
/// anonymous pipes, which Windows does not support overlapped I/O on. An "asynchronous" read from
/// one blocks a thread regardless of how it is written, so the honest implementation is one
/// long-running thread that blocks on reads and hands what it gets to a channel. Pretending
/// otherwise would just block a thread-pool thread instead, which is worse.
/// </para>
/// <para>
/// <b>Why the channel is bounded.</b> A command such as <c>dir /s</c> can produce output far faster
/// than a parser can consume it. An unbounded channel would let that turn into unbounded memory
/// growth; a bounded one makes the reader wait, which propagates back through the pipe to the
/// shell itself. Slowing down is the correct response to a consumer that cannot keep up.
/// </para>
/// <para>
/// <b>How reads are cancelled.</b> A blocking read on a pipe cannot be interrupted. Closing the
/// handle can - the read fails, and the pump treats that as end of stream. So shutdown works by
/// closing handles rather than by signalling the reader.
/// </para>
/// </remarks>
internal sealed class ConPtyShellSession : IShellSession
{
    /// <summary>
    /// Size of each read from the pseudo console. Large enough that a burst of output is a handful
    /// of reads, small enough that the first output of a slow command appears immediately.
    /// </summary>
    private const int ReadBufferSize = 8192;

    /// <summary>
    /// How many chunks may be waiting to be parsed before the reader is made to wait.
    /// </summary>
    private const int OutputQueueCapacity = 64;

    private readonly ShellSessionOptions _options;
    private readonly ILogger _logger;
    private readonly Channel<byte[]> _output;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lock _stateLock = new();

    private PseudoConsole? _console;
    private ChildProcess? _process;
    private FileStream? _reader;
    private FileStream? _writer;
    private Task? _pumpTask;
    private Task? _exitWatcherTask;
    private int _stopped;

    /// <summary>Creates a session. Nothing is launched until <see cref="StartAsync"/> is called.</summary>
    public ConPtyShellSession(ShellSessionOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _logger = logger ?? NullLogger.Instance;
        Size = options.InitialSize;

        _output = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(OutputQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    /// <inheritdoc />
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <inheritdoc />
    public ShellSessionStatus Status { get; private set; } = ShellSessionStatus.NotStarted;

    /// <inheritdoc />
    public TerminalSize Size { get; private set; }

    /// <inheritdoc />
    public int? ExitCode { get; private set; }

    /// <inheritdoc />
    public ChannelReader<byte[]> Output => _output.Reader;

    /// <inheritdoc />
    public event EventHandler<ShellExitedEventArgs>? Exited;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Status != ShellSessionStatus.NotStarted)
        {
            throw new InvalidOperationException($"Session {Id} has already been started.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        PlatformSupport.ThrowIfNoPtyAvailable();

        try
        {
            _console = PseudoConsole.Create(Size);

            var commandLine = CommandLine.Build(_options.Executable, _options.Arguments);
            _process = ChildProcess.Start(
                _console, commandLine, _options.WorkingDirectory, _options.Environment);

            _reader = new FileStream(_console.OutputReader, FileAccess.Read, ReadBufferSize, isAsync: false);
            _writer = new FileStream(_console.InputWriter, FileAccess.Write, ReadBufferSize, isAsync: false);

            Status = ShellSessionStatus.Running;
            PlatformLog.ShellStarted(_logger, Id, _options.Executable, _process.ProcessId, Size.Columns, Size.Rows);

            _pumpTask = Task.Factory.StartNew(
                PumpOutput,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            _exitWatcherTask = Task.Factory.StartNew(
                WatchForExit,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            Status = ShellSessionStatus.Failed;
            PlatformLog.ShellStartFailed(_logger, exception, Id, _options.Executable);

            Cleanup();
            _output.Writer.TryComplete(exception);
            Exited?.Invoke(this, new ShellExitedEventArgs(null, exception));

            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (Status != ShellSessionStatus.Running || _writer is null || data.IsEmpty)
        {
            return;
        }

        // Keystrokes can arrive from the UI thread while a paste is still being written. The lock
        // keeps the two from interleaving mid-sequence, which would corrupt both.
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _writer.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The shell exited between the check above and the write. That is a normal race, not a
            // failure: the exit watcher will report it.
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            if (Status != ShellSessionStatus.Running || _console is null || size == Size)
            {
                return ValueTask.CompletedTask;
            }

            _console.Resize(size);
            Size = size;
        }

        PlatformLog.ShellResized(_logger, Id, size.Columns, size.Rows);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);

        _process?.Terminate();

        // Closing the handles is what unblocks the reader; there is no way to cancel a pipe read.
        Cleanup();

        var pending = new[] { _pumpTask, _exitWatcherTask }.Where(task => task is not null).Cast<Task>();
        await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);

        _output.Writer.TryComplete();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // Disposal must not throw over a shell that was already going away.
        }

        _shutdown.Dispose();
        _writeLock.Dispose();
    }

    /// <summary>
    /// Reads from the pseudo console until it closes, handing each chunk to the channel.
    /// </summary>
    private void PumpOutput()
    {
        var buffer = new byte[ReadBufferSize];

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var read = _reader!.Read(buffer, 0, buffer.Length);

                if (read <= 0)
                {
                    break;
                }

                // The channel is bounded, so this call blocks when the parser falls behind. That
                // backpressure is deliberate: it is what stops a flood of output becoming a flood
                // of memory.
                _output.Writer.WriteAsync(buffer[..read]).AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The pseudo console closed, which is how a shell exiting looks from this end.
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        finally
        {
            _output.Writer.TryComplete();
        }
    }

    /// <summary>Waits for the child to exit and reports it once.</summary>
    private void WatchForExit()
    {
        var exitCode = _process?.WaitForExit() ?? 0;

        lock (_stateLock)
        {
            if (Status is ShellSessionStatus.Exited or ShellSessionStatus.Failed)
            {
                return;
            }

            ExitCode = exitCode;
            Status = ShellSessionStatus.Exited;
        }

        PlatformLog.ShellExited(_logger, Id, exitCode);
        Exited?.Invoke(this, new ShellExitedEventArgs(exitCode));
    }

    private void Cleanup()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _console?.Dispose();
        _process?.Dispose();

        _reader = null;
        _writer = null;
        _console = null;
        _process = null;
    }
}
