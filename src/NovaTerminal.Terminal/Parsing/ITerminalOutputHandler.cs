using System.Text;

namespace NovaTerminal.Terminal.Parsing;

/// <summary>
/// Receives the decoded results of parsing a terminal data stream.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam between <em>recognising</em> a sequence and <em>acting</em> on it.
/// <see cref="AnsiParser"/> knows the grammar and nothing about terminals;
/// <see cref="TerminalInterpreter"/> knows what each function means and nothing about bytes.
/// </para>
/// <para>
/// Splitting them this way is what makes the parser testable against a recording handler - the
/// tests can assert precisely which sequences were recognised, independently of whether the engine
/// implements them.
/// </para>
/// </remarks>
public interface ITerminalOutputHandler
{
    /// <summary>A printable character was decoded.</summary>
    void Print(Rune rune);

    /// <summary>A C0 control character was encountered and should be acted on.</summary>
    /// <param name="control">The control byte, always below 0x20.</param>
    void Execute(byte control);

    /// <summary>
    /// An escape sequence completed, such as <c>ESC M</c> or <c>ESC ( B</c>.
    /// </summary>
    /// <param name="final">The final byte identifying the function.</param>
    /// <param name="intermediate">
    /// The intermediate byte, or <see cref="CsiSequence.None"/> when there was none.
    /// </param>
    void EscapeDispatch(char final, char intermediate);

    /// <summary>A control sequence completed.</summary>
    void CsiDispatch(in CsiSequence sequence);

    /// <summary>
    /// An operating system command completed, such as a window title change.
    /// </summary>
    /// <param name="data">
    /// The raw payload between the introducer and the terminator, which by convention is a numeric
    /// command, a semicolon and then arbitrary text. It is delivered as bytes because the text is
    /// UTF-8 and its length is not known until the terminator arrives.
    /// </param>
    void OscDispatch(ReadOnlySpan<byte> data);
}
