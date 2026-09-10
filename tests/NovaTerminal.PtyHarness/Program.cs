using System.Globalization;
using System.Text;
using NovaTerminal.Core;
using NovaTerminal.Platform;
using NovaTerminal.Process;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.PtyHarness;

/// <summary>
/// Exercises the ConPTY backend end to end and writes what happened to a file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a separate process.</b> A process that owns a console cannot bind a child to a
/// pseudo console: the child attaches to the inherited console instead, and the pseudo console
/// never receives a byte. Detaching at run time with <c>FreeConsole</c> does not undo it. xUnit v3
/// requires its test host to be a console executable, so the ConPTY path simply cannot be exercised
/// faithfully from inside the test host.
/// </para>
/// <para>
/// This harness is therefore a GUI-subsystem executable - the same shape as the application itself.
/// It runs the real scenario against the real backend and reports through a file, which the
/// integration tests read and assert on. The alternative would have been to leave the platform
/// layer untested, or to test a mock and learn nothing.
/// </para>
/// </remarks>
internal static class Program
{
    private const string Marker = "NovaTerminalHarnessMarker";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private static async Task<int> Main(string[] args)
    {
        var reportPath = args.Length > 0 ? args[0] : "pty-harness.log";
        var report = new StringBuilder();

        try
        {
            var success = await RunAsync(report).ConfigureAwait(false);
            report.AppendLine(CultureInfo.InvariantCulture, $"result={(success ? "pass" : "fail")}");
            await File.WriteAllTextAsync(reportPath, report.ToString()).ConfigureAwait(false);
            return success ? 0 : 1;
        }
        catch (Exception exception)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"exception={exception.GetType().Name}: {exception.Message}");
            report.AppendLine("result=fail");
            await File.WriteAllTextAsync(reportPath, report.ToString()).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<bool> RunAsync(StringBuilder report)
    {
        var backend = ShellBackendFactory.Create();
        report.AppendLine(CultureInfo.InvariantCulture, $"backend={backend.Name}");
        report.AppendLine(CultureInfo.InvariantCulture, $"supported={backend.IsSupported}");

        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

        var options = new ShellSessionOptions(
            shell,
            [],
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["TERM"] = "xterm-256color" },
            new TerminalSize(80, 24));

        await using var session = backend.CreateSession(options);

        var terminal = new TerminalState(new TerminalSize(80, 24));
        var parser = new AnsiParser(new TerminalInterpreter(terminal));

        await session.StartAsync().ConfigureAwait(false);
        report.AppendLine(CultureInfo.InvariantCulture, $"status={session.Status}");

        using var timeout = new CancellationTokenSource(Timeout);
        var received = 0;
        var sawPrompt = false;
        var sawMarker = false;

        try
        {
            await foreach (var chunk in session.Output.ReadAllAsync(timeout.Token).ConfigureAwait(false))
            {
                received += chunk.Length;
                parser.Parse(chunk);

                var screen = terminal.Buffer.GetText();

                if (!sawPrompt && screen.Contains('>', StringComparison.Ordinal))
                {
                    sawPrompt = true;
                    report.AppendLine(CultureInfo.InvariantCulture, $"prompt=yes bytes={received}");

                    // A carriage return is what the Enter key sends.
                    await session.WriteAsync(
                        Encoding.UTF8.GetBytes($"echo {Marker}\r"), timeout.Token).ConfigureAwait(false);
                    continue;
                }

                if (sawPrompt && CountOccurrences(screen, Marker) >= 2)
                {
                    // Twice: once echoed as it is "typed", once as the command's result. Seeing the
                    // echo is itself proof the shell is in interactive mode.
                    sawMarker = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            report.AppendLine("timeout=yes");
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"bytes={received}");
        report.AppendLine(CultureInfo.InvariantCulture, $"prompt={sawPrompt}");
        report.AppendLine(CultureInfo.InvariantCulture, $"marker={sawMarker}");

        var resized = new TerminalSize(100, 30);
        await session.ResizeAsync(resized).ConfigureAwait(false);
        report.AppendLine(CultureInfo.InvariantCulture, $"resized={session.Size}");

        await session.StopAsync().ConfigureAwait(false);

        // A channel's completion task finishes only once the reader has drained it, so anything
        // still queued has to be consumed before asking. What consumers actually rely on is that
        // the loop *ends* rather than hanging, which is what this measures.
        var drained = await DrainAsync(session).ConfigureAwait(false);
        report.AppendLine(CultureInfo.InvariantCulture, $"drained={drained}");
        report.AppendLine(CultureInfo.InvariantCulture, $"completed={session.Output.Completion.IsCompleted}");

        return sawPrompt
               && sawMarker
               && session.Size == resized
               && drained
               && session.Output.Completion.IsCompleted;
    }

    /// <summary>
    /// Reads whatever is left in the output channel, and reports whether the loop ended by itself.
    /// </summary>
    private static async Task<bool> DrainAsync(IShellSession session)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await foreach (var _ in session.Output.ReadAllAsync(timeout.Token).ConfigureAwait(false))
            {
                // The contents no longer matter; that the enumeration terminates does.
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
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
