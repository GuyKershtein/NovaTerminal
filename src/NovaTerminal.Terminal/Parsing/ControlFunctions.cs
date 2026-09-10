namespace NovaTerminal.Terminal.Parsing;

/// <summary>
/// The final bytes that identify control sequences, named after the functions they select.
/// </summary>
/// <remarks>
/// A dispatch table full of bare character literals is a dispatch table nobody can review against
/// the standard. The names here are the standard's own abbreviations, so each case can be checked
/// against ECMA-48 or the xterm control sequences document directly.
/// </remarks>
public static class CsiFinal
{
    /// <summary>Insert Character: insert blanks, pushing the line right.</summary>
    public const char InsertCharacter = '@';

    /// <summary>Cursor Up.</summary>
    public const char CursorUp = 'A';

    /// <summary>Cursor Down.</summary>
    public const char CursorDown = 'B';

    /// <summary>Cursor Forward.</summary>
    public const char CursorForward = 'C';

    /// <summary>Cursor Backward.</summary>
    public const char CursorBackward = 'D';

    /// <summary>Cursor Next Line: down, and to the first column.</summary>
    public const char CursorNextLine = 'E';

    /// <summary>Cursor Previous Line: up, and to the first column.</summary>
    public const char CursorPreviousLine = 'F';

    /// <summary>Cursor Horizontal Absolute: move to a column on the current row.</summary>
    public const char CursorHorizontalAbsolute = 'G';

    /// <summary>Cursor Position: move to a row and column.</summary>
    public const char CursorPosition = 'H';

    /// <summary>Cursor Horizontal Tabulation: advance by tab stops.</summary>
    public const char CursorForwardTabulation = 'I';

    /// <summary>Erase in Display.</summary>
    public const char EraseInDisplay = 'J';

    /// <summary>Erase in Line.</summary>
    public const char EraseInLine = 'K';

    /// <summary>Insert Line.</summary>
    public const char InsertLine = 'L';

    /// <summary>Delete Line.</summary>
    public const char DeleteLine = 'M';

    /// <summary>Delete Character.</summary>
    public const char DeleteCharacter = 'P';

    /// <summary>Scroll Up: move the scrolling region's contents up.</summary>
    public const char ScrollUp = 'S';

    /// <summary>Scroll Down.</summary>
    public const char ScrollDown = 'T';

    /// <summary>Erase Character: blank cells in place without shifting the line.</summary>
    public const char EraseCharacter = 'X';

    /// <summary>Cursor Backward Tabulation.</summary>
    public const char CursorBackwardTabulation = 'Z';

    /// <summary>Horizontal Position Absolute: another spelling of <see cref="CursorHorizontalAbsolute"/>.</summary>
    public const char HorizontalPositionAbsolute = '`';

    /// <summary>Horizontal Position Relative: another spelling of <see cref="CursorForward"/>.</summary>
    public const char HorizontalPositionRelative = 'a';

    /// <summary>Device Attributes: the terminal is being asked to identify itself.</summary>
    public const char DeviceAttributes = 'c';

    /// <summary>Vertical Position Absolute: move to a row in the current column.</summary>
    public const char VerticalPositionAbsolute = 'd';

    /// <summary>Vertical Position Relative: another spelling of <see cref="CursorDown"/>.</summary>
    public const char VerticalPositionRelative = 'e';

    /// <summary>Horizontal and Vertical Position: another spelling of <see cref="CursorPosition"/>.</summary>
    public const char HorizontalVerticalPosition = 'f';

    /// <summary>Tabulation Clear.</summary>
    public const char TabulationClear = 'g';

    /// <summary>Set Mode.</summary>
    public const char SetMode = 'h';

    /// <summary>Reset Mode.</summary>
    public const char ResetMode = 'l';

    /// <summary>Select Graphic Rendition: colours and text attributes.</summary>
    public const char SelectGraphicRendition = 'm';

    /// <summary>Device Status Report.</summary>
    public const char DeviceStatusReport = 'n';

    /// <summary>Set Top and Bottom Margins, defining the scrolling region.</summary>
    public const char SetScrollingRegion = 'r';

    /// <summary>Save Cursor, in the ANSI.SYS spelling.</summary>
    public const char SaveCursor = 's';

    /// <summary>Restore Cursor, in the ANSI.SYS spelling.</summary>
    public const char RestoreCursor = 'u';
}

/// <summary>The final bytes of two-character escape sequences.</summary>
public static class EscapeFinal
{
    /// <summary>Reset to Initial State: everything back to power-on defaults.</summary>
    public const char ResetToInitialState = 'c';

    /// <summary>Index: move down one line, scrolling at the bottom margin.</summary>
    public const char Index = 'D';

    /// <summary>Next Line: carriage return followed by index.</summary>
    public const char NextLine = 'E';

    /// <summary>Horizontal Tabulation Set: place a tab stop at the cursor.</summary>
    public const char TabulationSet = 'H';

    /// <summary>Reverse Index: move up one line, scrolling at the top margin.</summary>
    public const char ReverseIndex = 'M';

    /// <summary>Save Cursor (DECSC), including the current pen.</summary>
    public const char SaveCursor = '7';

    /// <summary>Restore Cursor (DECRC).</summary>
    public const char RestoreCursor = '8';

    /// <summary>Application Keypad mode (DECKPAM).</summary>
    public const char ApplicationKeypad = '=';

    /// <summary>Numeric Keypad mode (DECKPNM).</summary>
    public const char NumericKeypad = '>';

    /// <summary>String Terminator: ends an OSC or DCS string.</summary>
    public const char StringTerminator = '\\';
}
