using System.Threading.Channels;
using NovaTerminal.Core;

namespace NovaTerminal.Process;

/// <summary>
/// One shell running inside a pseudo-terminal, viewed as a bidirectional byte stream.
/// </summary>
/// <remarks>
/// <para>
/// The interface is intentionally byte-oriented. A pseudo-terminal carries an undifferentiated
/// stream in which text, control codes and escape sequences are interleaved; splitting it into
/// "lines" or "commands" at this layer would destroy exactly the information the VT parser needs.
/// There is also no separate error stream: a PTY merges the child's stdout and stderr, which is
/// precisely why interactive programs behave correctly under one.
/// </para>
/// <para><b>Concurrency contract.</b>
/// <see cref="WriteAsync"/>, <see cref="ResizeAsync"/> and <see cref="StopAsync"/> may be called
/// from any thread and are safe to call concurrently. <see cref="Output"/> is a single-consumer
/// channel: exactly one pump loop should read from it. The channel is bounded, so a shell that
/// produces output faster than the terminal can parse it applies backpressure to the writer rather
/// than growing memory without limit.
/// </para>
/// </remarks>
public interface IShellSession : IAsyncDisposable
{
    /// <summary>A stable identifier used to correlate log entries for this session.</summary>
    string Id { get; }

    /// <summary>The session's position in its lifecycle.</summary>
    ShellSessionStatus Status { get; }

    /// <summary>The pseudo-terminal's current dimensions.</summary>
    TerminalSize Size { get; }

    /// <summary>The child process exit code once it is known.</summary>
    int? ExitCode { get; }

    /// <summary>
    /// Raw output from the shell, in arrival order. The reader completes when the child exits and
    /// all buffered output has been delivered, so a consumer can simply loop until completion.
    /// </summary>
    ChannelReader<byte[]> Output { get; }

    /// <summary>Raised once, after the child process exits or the session fails.</summary>
    event EventHandler<ShellExitedEventArgs>? Exited;

    /// <summary>Creates the pseudo-terminal and launches the child process.</summary>
    /// <exception cref="InvalidOperationException">The session has already been started.</exception>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes bytes to the shell's input, exactly as a keyboard would.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resizes the pseudo-terminal, which causes the operating system to deliver a window-change
    /// notification to the child so full-screen programs can repaint at the new size.
    /// </summary>
    ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session, closing the pseudo-terminal and terminating the child if it does not exit
    /// on its own. Safe to call more than once.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
