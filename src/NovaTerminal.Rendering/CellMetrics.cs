using NovaTerminal.Core;

namespace NovaTerminal.Rendering;

/// <summary>
/// The pixel geometry of a single character cell, and the arithmetic that converts between the
/// pixel world of the GUI and the row/column world of the terminal.
/// </summary>
/// <remarks>
/// <para>
/// A terminal renders on a fixed grid: every cell has the same advance width and the same height,
/// measured once from the chosen monospaced font. That uniformity is what makes the renderer fast -
/// the position of any cell is a multiplication, never a text-measurement call.
/// </para>
/// <para>
/// This type is the only place where pixels and cells meet. Above it the GUI works in
/// device-independent pixels; below it everything works in cells. Confining the conversion to one
/// tested type is what keeps resize handling free of off-by-one errors.
/// </para>
/// </remarks>
public readonly record struct CellMetrics
{
    /// <summary>Creates cell geometry, rejecting values that could not describe a real font.</summary>
    /// <param name="width">Advance width of one cell, in device-independent pixels.</param>
    /// <param name="height">Height of one cell (the line height), in device-independent pixels.</param>
    /// <param name="baseline">
    /// Distance from the top of the cell to the text baseline, in device-independent pixels.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not a positive finite number.</exception>
    public CellMetrics(double width, double height, double baseline)
    {
        ThrowIfNotPositiveFinite(width);
        ThrowIfNotPositiveFinite(height);

        if (!double.IsFinite(baseline) || baseline < 0 || baseline > height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseline), baseline, "Baseline must lie between the top and bottom of the cell.");
        }

        Width = width;
        Height = height;
        Baseline = baseline;
    }

    /// <summary>Advance width of one cell, in device-independent pixels.</summary>
    public double Width { get; }

    /// <summary>Height of one cell, in device-independent pixels.</summary>
    public double Height { get; }

    /// <summary>Distance from the top of a cell to the text baseline.</summary>
    public double Baseline { get; }

    /// <summary>
    /// Computes how many whole cells fit in a viewport. Partial cells are discarded rather than
    /// rounded up, because a partially visible row would make the shell believe it has space it
    /// cannot actually paint into.
    /// </summary>
    /// <remarks>
    /// The result is clamped to a legal <see cref="TerminalSize"/>: a viewport can legitimately be
    /// zero-sized while a window is being created or minimised, and the terminal must survive that
    /// rather than throw during layout.
    /// </remarks>
    public TerminalSize MeasureViewport(double viewportWidth, double viewportHeight)
    {
        var columns = double.IsFinite(viewportWidth) ? (int)(viewportWidth / Width) : 0;
        var rows = double.IsFinite(viewportHeight) ? (int)(viewportHeight / Height) : 0;
        return TerminalSize.Clamp(columns, rows);
    }

    /// <summary>Returns the top-left pixel offset of the given cell.</summary>
    public (double X, double Y) GetCellOrigin(int column, int row) => (column * Width, row * Height);

    /// <summary>
    /// Returns the cell containing the given pixel position, clamped into <paramref name="size"/>.
    /// Used to turn a mouse position into a selection anchor.
    /// </summary>
    public (int Column, int Row) HitTest(double x, double y, TerminalSize size)
    {
        var column = (int)Math.Floor(x / Width);
        var row = (int)Math.Floor(y / Height);
        return (
            Math.Clamp(column, 0, size.Columns - 1),
            Math.Clamp(row, 0, size.Rows - 1));
    }

    private static void ThrowIfNotPositiveFinite(
        double value,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Cell dimensions must be positive and finite.");
        }
    }
}
