using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NovaTerminal.Core;

namespace NovaTerminal.Platform.Windows;

/// <summary>
/// A Windows pseudo console, together with the pipe ends the emulator reads from and writes to.
/// </summary>
/// <remarks>
/// <para>
/// A pseudo console has two ends. The child process gets one and cannot tell it from a real
/// console: it can ask whether it is attached to a terminal, and the answer is yes. The emulator
/// holds the other end and sees exactly the byte stream a physical terminal would have received,
/// escape sequences and all.
/// </para>
/// <para>
/// Four handles are involved and only two survive construction. The read end of the input pipe and
/// the write end of the output pipe belong to the console once it has been created, so they are
/// closed here; keeping them open would stop the emulator ever seeing end-of-file when the child
/// exits, because a handle it owned would still hold the pipe open.
/// </para>
/// </remarks>
internal sealed class PseudoConsole : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    private PseudoConsole(IntPtr handle, SafeFileHandle input, SafeFileHandle output)
    {
        _handle = handle;
        InputWriter = input;
        OutputReader = output;
    }

    /// <summary>The pipe the emulator writes keystrokes into.</summary>
    public SafeFileHandle InputWriter { get; }

    /// <summary>The pipe the emulator reads the child's output from.</summary>
    public SafeFileHandle OutputReader { get; }

    /// <summary>The native pseudo console handle, needed when creating the child process.</summary>
    public IntPtr Handle => _handle;

    /// <summary>Creates a pseudo console of the given size.</summary>
    /// <exception cref="Win32Exception">The operating system refused to create it.</exception>
    public static PseudoConsole Create(TerminalSize size)
    {
        if (!NativeMethods.CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the pseudo console input pipe.");
        }

        if (!NativeMethods.CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0))
        {
            inputRead.Dispose();
            inputWrite.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the pseudo console output pipe.");
        }

        var result = NativeMethods.CreatePseudoConsole(ToCoord(size), inputRead, outputWrite, 0, out var handle);

        // The console has duplicated the ends it needs; ours would otherwise keep the pipes alive
        // after the child exits and the reader would never see end-of-file.
        inputRead.Dispose();
        outputWrite.Dispose();

        if (result != 0)
        {
            inputWrite.Dispose();
            outputRead.Dispose();
            throw new Win32Exception(result, "CreatePseudoConsole failed.");
        }

        return new PseudoConsole(handle, inputWrite, outputRead);
    }

    /// <summary>
    /// Changes the console's size, which makes the operating system tell the child that its window
    /// changed so full-screen programs can repaint.
    /// </summary>
    public void Resize(TerminalSize size)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var result = NativeMethods.ResizePseudoConsole(_handle, ToCoord(size));

        if (result != 0)
        {
            throw new Win32Exception(result, $"Could not resize the pseudo console to {size}.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Order matters. Closing the input pipe first lets the client see end-of-input and exit on
        // its own; ClosePseudoConsole waits for the client to finish, so closing it while the child
        // is still reading can block.
        InputWriter.Dispose();

        if (_handle != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_handle);
            _handle = IntPtr.Zero;
        }

        OutputReader.Dispose();
    }

    /// <summary>
    /// Converts a terminal size to the console coordinate pair.
    /// </summary>
    /// <remarks>
    /// The native fields are 16-bit, so the engine's four-thousand-column ceiling is clamped again
    /// here rather than being allowed to wrap into a negative size.
    /// </remarks>
    private static NativeMethods.Coord ToCoord(TerminalSize size) => new()
    {
        X = (short)Math.Clamp(size.Columns, 1, short.MaxValue),
        Y = (short)Math.Clamp(size.Rows, 1, short.MaxValue),
    };
}
