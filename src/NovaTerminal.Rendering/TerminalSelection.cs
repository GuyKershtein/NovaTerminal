using System.Text;
using NovaTerminal.Terminal;

namespace NovaTerminal.Rendering;

/// <summary>A cell coordinate within the current view.</summary>
/// <param name="Column">Zero-based column.</param>
/// <param name="Row">Zero-based row of the visible area.</param>
public readonly record struct CellPosition(int Column, int Row) : IComparable<CellPosition>
{
    /// <inheritdoc />
    public int CompareTo(CellPosition other)
    {
        var byRow = Row.CompareTo(other.Row);
        return byRow != 0 ? byRow : Column.CompareTo(other.Column);
    }

    /// <summary>Whether <paramref name="left"/> comes before <paramref name="right"/> in reading order.</summary>
    public static bool operator <(CellPosition left, CellPosition right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> comes after <paramref name="right"/> in reading order.</summary>
    public static bool operator >(CellPosition left, CellPosition right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> is at or before <paramref name="right"/>.</summary>
    public static bool operator <=(CellPosition left, CellPosition right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> is at or after <paramref name="right"/>.</summary>
    public static bool operator >=(CellPosition left, CellPosition right) => left.CompareTo(right) >= 0;
}

/// <summary>How a selection covers the cells between its two ends.</summary>
public enum SelectionMode
{
    /// <summary>Everything from the anchor to the focus, wrapping across rows like selected prose.</summary>
    Linear = 0,

    /// <summary>A rectangle: the same column range on each row, for selecting a column of output.</summary>
    Block = 1,
}

/// <summary>
/// A range of selected cells, and the rules for turning it back into text.
/// </summary>
/// <remarks>
/// <para>
/// A terminal selection is a range of <em>cells</em>, not of pixels or of characters in a string.
/// That distinction is what lets copied text come out as the text that was displayed rather than as
/// a screenshot of it.
/// </para>
/// <para>
/// The anchor is where the drag started and the focus is where it is now; either may be the earlier
/// of the two, which is why they are ordered on demand rather than on creation.
/// </para>
/// </remarks>
public readonly record struct TerminalSelection(
    CellPosition Anchor,
    CellPosition Focus,
    SelectionMode Mode = SelectionMode.Linear)
{
    /// <summary>The earlier of the two ends.</summary>
    public CellPosition Start => Anchor <= Focus ? Anchor : Focus;

    /// <summary>The later of the two ends.</summary>
    public CellPosition End => Anchor <= Focus ? Focus : Anchor;

    /// <summary>True when the selection covers no cells at all.</summary>
    public bool IsEmpty => Anchor == Focus;

    /// <summary>Tests whether a cell lies inside the selection.</summary>
    public bool Contains(int column, int row)
    {
        if (IsEmpty)
        {
            return false;
        }

        var start = Start;
        var end = End;

        if (Mode == SelectionMode.Block)
        {
            var left = Math.Min(Anchor.Column, Focus.Column);
            var right = Math.Max(Anchor.Column, Focus.Column);
            return row >= start.Row && row <= end.Row && column >= left && column < right;
        }

        if (row < start.Row || row > end.Row)
        {
            return false;
        }

        var from = row == start.Row ? start.Column : 0;
        var to = row == end.Row ? end.Column : int.MaxValue;

        return column >= from && column < to;
    }

    /// <summary>
    /// Extracts the selected text from a terminal's current view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two details make copied text usable rather than merely accurate. Trailing blanks are dropped
    /// from each row, because the cells to the right of a line's text were never really part of it.
    /// And a row that wrapped is joined to the next without a line break, because the terminal
    /// broke that line for display and the user selected one logical line, not two.
    /// </para>
    /// </remarks>
    public string GetText(TerminalState terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        if (IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var start = Start;
        var end = End;
        var lastRow = Math.Min(end.Row, terminal.Size.Rows - 1);

        for (var row = Math.Max(0, start.Row); row <= lastRow; row++)
        {
            var cells = terminal.GetViewRow(row);
            var (from, to) = GetRowRange(row, start, end, cells.Length);

            AppendRow(builder, cells, from, to);

            if (row == lastRow)
            {
                break;
            }

            // A wrapped row continues on the next one, so joining them restores the line the user
            // actually sees.
            var wrapped = terminal.ViewportOffset == 0 && terminal.Buffer.GetLine(row).IsWrapped;

            if (!wrapped)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private (int From, int To) GetRowRange(int row, CellPosition start, CellPosition end, int columns)
    {
        if (Mode == SelectionMode.Block)
        {
            return (
                Math.Clamp(Math.Min(Anchor.Column, Focus.Column), 0, columns),
                Math.Clamp(Math.Max(Anchor.Column, Focus.Column), 0, columns));
        }

        return (
            Math.Clamp(row == start.Row ? start.Column : 0, 0, columns),
            Math.Clamp(row == end.Row ? end.Column : columns, 0, columns));
    }

    private static void AppendRow(StringBuilder builder, ReadOnlySpan<TerminalCell> cells, int from, int to)
    {
        // Trailing blanks were never part of the line; copying them would paste invisible padding.
        var end = to;
        while (end > from && cells[end - 1].IsEmpty)
        {
            end--;
        }

        for (var column = from; column < end; column++)
        {
            if (cells[column].IsWideTrailing)
            {
                continue;
            }

            builder.Append(cells[column].DisplayCharacter);
        }
    }
}
