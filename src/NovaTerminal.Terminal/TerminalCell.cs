using System.Text;
using NovaTerminal.Core;

namespace NovaTerminal.Terminal;

/// <summary>
/// One character cell of the virtual screen: what is in it, how it looks, and how it occupies the
/// grid.
/// </summary>
/// <remarks>
/// <para>
/// The fields are stored flat rather than as a nested <see cref="CellStyle"/> so that the runtime
/// can pack them into sixteen bytes. A nested struct would be padded to its own alignment and cost
/// four bytes more per cell - twenty-five percent, multiplied by every cell of every scrollback
/// line. <see cref="Style"/> reassembles the value on demand, which costs nothing at run time.
/// </para>
/// <para>
/// A zeroed cell is a valid blank cell in default colours, so allocating or clearing a buffer never
/// needs an initialisation pass over its cells.
/// </para>
/// </remarks>
public readonly struct TerminalCell : IEquatable<TerminalCell>
{
    /// <summary>The character shown in place of a cell that has never been written to.</summary>
    public static readonly Rune Blank = new(' ');

    private readonly Rune _character;
    private readonly TerminalColor _foreground;
    private readonly TerminalColor _background;
    private readonly TextAttributes _attributes;
    private readonly CellRole _role;

    /// <summary>Creates a cell.</summary>
    /// <param name="character">The character to display.</param>
    /// <param name="style">Colours and attributes.</param>
    /// <param name="role">The part this cell plays in the grid, for double-width characters.</param>
    public TerminalCell(Rune character, CellStyle style, CellRole role = CellRole.Normal)
    {
        _character = character;
        _foreground = style.Foreground;
        _background = style.Background;
        _attributes = style.Attributes;
        _role = role;
    }

    /// <summary>
    /// Creates an empty cell carrying a style. Used when erasing, so that the erased region takes
    /// the background colour currently selected.
    /// </summary>
    public static TerminalCell Empty(CellStyle style) => new(default, style);

    /// <summary>Creates the placeholder cell that follows a double-width character.</summary>
    public static TerminalCell WideTrailing(CellStyle style) => new(default, style, CellRole.WideTrailing);

    /// <summary>
    /// The character in this cell. A value of <c>U+0000</c> means nothing has been written here;
    /// use <see cref="DisplayCharacter"/> when a printable character is required.
    /// </summary>
    public Rune Character => _character;

    /// <summary>
    /// The character to draw or copy: the same as <see cref="Character"/>, except that an unwritten
    /// cell reads as a space.
    /// </summary>
    public Rune DisplayCharacter => IsEmpty ? Blank : _character;

    /// <summary>Colours and attributes, reassembled from the packed fields.</summary>
    public CellStyle Style => new(_foreground, _background, _attributes);

    /// <summary>Colour of the glyph.</summary>
    public TerminalColor Foreground => _foreground;

    /// <summary>Colour behind the glyph.</summary>
    public TerminalColor Background => _background;

    /// <summary>Bold, underline, inverse and the rest.</summary>
    public TextAttributes Attributes => _attributes;

    /// <summary>The part this cell plays in the grid.</summary>
    public CellRole Role => _role;

    /// <summary>True when no character has been written to this cell.</summary>
    public bool IsEmpty => _character.Value == 0;

    /// <summary>True when this cell holds the first half of a double-width character.</summary>
    public bool IsWideLeading => _role == CellRole.WideLeading;

    /// <summary>True when this cell is the placeholder half of a double-width character.</summary>
    public bool IsWideTrailing => _role == CellRole.WideTrailing;

    /// <summary>True when this cell is either half of a double-width character.</summary>
    public bool IsWidePart => _role != CellRole.Normal;

    /// <summary>Returns a copy of this cell with different styling.</summary>
    public TerminalCell WithStyle(CellStyle style) => new(_character, style, _role);

    /// <inheritdoc />
    public bool Equals(TerminalCell other)
        => _character.Equals(other._character)
           && _foreground == other._foreground
           && _background == other._background
           && _attributes == other._attributes
           && _role == other._role;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TerminalCell other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(_character, _foreground, _background, _attributes, _role);

    /// <summary>Tests two cells for equality.</summary>
    public static bool operator ==(TerminalCell left, TerminalCell right) => left.Equals(right);

    /// <summary>Tests two cells for inequality.</summary>
    public static bool operator !=(TerminalCell left, TerminalCell right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString()
        => IsEmpty ? $"(empty, {_role})" : $"'{_character}' ({_role})";
}
