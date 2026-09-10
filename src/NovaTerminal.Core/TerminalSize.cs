namespace NovaTerminal.Core;

/// <summary>
/// A terminal's dimensions in character cells.
/// </summary>
/// <remarks>
/// Rows and columns - not pixels - are the unit the shell and the VT protocol understand. The GUI
/// converts a pixel viewport into a <see cref="TerminalSize"/> exactly once, at the boundary, and
/// everything below that boundary works in cells.
/// <para>
/// Note that <c>default(TerminalSize)</c> is 0x0 and therefore not a legal terminal size; the
/// struct's constructor rejects such values, and <see cref="Clamp"/> exists for callers (chiefly
/// layout code) whose arithmetic can legitimately produce zero or negative values.
/// </para>
/// </remarks>
public readonly record struct TerminalSize
{
    /// <summary>The smallest legal column count.</summary>
    public const int MinColumns = 1;

    /// <summary>The smallest legal row count.</summary>
    public const int MinRows = 1;

    /// <summary>
    /// An upper bound on columns. Not a protocol limit; a guard so that a malformed resize request
    /// cannot ask the engine to allocate an absurd buffer.
    /// </summary>
    public const int MaxColumns = 4096;

    /// <summary>An upper bound on rows, for the same reason as <see cref="MaxColumns"/>.</summary>
    public const int MaxRows = 4096;

    /// <summary>The 80x24 default inherited from the DEC VT100.</summary>
    public static TerminalSize Default => new(80, 24);

    /// <summary>Creates a size, rejecting values outside the supported range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Either dimension is out of range.</exception>
    public TerminalSize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, MinColumns);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, MaxColumns);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, MinRows);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, MaxRows);

        Columns = columns;
        Rows = rows;
    }

    /// <summary>The number of character cells across.</summary>
    public int Columns { get; }

    /// <summary>The number of character cells down.</summary>
    public int Rows { get; }

    /// <summary>The total number of cells on one screen of this size.</summary>
    public int CellCount => Columns * Rows;

    /// <summary>
    /// Creates a size from potentially out-of-range input by clamping it into the legal range.
    /// Use this when converting a pixel viewport, which may momentarily be zero-sized during layout.
    /// </summary>
    public static TerminalSize Clamp(int columns, int rows) => new(
        Math.Clamp(columns, MinColumns, MaxColumns),
        Math.Clamp(rows, MinRows, MaxRows));

    /// <summary>Tests whether the given zero-based cell coordinate lies on a screen of this size.</summary>
    public bool Contains(int column, int row)
        => (uint)column < (uint)Columns && (uint)row < (uint)Rows;

    /// <summary>Deconstructs the size into its two dimensions.</summary>
    public void Deconstruct(out int columns, out int rows)
    {
        columns = Columns;
        rows = Rows;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Columns}x{Rows}";
}
