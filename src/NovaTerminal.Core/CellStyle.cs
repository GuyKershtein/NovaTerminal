namespace NovaTerminal.Core;

/// <summary>
/// The complete visual styling of a character cell: two colours and a set of attribute flags.
/// </summary>
/// <remarks>
/// <para>
/// This type plays two roles. It is stored in every screen cell, and it is also the terminal's
/// current "pen" - the styling that SGR sequences accumulate and that newly printed characters
/// inherit. Modelling both with one immutable value keeps printing to a single field copy.
/// </para>
/// <para>
/// <c>default(CellStyle)</c> is the default pen: theme foreground, theme background, no attributes.
/// A zeroed screen buffer is therefore already in a valid, correct initial state.
/// </para>
/// </remarks>
/// <param name="Foreground">Colour of the glyph.</param>
/// <param name="Background">Colour behind the glyph.</param>
/// <param name="Attributes">Bold, underline, inverse and the rest.</param>
public readonly record struct CellStyle(
    TerminalColor Foreground,
    TerminalColor Background,
    TextAttributes Attributes)
{
    /// <summary>The default pen: theme colours and no attributes, as after <c>SGR 0</c>.</summary>
    public static CellStyle Default => default;

    /// <summary>True when this style is the default in every respect.</summary>
    public bool IsDefault => this == default;

    /// <summary>Returns a copy with a different foreground.</summary>
    public CellStyle WithForeground(TerminalColor foreground) => this with { Foreground = foreground };

    /// <summary>Returns a copy with a different background.</summary>
    public CellStyle WithBackground(TerminalColor background) => this with { Background = background };

    /// <summary>Returns a copy with the given attributes added.</summary>
    public CellStyle WithAttributes(TextAttributes attributes)
        => this with { Attributes = Attributes | attributes };

    /// <summary>Returns a copy with the given attributes removed.</summary>
    public CellStyle WithoutAttributes(TextAttributes attributes)
        => this with { Attributes = Attributes & ~attributes };

    /// <summary>Tests whether every attribute in <paramref name="attributes"/> is set.</summary>
    public bool HasAttributes(TextAttributes attributes) => (Attributes & attributes) == attributes;

    /// <summary>
    /// The style used to fill cells erased while this pen is active.
    /// </summary>
    /// <remarks>
    /// Erasing applies the current <em>background</em> colour but no other styling - the behaviour
    /// terminals call "background colour erase". Keeping the foreground and attributes would be
    /// visible wherever an attribute paints outside a glyph, such as underline or strikethrough,
    /// producing stray lines across supposedly blank space.
    /// </remarks>
    public CellStyle ForErase() => new(TerminalColor.Default, Background, TextAttributes.None);
}
