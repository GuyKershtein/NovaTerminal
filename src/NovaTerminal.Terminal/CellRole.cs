namespace NovaTerminal.Terminal;

/// <summary>
/// The part a cell plays in the grid, as distinct from how it looks.
/// </summary>
/// <remarks>
/// This exists because a character is not always one column wide. East Asian ideographs and most
/// emoji occupy two columns, and a fixed grid can only represent that as a pair of cells: one that
/// carries the character and one that reserves the column beside it. The three roles are mutually
/// exclusive, which is why this is a plain enumeration rather than a set of flags.
/// </remarks>
public enum CellRole : byte
{
    /// <summary>An ordinary single-width cell.</summary>
    Normal = 0,

    /// <summary>
    /// Holds the first half of a double-width character. The glyph is drawn from here and spills
    /// over the cell to its right.
    /// </summary>
    WideLeading = 1,

    /// <summary>
    /// The placeholder second half of a double-width character. It holds no character of its own -
    /// the renderer skips it and text extraction ignores it - but it occupies a column so that
    /// everything after it stays aligned.
    /// </summary>
    WideTrailing = 2,
}
