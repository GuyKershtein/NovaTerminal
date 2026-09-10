using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NovaTerminal.Platform.Windows;

/// <summary>
/// An attribute list carrying the pseudo console a child process should be attached to.
/// </summary>
/// <remarks>
/// The Win32 API for this is a two-call pattern: ask how large the list needs to be, allocate that
/// much, then initialise it. The first call is <em>expected</em> to fail with
/// "insufficient buffer", which is why that particular failure is not treated as an error here.
/// </remarks>
internal sealed class ProcThreadAttributeList : IDisposable
{
    private const int AttributeCount = 1;

    private IntPtr _handle;
    private IntPtr _consoleValue;
    private bool _disposed;

    private ProcThreadAttributeList(IntPtr handle, IntPtr consoleValue)
    {
        _handle = handle;
        _consoleValue = consoleValue;
    }

    /// <summary>The native attribute list, for <c>STARTUPINFOEX</c>.</summary>
    public IntPtr Handle => _handle;

    /// <summary>Builds a list that attaches a child to <paramref name="pseudoConsole"/>.</summary>
    /// <exception cref="Win32Exception">The list could not be built.</exception>
    public static ProcThreadAttributeList CreateForPseudoConsole(IntPtr pseudoConsole)
    {
        nint size = 0;

        // Expected to fail: this call exists only to report the size needed.
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, AttributeCount, 0, ref size);

        var error = Marshal.GetLastWin32Error();
        if (size == 0)
        {
            throw new Win32Exception(error, "Could not determine the process attribute list size.");
        }

        var handle = Marshal.AllocHGlobal(size);

        try
        {
            if (!NativeMethods.InitializeProcThreadAttributeList(handle, AttributeCount, 0, ref size))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "Could not initialise the process attribute list.");
            }

            // The pseudo console attribute is the odd one out: its value is passed *by value*
            // rather than as a pointer to the value, unlike PROC_THREAD_ATTRIBUTE_HANDLE_LIST and
            // most others. Passing a pointer here instead makes CreateProcess succeed and the child
            // then die during startup with STATUS_DLL_INIT_FAILED, because it inherits a console
            // handle built from whatever the pointer happened to address.
            if (!NativeMethods.UpdateProcThreadAttribute(
                    handle,
                    0,
                    NativeMethods.ProcThreadAttributePseudoConsole,
                    pseudoConsole,
                    IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                var updateError = Marshal.GetLastWin32Error();
                NativeMethods.DeleteProcThreadAttributeList(handle);
                throw new Win32Exception(updateError, "Could not attach the pseudo console to the child process.");
            }

            return new ProcThreadAttributeList(handle, IntPtr.Zero);
        }
        catch
        {
            Marshal.FreeHGlobal(handle);
            throw;
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

        if (_handle != IntPtr.Zero)
        {
            NativeMethods.DeleteProcThreadAttributeList(_handle);
            Marshal.FreeHGlobal(_handle);
            _handle = IntPtr.Zero;
        }

        if (_consoleValue != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_consoleValue);
            _consoleValue = IntPtr.Zero;
        }
    }
}
