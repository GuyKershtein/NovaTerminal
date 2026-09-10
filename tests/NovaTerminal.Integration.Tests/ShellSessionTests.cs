using System.Diagnostics;
using NovaTerminal.Platform;
using NovaTerminal.Process;

namespace NovaTerminal.Integration.Tests;

/// <summary>
/// Drives a real shell through a real pseudo terminal.
/// </summary>
/// <remarks>
/// <para>
/// The interesting part of these tests is that they run out of process, and the reason is worth
/// knowing. <b>A process that owns a console cannot bind a child to a pseudo console.</b> The child
/// attaches to the inherited console instead, the pseudo console never receives a byte, and
/// detaching at run time with <c>FreeConsole</c> does not undo it. This was established by running
/// the identical Win32 sequence from a console executable and from a GUI executable: only the
/// second one works.
/// </para>
/// <para>
/// xUnit v3 requires a console test host, so the only faithful way to exercise ConPTY is to launch
/// a GUI-subsystem helper that runs the real scenario against the real backend and reports back.
/// Testing a mock instead would have verified nothing: no test double can tell you whether
/// <c>UpdateProcThreadAttribute</c> was called with the right argument shape.
/// </para>
/// </remarks>
public sealed class ShellSessionTests
{
    private static readonly TimeSpan HarnessTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task AShellRunsInsideAPseudoTerminalAndEchoesACommand()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The ConPTY backend is Windows-only.");

        var report = await RunHarnessAsync();

        // Every line of the report is an assertion about the real pipeline: the shell started, it
        // announced itself the way only an interactive shell does, it echoed what was typed, the
        // resize was accepted, and the output channel completed on shutdown.
        Assert.Contains("backend=ConPTY", report, StringComparison.Ordinal);
        Assert.Contains("status=Running", report, StringComparison.Ordinal);
        Assert.Contains("prompt=True", report, StringComparison.Ordinal);
        Assert.Contains("marker=True", report, StringComparison.Ordinal);
        Assert.Contains("resized=100x30", report, StringComparison.Ordinal);
        Assert.Contains("completed=True", report, StringComparison.Ordinal);
        Assert.Contains("result=pass", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBackendReportsWhatItSupports()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The ConPTY backend is Windows-only.");

        var backend = ShellBackendFactory.Create();

        Assert.Equal("ConPTY", backend.Name);
        Assert.True(backend.IsSupported);
        Assert.True(File.Exists(backend.GetDefaultShellExecutable()));
    }

    [Fact]
    public void OnAPlatformWithNoBackendTheFactorySaysSoClearly()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has a backend.");

        // The Unix backend is not written yet. What matters is that the failure names the missing
        // piece rather than surfacing as something unrelated further down.
        var exception = Assert.Throws<PlatformNotSupportedException>(() => ShellBackendFactory.Create());

        Assert.Contains("Unix", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASessionIsCreatedInTheNotStartedState()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The ConPTY backend is Windows-only.");

        // Construction must not touch the operating system, so that creating a tab cannot fail for
        // environmental reasons before the user has done anything.
        await using var session = CreateUnstartedSession();

        Assert.Equal(ShellSessionStatus.NotStarted, session.Status);
        Assert.Null(session.ExitCode);
        Assert.Equal(new Core.TerminalSize(80, 24), session.Size);
    }

    [Fact]
    public async Task WritingToAnUnstartedSessionIsIgnoredRatherThanThrowing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The ConPTY backend is Windows-only.");

        await using var session = CreateUnstartedSession();

        // Input arriving before or after a session is alive is an ordinary race in a GUI; it must
        // never surface as an exception on the UI thread.
        await session.WriteAsync("ignored"u8.ToArray(), TestContext.Current.CancellationToken);
    }

    private static IShellSession CreateUnstartedSession()
    {
        var backend = ShellBackendFactory.Create();

        var options = new ShellSessionOptions(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            new Core.TerminalSize(80, 24));

        return backend.CreateSession(options);
    }

    /// <summary>
    /// Runs the out-of-process harness and returns its report.
    /// </summary>
    private static async Task<string> RunHarnessAsync()
    {
        var harness = Path.Combine(AppContext.BaseDirectory, "NovaTerminal.PtyHarness.exe");

        Assert.True(
            File.Exists(harness),
            $"The pseudo-terminal harness was not built alongside the tests. Expected it at {harness}.");

        var reportPath = Path.Combine(
            Path.GetTempPath(), $"nova-pty-harness-{Guid.NewGuid():N}.log");

        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new ProcessStartInfo(harness, [reportPath])
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.Start();

            using var timeout = new CancellationTokenSource(HarnessTimeout);
            await process.WaitForExitAsync(timeout.Token);

            var report = File.Exists(reportPath)
                ? await File.ReadAllTextAsync(reportPath, TestContext.Current.CancellationToken)
                : "(the harness produced no report)";

            Assert.True(
                process.ExitCode == 0,
                $"The pseudo-terminal harness failed. Report:{Environment.NewLine}{report}");

            return report;
        }
        finally
        {
            if (File.Exists(reportPath))
            {
                File.Delete(reportPath);
            }
        }
    }
}
