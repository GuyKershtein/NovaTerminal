namespace NovaTerminal.Terminal;

/// <summary>
/// The lines that have scrolled off the top of the screen.
/// </summary>
/// <remarks>
/// <para>
/// A ring buffer, so that retaining the last <em>n</em> lines costs nothing per line: the oldest
/// line is overwritten in place rather than the whole history being shifted. Adding a line to a
/// full scrollback is O(1), which matters because it happens once per line of every command that
/// produces output.
/// </para>
/// <para>
/// <b>The capacity is a security boundary, not a preference.</b> Terminal output is untrusted and
/// can be infinite - a program printing forever, a corrupted file being <c>cat</c>ed. Without a
/// bound, retaining history means retaining everything, and the terminal eventually takes the
/// machine down with it. The bound is configurable but cannot be removed.
/// </para>
/// <para>
/// Evicted lines are recycled rather than dropped: the line object leaving the ring is handed back
/// to the caller to reuse, so a terminal at steady state allocates nothing while scrolling.
/// </para>
/// </remarks>
public sealed class Scrollback
{
    private readonly TerminalLine?[] _lines;
    private int _start;

    /// <summary>Creates a scrollback retaining at most <paramref name="capacity"/> lines.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is negative.</exception>
    public Scrollback(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        _lines = new TerminalLine?[capacity];
    }

    /// <summary>The most lines this scrollback will ever hold.</summary>
    public int Capacity => _lines.Length;

    /// <summary>How many lines are currently held.</summary>
    public int Count { get; private set; }

    /// <summary>True when no history is being retained at all.</summary>
    public bool IsDisabled => _lines.Length == 0;

    /// <summary>
    /// Adds a line, returning the line it displaced so the caller can reuse it.
    /// </summary>
    /// <returns>
    /// The evicted line when the scrollback was already full, otherwise <see langword="null"/>.
    /// Returning it rather than discarding it is what lets the engine recycle line storage instead
    /// of allocating a replacement for every line of output.
    /// </returns>
    public TerminalLine? Add(TerminalLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (IsDisabled)
        {
            return line;
        }

        if (Count < _lines.Length)
        {
            _lines[(_start + Count) % _lines.Length] = line;
            Count++;
            return null;
        }

        var evicted = _lines[_start];
        _lines[_start] = line;
        _start = (_start + 1) % _lines.Length;
        return evicted;
    }

    /// <summary>
    /// Returns a line by age, where zero is the oldest line retained and
    /// <see cref="Count"/> minus one is the most recent.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no line at that index.</exception>
    public TerminalLine this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, $"Scrollback holds {Count} lines.");
            }

            return _lines[(_start + index) % _lines.Length]!;
        }
    }

    /// <summary>Discards all retained history, as <c>CSI 3 J</c> does.</summary>
    public void Clear()
    {
        Array.Clear(_lines);
        _start = 0;
        Count = 0;
    }
}
