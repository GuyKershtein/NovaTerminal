using System.Text;
using NovaTerminal.Core;

namespace NovaTerminal.Terminal;

/// <summary>
/// The screen-level features of a terminal: the alternate screen, character sets, and the modes
/// that change how printing and positioning behave.
/// </summary>
public sealed partial class TerminalState
{
    private readonly TerminalBuffer _primaryBuffer;

    private TerminalBuffer? _alternateBuffer;
    private TerminalCursorState? _savedPrimaryCursor;

    /// <summary>
    /// Whether the alternate screen is in use.
    /// </summary>
    /// <remarks>
    /// The alternate screen is how a full-screen program takes over the display without destroying
    /// what was there before. <c>vim</c> switches to it on entry and back on exit, which is why
    /// your shell history reappears intact when you quit. It is a genuinely separate buffer, and it
    /// deliberately has no scrollback: scrolling back through a half-drawn editor screen would be
    /// meaningless.
    /// </remarks>
    public bool IsAlternateScreenActive => _alternateBuffer is not null;

    /// <summary>
    /// Whether pasted text is wrapped in markers so a program can distinguish it from typing
    /// (mode 2004).
    /// </summary>
    public bool BracketedPaste { get; set; }

    /// <summary>
    /// Whether cursor positioning is relative to the scrolling region (<c>DECOM</c>).
    /// </summary>
    /// <remarks>
    /// With origin mode set, row one means the top margin rather than the top of the screen, and
    /// the cursor cannot leave the region at all. A program that has reserved a header row uses it
    /// so its own coordinates need no adjustment.
    /// </remarks>
    public bool OriginMode { get; set; }

    /// <summary>
    /// Whether printing inserts rather than overwrites (<c>IRM</c>).
    /// </summary>
    /// <remarks>
    /// In insert mode each printed character pushes the rest of the line right instead of
    /// replacing what was there, which is how some editors implement typing in the middle of a line
    /// without redrawing it.
    /// </remarks>
    public bool InsertMode { get; set; }

    /// <summary>The character set mapped to the G0 slot.</summary>
    public CharacterSet G0CharacterSet { get; set; } = CharacterSet.UsAscii;

    /// <summary>The character set mapped to the G1 slot.</summary>
    public CharacterSet G1CharacterSet { get; set; } = CharacterSet.UsAscii;

    /// <summary>
    /// Which slot printed characters are drawn from, as selected by the shift-in and shift-out
    /// controls.
    /// </summary>
    public bool UsingG1 { get; set; }

    /// <summary>
    /// Switches to the alternate screen, optionally saving the cursor first.
    /// </summary>
    /// <param name="saveCursor">
    /// True for mode 1049, which saves the cursor and clears the alternate screen as it switches -
    /// the behaviour almost every program actually wants. False for the older modes 47 and 1047,
    /// which do neither.
    /// </param>
    public void EnableAlternateScreen(bool saveCursor)
    {
        if (_alternateBuffer is not null)
        {
            return;
        }

        if (saveCursor)
        {
            _savedPrimaryCursor = Cursor.Capture(CurrentStyle);
        }

        _alternateBuffer = new TerminalBuffer(Size);
        ScrollRegion = ScrollRegion.FullScreen(Size);

        if (saveCursor)
        {
            MoveCursorTo(0, 0);
        }
    }

    /// <summary>
    /// Returns to the primary screen, restoring the cursor when it was saved on the way in.
    /// </summary>
    public void DisableAlternateScreen(bool restoreCursor)
    {
        if (_alternateBuffer is null)
        {
            return;
        }

        _alternateBuffer = null;
        ScrollRegion = ScrollRegion.FullScreen(Size);

        // The primary screen is untouched underneath, so everything the user had before the
        // full-screen program started is still exactly where they left it.
        _primaryBuffer.MarkAllRowsDirty();

        if (restoreCursor && _savedPrimaryCursor is { } saved)
        {
            Cursor.Restore(saved with
            {
                Column = Math.Clamp(saved.Column, 0, Size.Columns - 1),
                Row = Math.Clamp(saved.Row, 0, Size.Rows - 1),
            });

            CurrentStyle = saved.Style;
        }

        _savedPrimaryCursor = null;
    }

    /// <summary>
    /// Designates a character set into a slot, as <c>ESC ( B</c> and friends do.
    /// </summary>
    public void DesignateCharacterSet(int slot, CharacterSet characterSet)
    {
        if (slot == 0)
        {
            G0CharacterSet = characterSet;
        }
        else if (slot == 1)
        {
            G1CharacterSet = characterSet;
        }
    }

    /// <summary>
    /// Fills the screen with the letter E, as <c>DECALN</c> does.
    /// </summary>
    /// <remarks>
    /// This exists to check screen alignment on real hardware and is used by terminal conformance
    /// suites. Implementing it costs a few lines and makes those suites usable.
    /// </remarks>
    public void ScreenAlignmentPattern()
    {
        var cell = new TerminalCell(new Rune('E'), CellStyle.Default);

        for (var row = 0; row < Size.Rows; row++)
        {
            for (var column = 0; column < Size.Columns; column++)
            {
                Buffer.SetCell(column, row, cell);
            }
        }

        ScrollRegion = ScrollRegion.FullScreen(Size);
        MoveCursorTo(0, 0);
    }

    /// <summary>
    /// The row a one-based protocol coordinate refers to, accounting for origin mode.
    /// </summary>
    public int ResolveRow(int oneBasedRow)
    {
        var row = Math.Max(1, oneBasedRow) - 1;

        return OriginMode ? Math.Min(ScrollRegion.Top + row, ScrollRegion.Bottom) : row;
    }

    /// <summary>
    /// Translates a character through the active character set.
    /// </summary>
    /// <remarks>
    /// The DEC Special Graphics set replaces the punctuation range with box-drawing characters, and
    /// it is still how many text-mode programs draw borders: they select it, print <c>lqk</c>, and
    /// expect a corner, a horizontal line and another corner. Without the translation the user sees
    /// literal letters where a box should be.
    /// </remarks>
    private Rune TranslateForCharacterSet(Rune rune)
    {
        var active = UsingG1 ? G1CharacterSet : G0CharacterSet;

        if (active != CharacterSet.DecSpecialGraphics)
        {
            return rune;
        }

        return DecSpecialGraphics.Translate(rune);
    }
}
