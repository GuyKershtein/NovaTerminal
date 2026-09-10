using NovaTerminal.Core;

namespace NovaTerminal.Terminal;

/// <summary>
/// Where the next character will be written, and how the cursor is drawn.
/// </summary>
/// <remarks>
/// This type holds state and enforces no policy: clamping to the screen, wrapping and scrolling all
/// belong to <see cref="TerminalState"/>, which is the only thing that knows the screen size and
/// the scrolling region. Keeping the cursor a plain record of position makes the rules that govern
/// it testable in one place instead of spread across two.
/// </remarks>
public sealed class TerminalCursor
{
    /// <summary>The zero-based column the cursor sits in.</summary>
    public int Column { get; set; }

    /// <summary>The zero-based row the cursor sits in.</summary>
    public int Row { get; set; }

    /// <summary>Whether the cursor is drawn, as controlled by <c>DECTCEM</c>.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>The shape the cursor is drawn with, as controlled by <c>DECSCUSR</c>.</summary>
    public CursorStyle Style { get; set; } = CursorStyle.Block;

    /// <summary>
    /// Set when a character has been written into the final column and the cursor is waiting to see
    /// whether anything else arrives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the "deferred wrap" rule, and it is one of the details that separates a terminal
    /// emulator from a text box. Writing into the last column does <em>not</em> move the cursor to
    /// the next line. The cursor stays where it is and this flag is raised; only the next printable
    /// character actually wraps.
    /// </para>
    /// <para>
    /// Without it, printing exactly as many characters as the terminal is wide would immediately
    /// advance to the next line, and a following newline would produce a second, blank line. Every
    /// program that formats output to the full terminal width depends on this behaving as the
    /// original DEC hardware did.
    /// </para>
    /// </remarks>
    public bool PendingWrap { get; set; }

    /// <summary>Moves the cursor and cancels any deferred wrap.</summary>
    /// <remarks>
    /// Explicit positioning always clears the deferred wrap: the flag describes a position the
    /// cursor is about to leave, so carrying it across a move would wrap the wrong line.
    /// </remarks>
    public void MoveTo(int column, int row)
    {
        Column = column;
        Row = row;
        PendingWrap = false;
    }

    /// <summary>Captures the cursor state saved by <c>DECSC</c>.</summary>
    public TerminalCursorState Capture(CellStyle style) => new(Column, Row, PendingWrap, style);

    /// <summary>Restores position from a state captured by <see cref="Capture"/>.</summary>
    public void Restore(TerminalCursorState state)
    {
        Column = state.Column;
        Row = state.Row;
        PendingWrap = state.PendingWrap;
    }
}

/// <summary>
/// A snapshot of the cursor taken by <c>DECSC</c> and restored by <c>DECRC</c>.
/// </summary>
/// <remarks>
/// The saved state deliberately includes the current pen. <c>DECSC</c> saves the graphic rendition
/// along with the position, so a program can change colours freely and put everything back with one
/// sequence.
/// </remarks>
/// <param name="Column">Saved column.</param>
/// <param name="Row">Saved row.</param>
/// <param name="PendingWrap">Saved deferred-wrap flag.</param>
/// <param name="Style">Saved pen.</param>
public readonly record struct TerminalCursorState(int Column, int Row, bool PendingWrap, CellStyle Style);
