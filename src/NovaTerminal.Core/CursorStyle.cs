namespace NovaTerminal.Core;

/// <summary>The shape used to draw the cursor, as selectable via <c>DECSCUSR</c> (CSI Ps SP q).</summary>
public enum CursorStyle
{
    /// <summary>A filled rectangle covering the whole cell.</summary>
    Block = 0,

    /// <summary>A horizontal bar along the bottom of the cell.</summary>
    Underline = 1,

    /// <summary>A vertical bar at the left edge of the cell.</summary>
    Bar = 2,
}
