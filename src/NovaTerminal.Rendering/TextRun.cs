using NovaTerminal.Core;

namespace NovaTerminal.Rendering;

/// <summary>
/// A maximal span of adjacent cells on one row that share a style, and can therefore be drawn as a
/// single piece of text.
/// </summary>
/// <remarks>
/// Coalescing is what makes drawing affordable. A row of eighty plain characters is one run and one
/// text-drawing call rather than eighty; only where the colour or attributes actually change does a
/// new run begin. Text shaping and glyph lookup dominate the cost of drawing, so the difference is
/// not marginal.
/// </remarks>
/// <param name="Column">Column the run starts at.</param>
/// <param name="Length">Number of cells the run covers, including wide-character placeholders.</param>
/// <param name="Style">The style shared by every cell in the run.</param>
/// <param name="IsBlank">
/// True when no cell in the run holds a character. Such a run still needs its background painted,
/// but no text drawn - and when its background is the default, it needs nothing at all.
/// </param>
public readonly record struct TextRun(int Column, int Length, CellStyle Style, bool IsBlank);
