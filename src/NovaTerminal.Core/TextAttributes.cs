namespace NovaTerminal.Core;

/// <summary>
/// Non-colour styling applied to a run of characters, set and cleared by SGR sequences.
/// </summary>
/// <remarks>
/// Backed by a <see cref="ushort"/> so that a screen cell stays compact; see
/// <see cref="TerminalColor"/> for why cell size matters.
/// </remarks>
[Flags]
public enum TextAttributes : ushort
{
    /// <summary>No styling (SGR 0).</summary>
    None = 0,

    /// <summary>SGR 1. Conventionally rendered with a heavier font weight.</summary>
    Bold = 1 << 0,

    /// <summary>SGR 2. Rendered by blending the foreground toward the background.</summary>
    Faint = 1 << 1,

    /// <summary>SGR 3.</summary>
    Italic = 1 << 2,

    /// <summary>SGR 4.</summary>
    Underline = 1 << 3,

    /// <summary>SGR 5. Whether this actually blinks is a rendering policy decision.</summary>
    Blink = 1 << 4,

    /// <summary>SGR 7. Swaps foreground and background at render time.</summary>
    Inverse = 1 << 5,

    /// <summary>SGR 8. The cell occupies space but paints nothing.</summary>
    Invisible = 1 << 6,

    /// <summary>SGR 9.</summary>
    Strikethrough = 1 << 7,
}
