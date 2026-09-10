namespace NovaTerminal.Input;

/// <summary>
/// A toolkit-neutral identifier for the non-textual keys a terminal must encode specially.
/// </summary>
/// <remarks>
/// Printable characters are not listed here: they travel as text and are encoded as UTF-8. This
/// enum covers only the keys that have no character of their own and must be turned into escape
/// sequences - arrows, navigation, editing and function keys. Keeping the set toolkit-neutral is
/// what allows the input layer to be tested without a GUI, and what would allow a second front end
/// to reuse it unchanged.
/// </remarks>
public enum TerminalKey
{
    /// <summary>No special key; the event carries text instead.</summary>
    None = 0,

    /// <summary>Enter or Return.</summary>
    Enter,

    /// <summary>Backspace.</summary>
    Backspace,

    /// <summary>Tab.</summary>
    Tab,

    /// <summary>Escape.</summary>
    Escape,

    /// <summary>Delete (forward delete).</summary>
    Delete,

    /// <summary>Insert.</summary>
    Insert,

    /// <summary>Home.</summary>
    Home,

    /// <summary>End.</summary>
    End,

    /// <summary>Page Up.</summary>
    PageUp,

    /// <summary>Page Down.</summary>
    PageDown,

    /// <summary>Up arrow.</summary>
    Up,

    /// <summary>Down arrow.</summary>
    Down,

    /// <summary>Right arrow.</summary>
    Right,

    /// <summary>Left arrow.</summary>
    Left,

    /// <summary>F1.</summary>
    F1,

    /// <summary>F2.</summary>
    F2,

    /// <summary>F3.</summary>
    F3,

    /// <summary>F4.</summary>
    F4,

    /// <summary>F5.</summary>
    F5,

    /// <summary>F6.</summary>
    F6,

    /// <summary>F7.</summary>
    F7,

    /// <summary>F8.</summary>
    F8,

    /// <summary>F9.</summary>
    F9,

    /// <summary>F10.</summary>
    F10,

    /// <summary>F11.</summary>
    F11,

    /// <summary>F12.</summary>
    F12,
}
