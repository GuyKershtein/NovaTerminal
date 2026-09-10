using System.Buffers;
using System.Text;
using NovaTerminal.Core;

namespace NovaTerminal.Terminal;

/// <summary>
/// The virtual screen: a grid of cells, plus the primitives that move regions of it around.
/// </summary>
/// <remarks>
/// <para>
/// The buffer knows nothing about cursors, escape sequences or styling policy. It stores cells and
/// performs the four structural operations a terminal needs - write, erase, scroll and resize.
/// Anything that depends on <em>where the cursor is</em> belongs a layer up, in
/// <see cref="TerminalState"/>.
/// </para>
/// <para>
/// <b>Damage tracking.</b> Every mutation records which rows changed, so the renderer can repaint
/// only those. Tracking is per row rather than per cell: a finer granularity would cost more to
/// maintain than it saves, because a row is the unit the renderer draws anyway. For this to be
/// reliable, engine code must mutate through this class rather than through the
/// <see cref="TerminalLine"/> objects it hands out.
/// </para>
/// </remarks>
public sealed class TerminalBuffer
{
    private TerminalLine[] _lines;
    private bool[] _dirtyRows;

    /// <summary>Creates a blank buffer of the given size.</summary>
    public TerminalBuffer(TerminalSize size)
    {
        Size = size;
        _lines = new TerminalLine[size.Rows];
        _dirtyRows = new bool[size.Rows];

        for (var row = 0; row < size.Rows; row++)
        {
            _lines[row] = new TerminalLine(size.Columns);
        }

        MarkAllRowsDirty();
    }

    /// <summary>The buffer's dimensions.</summary>
    public TerminalSize Size { get; private set; }

    /// <summary>True when any row has changed since damage was last cleared.</summary>
    public bool HasDamage { get; private set; }

    /// <summary>
    /// Returns a row. Callers outside the engine should treat the result as read-only: mutating a
    /// line directly bypasses damage tracking, and the renderer would not learn that it changed.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The row is outside the buffer.</exception>
    public TerminalLine GetLine(int row)
    {
        ThrowIfRowOutOfRange(row);
        return _lines[row];
    }

    /// <summary>Returns a row's cells for reading, which is what a renderer needs.</summary>
    public ReadOnlySpan<TerminalCell> GetRow(int row)
    {
        ThrowIfRowOutOfRange(row);
        return _lines[row].Cells;
    }

    /// <summary>Reads one cell.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off-screen.</exception>
    public TerminalCell this[int column, int row]
    {
        get
        {
            ThrowIfOutOfRange(column, row);
            return _lines[row][column];
        }
    }

    /// <summary>Writes one cell and records the row as changed.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off-screen.</exception>
    public void SetCell(int column, int row, TerminalCell cell)
    {
        ThrowIfOutOfRange(column, row);
        _lines[row][column] = cell;
        MarkRowDirty(row);
    }

    /// <summary>Records whether a row's text continues onto the row below.</summary>
    public void SetLineWrapped(int row, bool isWrapped)
    {
        ThrowIfRowOutOfRange(row);
        _lines[row].IsWrapped = isWrapped;
    }

    /// <summary>Erases an inclusive span of one row.</summary>
    public void ClearLine(int row, int fromColumn, int toColumn, CellStyle style)
    {
        ThrowIfRowOutOfRange(row);
        _lines[row].Clear(fromColumn, toColumn, style);
        MarkRowDirty(row);
    }

    /// <summary>Erases an inclusive span of rows.</summary>
    public void ClearRows(int fromRow, int toRow, CellStyle style)
    {
        ThrowIfRowOutOfRange(fromRow);
        ThrowIfRowOutOfRange(toRow);

        for (var row = fromRow; row <= toRow; row++)
        {
            _lines[row].Clear(style);
            MarkRowDirty(row);
        }
    }

    /// <summary>Erases the whole buffer.</summary>
    public void Clear(CellStyle style) => ClearRows(0, Size.Rows - 1, style);

    /// <summary>Inserts blank cells within a row, shifting the remainder right.</summary>
    public void InsertCells(int row, int column, int count, CellStyle style)
    {
        ThrowIfRowOutOfRange(row);
        _lines[row].InsertCells(column, count, style);
        MarkRowDirty(row);
    }

    /// <summary>Deletes cells within a row, shifting the remainder left.</summary>
    public void DeleteCells(int row, int column, int count, CellStyle style)
    {
        ThrowIfRowOutOfRange(row);
        _lines[row].DeleteCells(column, count, style);
        MarkRowDirty(row);
    }

    /// <summary>
    /// Scrolls a region up: content moves toward the top of the screen and blank lines appear at
    /// the bottom. This is what a line feed on the last row of the region does.
    /// </summary>
    /// <remarks>
    /// The lines that scroll off the top are not discarded and reallocated - their cell arrays are
    /// cleared and re-inserted at the bottom. Combined with rotating references rather than copying
    /// cells, a scroll costs no allocation at all, which is what keeps a flood of output from
    /// turning into garbage collection pressure.
    /// </remarks>
    public void ScrollUp(ScrollRegion region, int count, CellStyle style)
    {
        ThrowIfRegionOutOfRange(region);

        if (count <= 0)
        {
            return;
        }

        if (count >= region.Height)
        {
            ClearRows(region.Top, region.Bottom, style);
            return;
        }

        var recycled = ArrayPool<TerminalLine>.Shared.Rent(count);
        try
        {
            Array.Copy(_lines, region.Top, recycled, 0, count);
            Array.Copy(_lines, region.Top + count, _lines, region.Top, region.Height - count);

            for (var offset = 0; offset < count; offset++)
            {
                var line = recycled[offset];
                line.Clear(style);
                _lines[region.Bottom - count + 1 + offset] = line;
            }
        }
        finally
        {
            ArrayPool<TerminalLine>.Shared.Return(recycled, clearArray: true);
        }

        MarkRowsDirty(region.Top, region.Bottom);
    }

    /// <summary>
    /// Scrolls a region down: content moves toward the bottom and blank lines appear at the top.
    /// This is what a reverse line feed on the first row of the region does.
    /// </summary>
    public void ScrollDown(ScrollRegion region, int count, CellStyle style)
    {
        ThrowIfRegionOutOfRange(region);

        if (count <= 0)
        {
            return;
        }

        if (count >= region.Height)
        {
            ClearRows(region.Top, region.Bottom, style);
            return;
        }

        var recycled = ArrayPool<TerminalLine>.Shared.Rent(count);
        try
        {
            Array.Copy(_lines, region.Bottom - count + 1, recycled, 0, count);
            Array.Copy(_lines, region.Top, _lines, region.Top + count, region.Height - count);

            for (var offset = 0; offset < count; offset++)
            {
                var line = recycled[offset];
                line.Clear(style);
                _lines[region.Top + offset] = line;
            }
        }
        finally
        {
            ArrayPool<TerminalLine>.Shared.Return(recycled, clearArray: true);
        }

        MarkRowsDirty(region.Top, region.Bottom);
    }

    /// <summary>
    /// Changes the buffer's dimensions, preserving content anchored at the top-left corner.
    /// </summary>
    /// <remarks>
    /// Rows are removed from the bottom when the screen shrinks. Keeping the cursor's line visible
    /// is <see cref="TerminalState"/>'s responsibility, because only it knows where the cursor is;
    /// it scrolls the content up before calling this. Reflowing wrapped lines onto the new width is
    /// a later refinement.
    /// </remarks>
    public void Resize(TerminalSize size, CellStyle style)
    {
        if (size == Size)
        {
            return;
        }

        if (size.Columns != Size.Columns)
        {
            foreach (var line in _lines)
            {
                line.Resize(size.Columns, style);
            }
        }

        if (size.Rows != Size.Rows)
        {
            var resized = new TerminalLine[size.Rows];
            var copied = Math.Min(size.Rows, Size.Rows);
            Array.Copy(_lines, resized, copied);

            for (var row = copied; row < size.Rows; row++)
            {
                resized[row] = new TerminalLine(size.Columns);
            }

            _lines = resized;
            _dirtyRows = new bool[size.Rows];
        }

        Size = size;
        MarkAllRowsDirty();
    }

    /// <summary>Reports whether a row has changed since damage was last cleared.</summary>
    public bool IsRowDirty(int row)
    {
        ThrowIfRowOutOfRange(row);
        return _dirtyRows[row];
    }

    /// <summary>Records a row as changed.</summary>
    public void MarkRowDirty(int row)
    {
        ThrowIfRowOutOfRange(row);
        _dirtyRows[row] = true;
        HasDamage = true;
    }

    /// <summary>Records an inclusive span of rows as changed.</summary>
    public void MarkRowsDirty(int fromRow, int toRow)
    {
        ThrowIfRowOutOfRange(fromRow);
        ThrowIfRowOutOfRange(toRow);

        for (var row = fromRow; row <= toRow; row++)
        {
            _dirtyRows[row] = true;
        }

        HasDamage = true;
    }

    /// <summary>Records every row as changed, as after a resize or a theme change.</summary>
    public void MarkAllRowsDirty()
    {
        _dirtyRows.AsSpan().Fill(true);
        HasDamage = true;
    }

    /// <summary>Clears all damage. The renderer calls this once it has repainted.</summary>
    public void ClearDamage()
    {
        _dirtyRows.AsSpan().Clear();
        HasDamage = false;
    }

    /// <summary>Reads a row as text.</summary>
    public string GetLineText(int row)
    {
        ThrowIfRowOutOfRange(row);
        return _lines[row].GetText();
    }

    /// <summary>
    /// Reads the whole screen as text, one line per row, with trailing blank space removed.
    /// </summary>
    public string GetText()
    {
        var builder = new StringBuilder();

        for (var row = 0; row < Size.Rows; row++)
        {
            if (row > 0)
            {
                builder.Append('\n');
            }

            builder.Append(_lines[row].GetText());
        }

        return builder.ToString();
    }

    private void ThrowIfRowOutOfRange(int row)
    {
        if ((uint)row >= (uint)Size.Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, $"Row must be between 0 and {Size.Rows - 1}.");
        }
    }

    private void ThrowIfOutOfRange(int column, int row)
    {
        ThrowIfRowOutOfRange(row);

        if ((uint)column >= (uint)Size.Columns)
        {
            throw new ArgumentOutOfRangeException(
                nameof(column), column, $"Column must be between 0 and {Size.Columns - 1}.");
        }
    }

    private void ThrowIfRegionOutOfRange(ScrollRegion region)
    {
        if (region.Bottom >= Size.Rows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region), region, $"Region must lie within {Size.Rows} rows.");
        }
    }
}
