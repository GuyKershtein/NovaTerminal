using System.Text;
using NovaTerminal.Terminal;

namespace NovaTerminal.Rendering;

/// <summary>
/// Turns a row of cells into the runs a renderer should draw.
/// </summary>
/// <remarks>
/// <para>
/// This is the piece of the rendering pipeline most worth having outside the GUI. Coalescing is
/// fiddly - a run ends on any style change, wide-character placeholders belong to the run that
/// started them, and a blank run may be skippable - and every one of those rules is testable here
/// without a window.
/// </para>
/// <para>
/// An instance reuses its internal buffers across rows and frames, so building the runs for a
/// screen allocates nothing after the first frame. A renderer runs sixty times a second; anything
/// that allocates per row would be visible as garbage collection pauses.
/// </para>
/// <para>
/// Not thread-safe: a builder belongs to one renderer.
/// </para>
/// </remarks>
public sealed class RowRunBuilder
{
    private readonly List<TextRun> _runs = [];
    private readonly StringBuilder _text = new();

    /// <summary>
    /// Splits <paramref name="row"/> into runs of identical style.
    /// </summary>
    /// <returns>
    /// The runs, in column order. The list is reused by the next call, so a caller that needs to
    /// keep them must copy them.
    /// </returns>
    public IReadOnlyList<TextRun> Build(ReadOnlySpan<TerminalCell> row)
    {
        _runs.Clear();

        if (row.IsEmpty)
        {
            return _runs;
        }

        var runStart = 0;
        var runStyle = row[0].Style;
        var runIsBlank = row[0].IsEmpty;

        for (var column = 1; column < row.Length; column++)
        {
            var cell = row[column];

            if (cell.Style == runStyle)
            {
                runIsBlank &= cell.IsEmpty;
                continue;
            }

            _runs.Add(new TextRun(runStart, column - runStart, runStyle, runIsBlank));
            runStart = column;
            runStyle = cell.Style;
            runIsBlank = cell.IsEmpty;
        }

        _runs.Add(new TextRun(runStart, row.Length - runStart, runStyle, runIsBlank));
        return _runs;
    }

    /// <summary>
    /// Returns the text of a run, ready to be drawn.
    /// </summary>
    /// <remarks>
    /// The placeholder half of a double-width character contributes nothing: its glyph was already
    /// drawn by the leading cell and spills over it. Emitting anything for it would draw a stray
    /// character; emitting nothing keeps the run's text aligned with the columns it covers, because
    /// the wide glyph is itself two columns wide.
    /// </remarks>
    public string GetRunText(ReadOnlySpan<TerminalCell> row, in TextRun run)
    {
        _text.Clear();

        var end = Math.Min(run.Column + run.Length, row.Length);

        for (var column = run.Column; column < end; column++)
        {
            var cell = row[column];

            if (cell.IsWideTrailing)
            {
                continue;
            }

            _text.Append(cell.DisplayCharacter);
        }

        return _text.ToString();
    }
}
