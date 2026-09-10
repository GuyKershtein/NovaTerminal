using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NovaTerminal.Platform.Windows;

/// <summary>
/// The Win32 entry points needed to run a shell inside a pseudo console.
/// </summary>
/// <remarks>
/// <para>
/// This is the only file in NovaTerminal that talks to the operating system directly. Everything
/// above it works in terms of <see cref="Process.IShellSession"/>, which is what allows a Unix
/// backend to be added later without any other layer noticing.
/// </para>
/// <para>
/// Declarations use <c>LibraryImport</c> rather than <c>DllImport</c>, so the marshalling code is
/// generated at compile time instead of at run time: it is faster, it is visible to the reader, and
/// it works under ahead-of-time compilation.
/// </para>
/// </remarks>
internal static partial class NativeMethods
{
    private const string Kernel32 = "kernel32.dll";

    /// <summary>Tells <c>CreateProcess</c> that the startup information carries an attribute list.</summary>
    internal const uint ExtendedStartupInfoPresent = 0x0008_0000;

    /// <summary>Tells <c>CreateProcess</c> that the supplied environment block is UTF-16.</summary>
    internal const uint CreateUnicodeEnvironment = 0x0000_0400;

    /// <summary>
    /// The attribute that binds a new process to a pseudo console. This one value is what makes the
    /// child believe it is attached to a terminal.
    /// </summary>
    internal const nint ProcThreadAttributePseudoConsole = 0x0002_0016;

    /// <summary>Wait timeout meaning "however long it takes".</summary>
    internal const uint Infinite = 0xFFFF_FFFF;

    /// <summary>Error code returned when a buffer was too small, which is how sizes are queried.</summary>
    internal const int ErrorInsufficientBuffer = 122;

    /// <summary>Error code returned when a pipe's other end has been closed.</summary>
    internal const int ErrorBrokenPipe = 109;

    /// <summary>A console screen coordinate pair. Note that both fields are 16-bit.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfoW
    {
        public int Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Length;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfoEx
    {
        public StartupInfoW StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe,
        IntPtr pipeAttributes,
        int size);

    /// <summary>
    /// Creates a pseudo console. Introduced in Windows 10 version 1809; on anything older this
    /// entry point does not exist, which is why availability is checked before it is called.
    /// </summary>
    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int CreatePseudoConsole(
        Coord size,
        SafeFileHandle input,
        SafeFileHandle output,
        uint flags,
        out IntPtr pseudoConsole);

    /// <summary>
    /// Changes a pseudo console's dimensions, which causes the operating system to notify the
    /// client so full-screen programs can repaint at the new size.
    /// </summary>
    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int ResizePseudoConsole(IntPtr pseudoConsole, Coord size);

    [LibraryImport(Kernel32)]
    internal static partial void ClosePseudoConsole(IntPtr pseudoConsole);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        int attributeCount,
        int flags,
        ref nint size);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        nint attribute,
        IntPtr value,
        nint size,
        IntPtr previousValue,
        IntPtr returnSize);

    [LibraryImport(Kernel32)]
    internal static partial void DeleteProcThreadAttributeList(IntPtr attributeList);

    [LibraryImport(Kernel32, EntryPoint = "CreateProcessW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool CreateProcess(
        char* applicationName,
        char* commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        void* environment,
        char* currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateProcess(IntPtr process, uint exitCode);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);
}
