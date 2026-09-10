using System.Text;
using NovaTerminal.Core;

namespace NovaTerminal.Terminal;

/// <summary>
/// A complete virtual terminal: a screen buffer, a cursor, the current pen, the scrolling region
/// and the tab stops, together with the operations that a data stream performs on them.
/// </summary>
/// <remarks>
/// <para>
/// This is the interface the ANSI parser drives. Every method corresponds to something a terminal
/// can be told to do - print a character, move the cursor, erase a region - expressed in terms the
/// engine understands rather than in escape sequences. The parser's job is to turn bytes into these
/// calls; this class's job is to know what each one means.
/// </para>
/// <para>
/// Coordinates here are zero-based throughout. The VT protocol numbers rows and columns from one,
/// and that conversion happens in the parser, so exactly one place in the codebase has to get the
/// off-by-one right.
/// </para>
/// <para>
/// <b>Threading.</b> This type is not thread-safe and is not meant to be. One pump loop owns it and
/// applies parsed output, resizes and resets in sequence; the renderer only ever reads. That
/// ownership model is what lets the hot path run without a single lock.
/// </para>
/// </remarks>
public sealed partial class TerminalState
{
    private TerminalCursorState? _savedCursor;

    /// <summary>Creates a blank terminal of the given size.</summary>
    public TerminalState(TerminalSize size)
    {
        _primaryBuffer = new TerminalBuffer(size);
        Cursor = new TerminalCursor();
        TabStops = new TabStops(size.Columns);
        ScrollRegion = ScrollRegion.FullScreen(size);
    }

    /// <summary>Creates a blank terminal at the default 80x24.</summary>
    public TerminalState()
        : this(TerminalSize.Default)
    {
    }

    /// <summary>
    /// The screen currently being displayed, which is the alternate screen while a full-screen
    /// program is running and the primary screen otherwise.
    /// </summary>
    public TerminalBuffer Buffer => _alternateBuffer ?? _primaryBuffer;

    /// <summary>Where the next character goes.</summary>
    public TerminalCursor Cursor { get; }

    /// <summary>The columns a tab advances to.</summary>
    public TabStops TabStops { get; }

    /// <summary>The screen's dimensions.</summary>
    public TerminalSize Size => Buffer.Size;

    /// <summary>
    /// The styling applied to characters as they are printed, accumulated by SGR sequences.
    /// </summary>
    public CellStyle CurrentStyle { get; set; } = CellStyle.Default;

    /// <summary>The band of rows affected by scrolling.</summary>
    public ScrollRegion ScrollRegion { get; private set; }

    /// <summary>
    /// Whether printing past the last column moves to the next line, as controlled by
    /// <c>DECAWM</c>. When disabled, characters pile up in the final column instead.
    /// </summary>
    public bool AutoWrap { get; set; } = true;

    /// <summary>
    /// Whether the arrow and Home/End keys send their application-mode form, as controlled by
    /// <c>DECCKM</c>.
    /// </summary>
    /// <remarks>
    /// This mode changes what the <em>keyboard</em> sends, not what the screen does: with it set,
    /// Up sends <c>ESC O A</c> instead of <c>ESC [ A</c>. Full-screen programs enable it so they can
    /// tell an arrow key apart from a user typing the same characters. The engine only records it;
    /// the input layer reads it when encoding a key press.
    /// </remarks>
    public bool ApplicationCursorKeys { get; set; }

    /// <summary>
    /// Whether the numeric keypad sends its application-mode form, as controlled by <c>DECKPAM</c>
    /// and <c>DECKPNM</c>. Recorded here for the same reason as <see cref="ApplicationCursorKeys"/>.
    /// </summary>
    public bool ApplicationKeypad { get; set; }

    /// <summary>
    /// The style erased cells take: the current background, without the other attributes.
    /// </summary>
    private CellStyle EraseStyle => CurrentStyle.ForErase();

    /// <summary>
    /// Prints a character at the cursor and advances, wrapping and scrolling as needed.
    /// </summary>
    /// <remarks>
    /// Characters that occupy no columns of their own - combining marks and formatting characters -
    /// are currently discarded rather than attached to the preceding cell. Attaching them requires
    /// a cell to hold a grapheme cluster rather than a single scalar, which is a later refinement.
    /// </remarks>
    public void Print(Rune rune)
    {
        rune = TranslateForCharacterSet(rune);

        var width = CharacterWidth.Measure(rune);

        if (width == CharacterWidth.ZeroWidth)
        {
            return;
        }

        // A wrap deferred by the previous character happens now, before anything is written.
        if (Cursor.PendingWrap)
        {
            WrapToNextLine();
        }

        if (width == CharacterWidth.DoubleWidth && Cursor.Column + 1 >= Size.Columns)
        {
            if (!AutoWrap)
            {
                // There is one column left and the character needs two. With wrapping disabled
                // there is nowhere for it to go, so it is dropped rather than split in half.
                return;
            }

            // A double-width glyph cannot straddle a line break, so the odd column is left blank.
            Buffer.ClearLine(Cursor.Row, Cursor.Column, Cursor.Column, EraseStyle);
            WrapToNextLine();
        }

        WriteCharacter(rune, width);
    }

    /// <summary>Prints a string, one character at a time, exactly as if it had arrived as output.</summary>
    public void Print(ReadOnlySpan<char> text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            Print(rune);
        }
    }

    /// <summary>Moves the cursor to column zero, as <c>CR</c> (0x0D) does.</summary>
    public void CarriageReturn()
    {
        Cursor.Column = 0;
        Cursor.PendingWrap = false;
    }

    /// <summary>
    /// Moves the cursor down one row, scrolling the region when it is already on the last row -
    /// what <c>LF</c> (0x0A) and <c>IND</c> do.
    /// </summary>
    public void LineFeed()
    {
        Cursor.PendingWrap = false;
        LineFeedCore();
    }

    /// <summary>
    /// Moves the cursor up one row, scrolling the region down when it is already on the first row -
    /// what <c>RI</c> (<c>ESC M</c>) does.
    /// </summary>
    public void ReverseLineFeed()
    {
        Cursor.PendingWrap = false;

        if (Cursor.Row == ScrollRegion.Top)
        {
            Buffer.ScrollDown(ScrollRegion, 1, EraseStyle);
        }
        else if (Cursor.Row > 0)
        {
            Cursor.Row--;
        }
    }

    /// <summary>
    /// Moves the cursor one column left without erasing, as <c>BS</c> (0x08) does.
    /// </summary>
    /// <remarks>
    /// When a wrap is pending the cursor is logically one column past the end of the line while
    /// being displayed in the last column. A backspace then cancels the pending wrap and stays put,
    /// which is what leaves the cursor over the character just typed - exactly where a shell's
    /// "backspace, space, backspace" erase sequence expects it to be.
    /// </remarks>
    public void Backspace()
    {
        if (Cursor.PendingWrap)
        {
            Cursor.PendingWrap = false;
            return;
        }

        if (Cursor.Column > 0)
        {
            Cursor.Column--;
        }
    }

    /// <summary>Advances the cursor to the next tab stop, as <c>HT</c> (0x09) does.</summary>
    public void HorizontalTab()
    {
        Cursor.Column = TabStops.Next(Cursor.Column);
        Cursor.PendingWrap = false;
    }

    /// <summary>Moves the cursor back to the previous tab stop, as <c>CBT</c> does.</summary>
    public void BackTab()
    {
        Cursor.Column = TabStops.Previous(Cursor.Column);
        Cursor.PendingWrap = false;
    }

    /// <summary>Moves the cursor to an absolute position, clamped to the screen.</summary>
    public void MoveCursorTo(int column, int row)
    {
        // With origin mode set the cursor may not leave the scrolling region at all, which is what
        // lets a program treat the region as if it were the whole screen.
        var (lowestRow, highestRow) = OriginMode
            ? (ScrollRegion.Top, ScrollRegion.Bottom)
            : (0, Size.Rows - 1);

        Cursor.MoveTo(
            Math.Clamp(column, 0, Size.Columns - 1),
            Math.Clamp(row, lowestRow, highestRow));
    }

    /// <summary>Moves the cursor to a column on its current row.</summary>
    public void MoveCursorToColumn(int column) => MoveCursorTo(column, Cursor.Row);

    /// <summary>Moves the cursor to a row in its current column.</summary>
    public void MoveCursorToRow(int row) => MoveCursorTo(Cursor.Column, row);

    /// <summary>
    /// Moves the cursor up, stopping at the top margin when the cursor starts inside the scrolling
    /// region. Cursor movement never scrolls; only line feeds do.
    /// </summary>
    public void MoveCursorUp(int count)
    {
        var limit = Cursor.Row >= ScrollRegion.Top ? ScrollRegion.Top : 0;
        Cursor.MoveTo(Cursor.Column, Math.Max(limit, Cursor.Row - AtLeastOne(count)));
    }

    /// <summary>Moves the cursor down, stopping at the bottom margin.</summary>
    public void MoveCursorDown(int count)
    {
        var limit = Cursor.Row <= ScrollRegion.Bottom ? ScrollRegion.Bottom : Size.Rows - 1;
        Cursor.MoveTo(Cursor.Column, Math.Min(limit, Cursor.Row + AtLeastOne(count)));
    }

    /// <summary>Moves the cursor right, stopping at the last column.</summary>
    public void MoveCursorForward(int count)
        => Cursor.MoveTo(Math.Min(Size.Columns - 1, Cursor.Column + AtLeastOne(count)), Cursor.Row);

    /// <summary>Moves the cursor left, stopping at column zero.</summary>
    public void MoveCursorBackward(int count)
        => Cursor.MoveTo(Math.Max(0, Cursor.Column - AtLeastOne(count)), Cursor.Row);

    /// <summary>Saves the cursor position and the current pen, as <c>DECSC</c> does.</summary>
    public void SaveCursor() => _savedCursor = Cursor.Capture(CurrentStyle);

    /// <summary>
    /// Restores the cursor and pen saved by <see cref="SaveCursor"/>, as <c>DECRC</c> does.
    /// Restoring without a prior save homes the cursor and resets the pen, as on real hardware.
    /// </summary>
    public void RestoreCursor()
    {
        if (_savedCursor is not { } saved)
        {
            MoveCursorTo(0, 0);
            CurrentStyle = CellStyle.Default;
            return;
        }

        // The screen may have shrunk since the save, so the position is re-clamped.
        Cursor.Restore(saved with
        {
            Column = Math.Clamp(saved.Column, 0, Size.Columns - 1),
            Row = Math.Clamp(saved.Row, 0, Size.Rows - 1),
        });

        CurrentStyle = saved.Style;
    }

    /// <summary>Erases part of the cursor's row, as <c>EL</c> does.</summary>
    public void EraseInLine(EraseExtent extent)
    {
        var lastColumn = Size.Columns - 1;

        switch (extent)
        {
            case EraseExtent.ToEnd:
                Buffer.ClearLine(Cursor.Row, Cursor.Column, lastColumn, EraseStyle);
                break;
            case EraseExtent.ToStart:
                Buffer.ClearLine(Cursor.Row, 0, Cursor.Column, EraseStyle);
                break;
            case EraseExtent.All:
                Buffer.ClearLine(Cursor.Row, 0, lastColumn, EraseStyle);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(extent), extent, "Unknown erase extent.");
        }
    }

    /// <summary>Erases part of the screen, as <c>ED</c> does. The cursor does not move.</summary>
    public void EraseInDisplay(EraseExtent extent)
    {
        var lastColumn = Size.Columns - 1;
        var lastRow = Size.Rows - 1;

        switch (extent)
        {
            case EraseExtent.ToEnd:
                Buffer.ClearLine(Cursor.Row, Cursor.Column, lastColumn, EraseStyle);
                if (Cursor.Row < lastRow)
                {
                    Buffer.ClearRows(Cursor.Row + 1, lastRow, EraseStyle);
                }

                break;

            case EraseExtent.ToStart:
                if (Cursor.Row > 0)
                {
                    Buffer.ClearRows(0, Cursor.Row - 1, EraseStyle);
                }

                Buffer.ClearLine(Cursor.Row, 0, Cursor.Column, EraseStyle);
                break;

            case EraseExtent.All:
                Buffer.ClearRows(0, lastRow, EraseStyle);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(extent), extent, "Unknown erase extent.");
        }
    }

    /// <summary>
    /// Erases characters from the cursor without moving anything, as <c>ECH</c> does. Unlike
    /// <see cref="DeleteCharacters"/>, the rest of the line stays where it is.
    /// </summary>
    public void EraseCharacters(int count)
    {
        var last = Math.Min(Cursor.Column + AtLeastOne(count) - 1, Size.Columns - 1);
        Buffer.ClearLine(Cursor.Row, Cursor.Column, last, EraseStyle);
    }

    /// <summary>Inserts blank cells at the cursor, pushing the rest of the line right (<c>ICH</c>).</summary>
    public void InsertCharacters(int count)
        => Buffer.InsertCells(Cursor.Row, Cursor.Column, AtLeastOne(count), EraseStyle);

    /// <summary>Deletes cells at the cursor, pulling the rest of the line left (<c>DCH</c>).</summary>
    public void DeleteCharacters(int count)
        => Buffer.DeleteCells(Cursor.Row, Cursor.Column, AtLeastOne(count), EraseStyle);

    /// <summary>
    /// Inserts blank lines at the cursor row, pushing the rest of the scrolling region down
    /// (<c>IL</c>). Ignored when the cursor is outside the region.
    /// </summary>
    public void InsertLines(int count)
    {
        if (!ScrollRegion.Contains(Cursor.Row))
        {
            return;
        }

        Buffer.ScrollDown(new ScrollRegion(Cursor.Row, ScrollRegion.Bottom), AtLeastOne(count), EraseStyle);
        Cursor.MoveTo(0, Cursor.Row);
    }

    /// <summary>
    /// Deletes lines at the cursor row, pulling the rest of the scrolling region up (<c>DL</c>).
    /// Ignored when the cursor is outside the region.
    /// </summary>
    public void DeleteLines(int count)
    {
        if (!ScrollRegion.Contains(Cursor.Row))
        {
            return;
        }

        Buffer.ScrollUp(new ScrollRegion(Cursor.Row, ScrollRegion.Bottom), AtLeastOne(count), EraseStyle);
        Cursor.MoveTo(0, Cursor.Row);
    }

    /// <summary>Scrolls the region up without moving the cursor (<c>SU</c>).</summary>
    public void ScrollUp(int count) => Buffer.ScrollUp(ScrollRegion, AtLeastOne(count), EraseStyle);

    /// <summary>Scrolls the region down without moving the cursor (<c>SD</c>).</summary>
    public void ScrollDown(int count) => Buffer.ScrollDown(ScrollRegion, AtLeastOne(count), EraseStyle);

    /// <summary>
    /// Sets the scrolling region and homes the cursor, as <c>DECSTBM</c> does.
    /// </summary>
    /// <returns>
    /// False when the request is rejected. A region must lie on the screen and be at least two rows
    /// tall; a one-row region could not scroll, and real terminals ignore such a request rather
    /// than adopting it.
    /// </returns>
    public bool SetScrollRegion(int top, int bottom)
    {
        if (top < 0 || bottom >= Size.Rows || top >= bottom)
        {
            return false;
        }

        ScrollRegion = new ScrollRegion(top, bottom);
        MoveCursorTo(0, 0);
        return true;
    }

    /// <summary>Restores the scrolling region to the whole screen and homes the cursor.</summary>
    public void ResetScrollRegion()
    {
        ScrollRegion = ScrollRegion.FullScreen(Size);
        MoveCursorTo(0, 0);
    }

    /// <summary>
    /// Changes the screen size, keeping the cursor's line visible.
    /// </summary>
    /// <remarks>
    /// When the screen loses rows, the naive approach - dropping rows from the bottom - would throw
    /// away the line the user is typing on. Instead the content is scrolled up by however far the
    /// cursor would have fallen off, so the prompt survives the resize. The scrolling region is
    /// reset, as it is on real terminals, because margins set for one screen height rarely make
    /// sense at another.
    /// </remarks>
    public void Resize(TerminalSize size)
    {
        if (size == Size)
        {
            return;
        }

        if (size.Rows < Size.Rows)
        {
            var overflow = Cursor.Row - (size.Rows - 1);
            if (overflow > 0)
            {
                Buffer.ScrollUp(ScrollRegion.FullScreen(Size), overflow, EraseStyle);
                Cursor.Row -= overflow;
            }
        }

        _primaryBuffer.Resize(size, CellStyle.Default);
        _alternateBuffer?.Resize(size, CellStyle.Default);
        TabStops.Resize(size.Columns);
        ScrollRegion = ScrollRegion.FullScreen(size);

        Cursor.MoveTo(
            Math.Min(Cursor.Column, size.Columns - 1),
            Math.Min(Cursor.Row, size.Rows - 1));
    }

    /// <summary>
    /// Returns the terminal to its power-on state, as <c>RIS</c> (<c>ESC c</c>) does.
    /// </summary>
    public void Reset()
    {
        CurrentStyle = CellStyle.Default;
        AutoWrap = true;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        BracketedPaste = false;
        OriginMode = false;
        InsertMode = false;
        G0CharacterSet = CharacterSet.UsAscii;
        G1CharacterSet = CharacterSet.UsAscii;
        UsingG1 = false;
        DisableAlternateScreen(restoreCursor: false);
        _savedCursor = null;

        Buffer.Clear(CellStyle.Default);
        TabStops.Reset();
        ScrollRegion = ScrollRegion.FullScreen(Size);

        Cursor.MoveTo(0, 0);
        Cursor.IsVisible = true;
        Cursor.Style = CursorStyle.Block;
    }

    /// <summary>
    /// A CSI parameter of zero means one. Applying that here keeps the rule in a single place
    /// rather than at every call site in the parser.
    /// </summary>
    private static int AtLeastOne(int count) => count < 1 ? 1 : count;

    private void LineFeedCore()
    {
        if (Cursor.Row == ScrollRegion.Bottom)
        {
            Buffer.ScrollUp(ScrollRegion, 1, EraseStyle);
        }
        else if (Cursor.Row < Size.Rows - 1)
        {
            Cursor.Row++;
        }
    }

    private void WrapToNextLine()
    {
        Buffer.SetLineWrapped(Cursor.Row, true);
        Cursor.PendingWrap = false;
        LineFeedCore();
        Cursor.Column = 0;
    }

    private void WriteCharacter(Rune rune, int width)
    {
        var row = Cursor.Row;
        var column = Cursor.Column;

        BreakWideCharacterAt(column, row);
        if (width == CharacterWidth.DoubleWidth)
        {
            BreakWideCharacterAt(column + 1, row);
        }

        if (InsertMode)
        {
            // Insert mode pushes the rest of the line right instead of overwriting it.
            Buffer.InsertCells(row, column, width, EraseStyle);
        }

        var role = width == CharacterWidth.DoubleWidth ? CellRole.WideLeading : CellRole.Normal;
        Buffer.SetCell(column, row, new TerminalCell(rune, CurrentStyle, role));

        if (width == CharacterWidth.DoubleWidth)
        {
            Buffer.SetCell(column + 1, row, TerminalCell.WideTrailing(CurrentStyle));
        }

        Advance(width);
    }

    /// <summary>
    /// Blanks the other half of a double-width character that is about to be partly overwritten.
    /// </summary>
    /// <remarks>
    /// Overwriting one cell of a wide pair would otherwise leave the other behind: a leading cell
    /// with nothing to spill into, or an orphaned placeholder holding a column that no character
    /// occupies. Either way the rest of the line shifts by one, which is the visible corruption
    /// this prevents.
    /// </remarks>
    private void BreakWideCharacterAt(int column, int row)
    {
        var cell = Buffer[column, row];

        if (cell.IsWideLeading && column + 1 < Size.Columns)
        {
            Buffer.SetCell(column + 1, row, TerminalCell.Empty(EraseStyle));
        }
        else if (cell.IsWideTrailing && column > 0)
        {
            Buffer.SetCell(column - 1, row, TerminalCell.Empty(EraseStyle));
        }
    }

    private void Advance(int width)
    {
        var next = Cursor.Column + width;

        if (next >= Size.Columns)
        {
            // Sit in the last column and defer the wrap. With auto-wrap disabled there is no wrap
            // to defer, so the cursor simply stays put and the next character overwrites this one.
            Cursor.Column = Size.Columns - 1;
            Cursor.PendingWrap = AutoWrap;
        }
        else
        {
            Cursor.Column = next;
        }
    }
}
