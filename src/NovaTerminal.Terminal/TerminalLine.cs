using System.Text;
using NovaTerminal.Core;

namespace NovaTerminal.Terminal;

/// <summary>
/// One row of the virtual screen.
/// </summary>
/// <remarks>
/// <para>
/// A line is a reference type even though a cell is a value type, and that is a deliberate
/// asymmetry. Scrolling the screen then costs a rotation of line <em>references</em> rather than a
/// copy of every cell, and the line that scrolls away can be recycled instead of reallocated. A
/// terminal scrolls constantly, so this is the difference between scrolling being free and
/// scrolling being the dominant cost of running <c>cat</c> on a large file.
/// </para>
/// </remarks>
public sealed class TerminalLine
{
    private TerminalCell[] _cells;

    /// <summary>Creates a blank line of the given width.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="columns"/> is not positive.</exception>
    public TerminalLine(int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        _cells = new TerminalCell[columns];
    }

    /// <summary>The number of cells in this line.</summary>
    public int Columns => _cells.Length;

    /// <summary>
    /// True when this line ran out of width and its text continues on the next line.
    /// </summary>
    /// <remarks>
    /// The distinction between a line that wrapped and a line that ended matters later: copied text
    /// should rejoin a wrapped line rather than insert a line break, and a resize should reflow it.
    /// The information is only available at the moment the wrap happens, so it is recorded here.
    /// </remarks>
    public bool IsWrapped { get; set; }

    /// <summary>Gets or sets one cell.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The column is outside the line.</exception>
    public TerminalCell this[int column]
    {
        get
        {
            ThrowIfColumnOutOfRange(column);
            return _cells[column];
        }

        set
        {
            ThrowIfColumnOutOfRange(column);
            _cells[column] = value;
        }
    }

    /// <summary>
    /// The line's cells, for callers that read many at once such as the renderer. Read-only,
    /// because every mutation has to go through a method that can maintain the line's invariants.
    /// </summary>
    public ReadOnlySpan<TerminalCell> Cells => _cells;

    /// <summary>
    /// Writes a cell without checking the column, for callers that have already checked it.
    /// </summary>
    /// <remarks>
    /// Internal, and used only by <see cref="TerminalBuffer"/> immediately after it has validated
    /// the coordinate. Printing is the hottest path in the engine, and it was paying for the same
    /// bounds check three times over.
    /// </remarks>
    internal void SetUnchecked(int column, TerminalCell cell) => _cells[column] = cell;

    /// <summary>Erases the whole line, filling it with the given style.</summary>
    public void Clear(CellStyle style)
    {
        _cells.AsSpan().Fill(TerminalCell.Empty(style));
        IsWrapped = false;
    }

    /// <summary>Erases an inclusive range of cells, filling them with the given style.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range lies outside the line.</exception>
    public void Clear(int fromColumn, int toColumn, CellStyle style)
    {
        ThrowIfColumnOutOfRange(fromColumn);
        ThrowIfColumnOutOfRange(toColumn);

        if (toColumn < fromColumn)
        {
            return;
        }

        _cells.AsSpan(fromColumn, toColumn - fromColumn + 1).Fill(TerminalCell.Empty(style));
    }

    /// <summary>
    /// Inserts blank cells at <paramref name="column"/>, shifting the rest of the line right. Cells
    /// pushed past the end of the line are discarded, as on real hardware.
    /// </summary>
    public void InsertCells(int column, int count, CellStyle style)
    {
        ThrowIfColumnOutOfRange(column);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var available = Columns - column;
        count = Math.Min(count, available);
        if (count == 0)
        {
            return;
        }

        var span = _cells.AsSpan();
        span.Slice(column, available - count).CopyTo(span[(column + count)..]);
        span.Slice(column, count).Fill(TerminalCell.Empty(style));
    }

    /// <summary>
    /// Deletes cells at <paramref name="column"/>, shifting the rest of the line left and filling
    /// the vacated cells at the right-hand end.
    /// </summary>
    public void DeleteCells(int column, int count, CellStyle style)
    {
        ThrowIfColumnOutOfRange(column);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var available = Columns - column;
        count = Math.Min(count, available);
        if (count == 0)
        {
            return;
        }

        var span = _cells.AsSpan();
        span[(column + count)..].CopyTo(span[column..]);
        span[(Columns - count)..].Fill(TerminalCell.Empty(style));
    }

    /// <summary>
    /// Changes the line's width, preserving as much content as still fits.
    /// </summary>
    public void Resize(int columns, CellStyle style)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        if (columns == Columns)
        {
            return;
        }

        var resized = new TerminalCell[columns];
        var copied = Math.Min(columns, Columns);
        _cells.AsSpan(0, copied).CopyTo(resized);

        if (columns > Columns)
        {
            resized.AsSpan(Columns).Fill(TerminalCell.Empty(style));
        }
        else if (copied > 0 && resized[copied - 1].IsWideLeading)
        {
            // A double-width character cannot survive losing its second half.
            resized[copied - 1] = TerminalCell.Empty(style);
        }

        _cells = resized;
    }

    /// <summary>
    /// Reads the line as text, as it would be copied to the clipboard.
    /// </summary>
    /// <param name="trimTrailingBlanks">
    /// When true, unwritten cells at the end of the line are omitted. This is what a user expects
    /// from a selection; a caller reconstructing the screen exactly should pass false.
    /// </param>
    public string GetText(bool trimTrailingBlanks = true)
    {
        var end = Columns - 1;

        if (trimTrailingBlanks)
        {
            while (end >= 0 && _cells[end].IsEmpty && !_cells[end].IsWideTrailing)
            {
                end--;
            }
        }

        if (end < 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(end + 1);
        for (var column = 0; column <= end; column++)
        {
            var cell = _cells[column];

            // The second half of a double-width character is a layout placeholder, not a character.
            if (cell.IsWideTrailing)
            {
                continue;
            }

            builder.Append(cell.DisplayCharacter);
        }

        return builder.ToString();
    }

    private void ThrowIfColumnOutOfRange(int column)
    {
        if ((uint)column >= (uint)Columns)
        {
            throw new ArgumentOutOfRangeException(
                nameof(column), column, $"Column must be between 0 and {Columns - 1}.");
        }
    }
}
