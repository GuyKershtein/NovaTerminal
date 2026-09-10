using System.Text;

namespace NovaTerminal.Terminal;

/// <summary>
/// The scrollback view: what the user is currently looking at, which is not always the live screen.
/// </summary>
public sealed partial class TerminalState
{
    private int _viewportOffset;

    /// <summary>
    /// How many lines back from the live screen the view is scrolled. Zero means the bottom, where
    /// new output appears.
    /// </summary>
    public int ViewportOffset => _viewportOffset;

    /// <summary>True when the user is looking at history rather than at the live screen.</summary>
    public bool IsScrolledBack => _viewportOffset > 0;

    /// <summary>The retained history of the primary screen.</summary>
    public Scrollback? Scrollback => _primaryBuffer.Scrollback;

    /// <summary>How many lines of history are available to scroll through.</summary>
    /// <remarks>
    /// The alternate screen has no history. A full-screen program's intermediate states are not
    /// something a user would ever want to scroll through, and offering it would only produce
    /// confusing fragments of half-drawn screens.
    /// </remarks>
    public int MaxViewportOffset =>
        IsAlternateScreenActive ? 0 : Scrollback?.Count ?? 0;

    /// <summary>Configures how much history the primary screen retains.</summary>
    /// <remarks>
    /// The capacity is bounded by configuration, and that bound is not negotiable: terminal output
    /// can be infinite, so unbounded retention means the terminal eventually exhausts memory.
    /// </remarks>
    public void ConfigureScrollback(int capacity)
        => _primaryBuffer.Scrollback = capacity > 0 ? new Scrollback(capacity) : null;

    /// <summary>
    /// Returns the cells of a row of the current view, counting from the top of the visible area.
    /// </summary>
    /// <remarks>
    /// When the view is scrolled back, the top rows come from history and the rest from the live
    /// screen. Presenting them as one continuous sequence keeps the renderer free of any notion of
    /// scrollback at all: it draws rows zero to <c>Rows-1</c> and never asks where they came from.
    /// </remarks>
    public ReadOnlySpan<TerminalCell> GetViewRow(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);

        if (_viewportOffset == 0)
        {
            return Buffer.GetRow(row);
        }

        // Rows above this index are history; the remainder are the top of the live screen.
        var historyRow = Scrollback!.Count - _viewportOffset + row;

        if (historyRow < Scrollback.Count)
        {
            return Scrollback[historyRow].Cells;
        }

        return Buffer.GetRow(historyRow - Scrollback.Count);
    }

    /// <summary>Reads a row of the current view as text.</summary>
    public string GetViewRowText(int row)
    {
        if (_viewportOffset == 0)
        {
            return Buffer.GetLineText(row);
        }

        var historyRow = Scrollback!.Count - _viewportOffset + row;

        return historyRow < Scrollback.Count
            ? Scrollback[historyRow].GetText()
            : Buffer.GetLineText(historyRow - Scrollback.Count);
    }

    /// <summary>
    /// Scrolls the view back through history. Returns whether anything moved.
    /// </summary>
    public bool ScrollViewBack(int lines)
    {
        var target = Math.Clamp(_viewportOffset + Math.Max(0, lines), 0, MaxViewportOffset);
        return SetViewportOffset(target);
    }

    /// <summary>Scrolls the view toward the live screen. Returns whether anything moved.</summary>
    public bool ScrollViewForward(int lines)
    {
        var target = Math.Clamp(_viewportOffset - Math.Max(0, lines), 0, MaxViewportOffset);
        return SetViewportOffset(target);
    }

    /// <summary>
    /// Returns the view to the live screen, which is what typing should do.
    /// </summary>
    /// <remarks>
    /// A user who is reading history and then types expects to be taken back to the prompt. Leaving
    /// them in history while their keystrokes go to an invisible shell is disorienting, so any
    /// input snaps the view back.
    /// </remarks>
    public bool ScrollViewToBottom() => SetViewportOffset(0);

    /// <summary>Discards retained history, as <c>CSI 3 J</c> does.</summary>
    public void ClearScrollback()
    {
        Scrollback?.Clear();
        SetViewportOffset(0);
    }

    private bool SetViewportOffset(int offset)
    {
        if (offset == _viewportOffset)
        {
            return false;
        }

        _viewportOffset = offset;

        // Everything on screen moved, so everything needs repainting.
        Buffer.MarkAllRowsDirty();
        return true;
    }
}
