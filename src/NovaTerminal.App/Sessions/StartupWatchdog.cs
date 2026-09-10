using Avalonia.Threading;
using NovaTerminal.Terminal;
using NovaTerminal.Terminal.Parsing;

namespace NovaTerminal.App.Sessions;

/// <summary>
/// Notices when a shell starts but never says anything, and explains it on the terminal itself.
/// </summary>
/// <remarks>
/// <para>
/// A shell attached to a working pseudo terminal announces itself within milliseconds. Silence
/// means the child was created but never bound to the pseudo console - usually because something
/// intercepted process creation and re-launched it without the attribute list that performs the
/// binding.
/// </para>
/// <para>
/// The failure is invisible otherwise: the process really is running, so nothing errors, and the
/// user is left staring at an empty window with no idea why. Saying so on the screen costs a few
/// lines and turns a mystery into a diagnosis.
/// </para>
/// </remarks>
public sealed class StartupWatchdog : IDisposable
{
    /// <summary>
    /// How long to wait for a shell's first byte. Generous compared with the milliseconds a healthy
    /// shell takes, so a slow machine is never accused of being broken.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private const string Csi = "\u001b[";
    private const string NewLine = "\r\n";

    private readonly DispatcherTimer _timer;
    private readonly TerminalState _terminal;
    private readonly AnsiParser _parser;
    private readonly Action _onDiagnosed;

    private bool _sawOutput;
    private bool _disposed;

    /// <summary>Starts watching a terminal for its shell's first output.</summary>
    public StartupWatchdog(TerminalState terminal, Action onDiagnosed)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(onDiagnosed);

        _terminal = terminal;
        _onDiagnosed = onDiagnosed;
        _parser = new AnsiParser(new TerminalInterpreter(terminal));

        _timer = new DispatcherTimer { Interval = Timeout };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Records that the shell produced output, which is the healthy case.</summary>
    public void NoteOutput()
    {
        _sawOutput = true;
        _timer.Stop();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();

        if (_sawOutput || _disposed)
        {
            return;
        }

        WriteDiagnostic();
        _onDiagnosed();
    }

    private void WriteDiagnostic()
    {
        var message =
            $"{Csi}1;33mThe shell started but has not produced any output.{Csi}0m" + NewLine + NewLine +
            "  The process was created and is running, but it was not bound to the pseudo" + NewLine +
            "  console, so nothing it writes reaches this window." + NewLine + NewLine +
            $"  {Csi}1mThe usual cause{Csi}0m is another program intercepting process creation -" + NewLine +
            "  endpoint security software is the common culprit - which re-launches the" + NewLine +
            "  child without the attribute list that performs the binding." + NewLine + NewLine +
            $"  {Csi}90mSee docs/troubleshooting.md for how to confirm and work around this.{Csi}0m" + NewLine;

        _parser.Parse(System.Text.Encoding.UTF8.GetBytes(message));
    }
}
