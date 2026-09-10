using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Platform;
using NovaTerminal.Process;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.Integration.Tests;

/// <summary>
/// Drives a real shell through a real pseudo terminal.
/// </summary>
/// <remarks>
/// <para>
/// These are deliberately not mocked. The whole point of the platform layer is that it talks to the
/// operating system correctly, and no test double can tell you whether
/// <c>UpdateProcThreadAttribute</c> was called with the right argument shape - only a shell that
/// either starts or does not.
/// </para>
/// <para>
/// <c>cmd.exe</c> is used rather than PowerShell because it starts faster and its echo behaviour is
/// simpler to assert on. What is being tested is the pseudo terminal, not the shell.
/// </para>
/// </remarks>
public sealed class ShellSessionTests
{
    /// <summary>
    /// A shell announces itself within milliseconds when the pseudo terminal is working. A generous
    /// few seconds distinguishes "slow machine" from "not attached" without making a broken
    /// environment take a minute and a half to report itself.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private const string AttachmentDiagnostic =
        "The pseudo console was created and a headless conhost was started, but the child produced " +
        "no output through it. That means the child process was not bound to the pseudo console. " +
        "The usual cause is another program intercepting process creation - endpoint security " +
        "software is the common culprit - which re-launches the child without the " +
        "PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE attribute list. See docs/troubleshooting.md.";

    [Fact]
    public async Task AShellStartsAndProducesOutput()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The ConPTY backend is Windows-only.");

        await using var session = CreateSession();
        await session.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ShellSessionStatus.Running, session.Status);

        // A shell attached to a terminal announces itself. One attached to a plain pipe would not:
        // it would decide it is not interactive and stay silent, which is exactly the failure a
        // pseudo terminal exists to avoid.
        var output = await ReadUntilAsync(session, text => text.Length > 0);

        Assert.NotEmpty(output);
    }

    [Fact]
    public async Task ACommandTypedIntoTheShellProducesItsOutput()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The ConPTY backend is Windows-only.");

        const string Marker = "NovaTerminalIntegrationMarker";

        await using var session = CreateSession();
        await session.StartAsync(TestContext.Current.CancellationToken);

        await ReadUntilAsync(session, text => text.Contains('>', StringComparison.Ordinal));

        // A carriage return is what the Enter key sends, which is what the shell is waiting for.
        await session.WriteAsync(
            Encoding.UTF8.GetBytes($"echo {Marker}\r"), TestContext.Current.CancellationToken);

        var output = await ReadUntilAsync(
            session,
            text => CountOccurrences(text, Marker) >= 2);

        // The marker appears twice: once echoed as the user "types" it, and once as the result.
        // Seeing the echo is itself the proof that the shell is in interactive mode.
        Assert.True(CountOccurrences(output, Marker) >= 2, output);
    }

    [Fact]
    public async Task OutputReachesTheTerminalEngineThroughTheParser()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The ConPTY backend is Windows-only.");

        const string Marker = "NovaTerminalScreenMarker";

        var terminal = new TerminalState(new TerminalSize(80, 24));
        var parser = new AnsiParser(new TerminalInterpreter(terminal));

        await using var session = CreateSession();
        await session.StartAsync(TestContext.Current.CancellationToken);

        await PumpUntilAsync(session, parser, terminal, screen => screen.Contains('>', StringComparison.Ordinal));

        await session.WriteAsync(
            Encoding.UTF8.GetBytes($"echo {Marker}\r"), TestContext.Current.CancellationToken);

        var screen = await PumpUntilAsync(
            session, parser, terminal, text => CountOccurrences(text, Marker) >= 2);

        // The full pipeline: pseudo terminal, parser, engine, screen. The marker is on a line of
        // its own, which means the control sequences around it were interpreted rather than printed.
        Assert.Contains(Marker, screen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResizeIsAcceptedWhileTheShellIsRunning()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The ConPTY backend is Windows-only.");

        await using var session = CreateSession();
        await session.StartAsync(TestContext.Current.CancellationToken);
        await ReadUntilAsync(session, text => text.Length > 0);

        var resized = new TerminalSize(100, 30);
        await session.ResizeAsync(resized, TestContext.Current.CancellationToken);

        Assert.Equal(resized, session.Size);
        Assert.Equal(ShellSessionStatus.Running, session.Status);
    }

    [Fact]
    public async Task StoppingASessionCompletesItsOutput()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The ConPTY backend is Windows-only.");

        var session = CreateSession();

        await using (session)
        {
            await session.StartAsync(TestContext.Current.CancellationToken);
            await ReadUntilAsync(session, text => text.Length > 0);
            await session.StopAsync(TestContext.Current.CancellationToken);
        }

        // The reader completing is how a consumer learns the shell is gone, so a loop over the
        // channel ends rather than hanging.
        Assert.True(session.Output.Completion.IsCompleted);
    }

    [Fact]
    public async Task ASessionCannotBeStartedTwice()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The ConPTY backend is Windows-only.");

        await using var session = CreateSession();
        await session.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WritingToAStoppedSessionIsIgnoredRatherThanThrowing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The ConPTY backend is Windows-only.");

        await using var session = CreateSession();
        await session.StartAsync(TestContext.Current.CancellationToken);
        await session.StopAsync(TestContext.Current.CancellationToken);

        // A keystroke arriving as the shell exits is a race that happens constantly in practice.
        // It must not surface as an exception on the UI thread.
        await session.WriteAsync("ignored"u8.ToArray(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void TheBackendReportsWhatItSupports()
    {
        var backend = ShellBackendFactory.Create();

        Assert.Equal("ConPTY", backend.Name);
        Assert.True(backend.IsSupported);
        Assert.True(File.Exists(backend.GetDefaultShellExecutable()));
    }

    private static IShellSession CreateSession()
    {
        var backend = ShellBackendFactory.Create();
        var comSpec = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

        var options = new ShellSessionOptions(
            comSpec,
            [],
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["TERM"] = "xterm-256color" },
            new TerminalSize(80, 24));

        return backend.CreateSession(options);
    }

    /// <summary>Accumulates raw output until it satisfies <paramref name="isComplete"/>.</summary>
    private static async Task<string> ReadUntilAsync(IShellSession session, Func<string, bool> isComplete)
    {
        var accumulated = new StringBuilder();
        using var timeout = new CancellationTokenSource(Timeout);

        try
        {
            await foreach (var chunk in session.Output.ReadAllAsync(timeout.Token))
            {
                accumulated.Append(Encoding.UTF8.GetString(chunk));

                if (isComplete(accumulated.ToString()))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail(
                $"Timed out after {Timeout.TotalSeconds:0} seconds waiting for shell output. " +
                $"Received so far: '{accumulated}'.{Environment.NewLine}{AttachmentDiagnostic}");
        }

        return accumulated.ToString();
    }

    /// <summary>Feeds output through the parser until the rendered screen satisfies a condition.</summary>
    private static async Task<string> PumpUntilAsync(
        IShellSession session,
        AnsiParser parser,
        TerminalState terminal,
        Func<string, bool> isComplete)
    {
        using var timeout = new CancellationTokenSource(Timeout);

        try
        {
            await foreach (var chunk in session.Output.ReadAllAsync(timeout.Token))
            {
                parser.Parse(chunk);

                var screen = terminal.Buffer.GetText();

                if (isComplete(screen))
                {
                    return screen;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail(
                $"Timed out. Screen was:{Environment.NewLine}{terminal.Buffer.GetText()}" +
                $"{Environment.NewLine}{AttachmentDiagnostic}");
        }

        return terminal.Buffer.GetText();
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
