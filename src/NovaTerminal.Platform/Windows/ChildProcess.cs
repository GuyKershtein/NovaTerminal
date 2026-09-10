using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace NovaTerminal.Platform.Windows;

/// <summary>
/// A process launched attached to a pseudo console.
/// </summary>
/// <remarks>
/// <para>
/// The attachment happens through an attribute list passed to <c>CreateProcess</c>. That indirection
/// is the whole mechanism: rather than handing the child pipe handles as its standard streams -
/// which is what makes a shell decide it is not interactive - the child is told it owns a console,
/// and the operating system routes its console I/O to the pseudo console for us.
/// </para>
/// <para>
/// <see cref="System.Diagnostics.Process"/> is deliberately not used. It cannot adopt a handle from
/// <c>CreateProcess</c>, and looking the process up by identifier afterwards would race against
/// identifier reuse. Waiting on the handle we were given has neither problem.
/// </para>
/// </remarks>
internal sealed class ChildProcess : IDisposable
{
    private IntPtr _processHandle;
    private IntPtr _threadHandle;
    private bool _disposed;

    private ChildProcess(IntPtr processHandle, IntPtr threadHandle, int processId)
    {
        _processHandle = processHandle;
        _threadHandle = threadHandle;
        ProcessId = processId;
    }

    /// <summary>The child's process identifier, for logging.</summary>
    public int ProcessId { get; }

    /// <summary>Launches a process attached to <paramref name="console"/>.</summary>
    /// <exception cref="Win32Exception">The process could not be created.</exception>
    public static unsafe ChildProcess Start(
        PseudoConsole console,
        string commandLine,
        string? workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        var attributes = ProcThreadAttributeList.CreateForPseudoConsole(console.Handle);

        try
        {
            var startupInfo = new NativeMethods.StartupInfoEx
            {
                StartupInfo = { Size = sizeof(NativeMethods.StartupInfoEx) },
                AttributeList = attributes.Handle,
            };

            // CreateProcessW may modify the command line in place, so it must be given a writable
            // buffer rather than a pinned string literal.
            var commandLineBuffer = new char[commandLine.Length + 1];
            commandLine.CopyTo(commandLineBuffer);

            var environmentBlock = BuildEnvironmentBlock(environment);

            fixed (char* commandLinePointer = commandLineBuffer)
            fixed (char* workingDirectoryPointer = workingDirectory)
            fixed (char* environmentPointer = environmentBlock)
            {
                var flags = NativeMethods.ExtendedStartupInfoPresent;

                if (environmentBlock is not null)
                {
                    flags |= NativeMethods.CreateUnicodeEnvironment;
                }

                var created = NativeMethods.CreateProcess(
                    applicationName: null,
                    commandLine: commandLinePointer,
                    processAttributes: IntPtr.Zero,
                    threadAttributes: IntPtr.Zero,
                    inheritHandles: false,
                    creationFlags: flags,
                    environment: environmentPointer,
                    currentDirectory: workingDirectoryPointer,
                    startupInfo: ref startupInfo,
                    processInformation: out var information);

                if (!created)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), $"Could not start '{commandLine}'.");
                }

                return new ChildProcess(information.Process, information.Thread, information.ProcessId);
            }
        }
        finally
        {
            attributes.Dispose();
        }
    }

    /// <summary>Blocks until the process exits and returns its exit code.</summary>
    public int WaitForExit()
    {
        if (_processHandle == IntPtr.Zero)
        {
            return 0;
        }

        NativeMethods.WaitForSingleObject(_processHandle, NativeMethods.Infinite);

        return NativeMethods.GetExitCodeProcess(_processHandle, out var exitCode) ? (int)exitCode : 0;
    }

    /// <summary>Terminates the process, ignoring the case where it has already exited.</summary>
    public void Terminate()
    {
        if (_processHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.TerminateProcess(_processHandle, 1);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_threadHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_threadHandle);
            _threadHandle = IntPtr.Zero;
        }

        if (_processHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Builds a UTF-16 environment block, or returns null to inherit the parent's environment.
    /// </summary>
    /// <remarks>
    /// The format is a run of null-terminated <c>NAME=VALUE</c> strings followed by one more null.
    /// Windows also requires the entries to be sorted by name, case-insensitively.
    /// </remarks>
    private static char[]? BuildEnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.Count == 0)
        {
            return null;
        }

        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                merged[name] = value;
            }
        }

        foreach (var (name, value) in overrides)
        {
            merged[name] = value;
        }

        var builder = new StringBuilder();

        foreach (var (name, value) in merged)
        {
            builder.Append(name).Append('=').Append(value).Append('\0');
        }

        builder.Append('\0');
        return builder.ToString().ToCharArray();
    }
}
