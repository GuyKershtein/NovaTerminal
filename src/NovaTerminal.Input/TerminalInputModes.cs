namespace NovaTerminal.Input;

/// <summary>
/// The terminal modes that change what a key press sends.
/// </summary>
/// <remarks>
/// <para>
/// Key encoding is not a fixed table. The same Up arrow sends <c>ESC [ A</c> normally and
/// <c>ESC O A</c> once a program has enabled application cursor keys, and pasted text is framed
/// differently once bracketed paste is on.
/// </para>
/// <para>
/// Passing the modes in - rather than letting the input layer reach into the engine - is what keeps
/// encoding a pure function of its arguments. The whole xterm key table can then be verified as
/// data, with no terminal, no window and no shell involved.
/// </para>
/// </remarks>
/// <param name="ApplicationCursorKeys">
/// Set by <c>DECCKM</c>. Full-screen programs enable it so they can tell an arrow key from a user
/// typing the same characters.
/// </param>
/// <param name="ApplicationKeypad">Set by <c>DECKPAM</c>.</param>
/// <param name="BracketedPaste">
/// Set by mode 2004. Wraps pasted text in markers so a program can tell it from typing, and refuse
/// to execute a pasted newline.
/// </param>
public readonly record struct TerminalInputModes(
    bool ApplicationCursorKeys = false,
    bool ApplicationKeypad = false,
    bool BracketedPaste = false)
{
    /// <summary>The modes a terminal starts in.</summary>
    public static TerminalInputModes Default => default;
}
