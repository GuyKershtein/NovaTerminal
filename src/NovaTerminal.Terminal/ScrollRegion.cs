using NovaTerminal.Core;

namespace NovaTerminal.Terminal;

/// <summary>
/// The band of rows that scrolling affects, set by <c>DECSTBM</c> (CSI top;bottom r).
/// </summary>
/// <remarks>
/// <para>
/// A scrolling region is how a terminal program keeps part of the screen still while the rest
/// scrolls - a status line pinned to the bottom, or the fixed header of a full-screen editor. Rows
/// outside the region are simply never moved by a line feed.
/// </para>
/// <para>
/// Both bounds are zero-based and inclusive. The VT protocol numbers rows from one; that conversion
/// happens in the parser, so the engine only ever deals in zero-based coordinates.
/// </para>
/// </remarks>
public readonly record struct ScrollRegion
{
    /// <summary>Creates a region spanning the given inclusive row range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A bound is negative, or <paramref name="bottom"/> is above <paramref name="top"/>.
    /// </exception>
    public ScrollRegion(int top, int bottom)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(top);
        ArgumentOutOfRangeException.ThrowIfLessThan(bottom, top);

        Top = top;
        Bottom = bottom;
    }

    /// <summary>The first row of the region, inclusive.</summary>
    public int Top { get; }

    /// <summary>The last row of the region, inclusive.</summary>
    public int Bottom { get; }

    /// <summary>The number of rows in the region.</summary>
    public int Height => Bottom - Top + 1;

    /// <summary>The region covering an entire screen of the given size.</summary>
    public static ScrollRegion FullScreen(TerminalSize size) => new(0, size.Rows - 1);

    /// <summary>Tests whether a row lies inside the region.</summary>
    public bool Contains(int row) => row >= Top && row <= Bottom;

    /// <summary>
    /// Returns this region clipped to a screen of the given size, which is what a resize needs when
    /// the region no longer fits.
    /// </summary>
    public ScrollRegion ClampTo(TerminalSize size)
    {
        var lastRow = size.Rows - 1;
        var top = Math.Min(Top, lastRow);
        var bottom = Math.Min(Bottom, lastRow);
        return new ScrollRegion(top, Math.Max(top, bottom));
    }

    /// <inheritdoc />
    public override string ToString() => $"rows {Top}-{Bottom}";
}
