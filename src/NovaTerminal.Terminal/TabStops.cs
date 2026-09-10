namespace NovaTerminal.Terminal;

/// <summary>
/// The columns a horizontal tab advances to.
/// </summary>
/// <remarks>
/// <para>
/// Tab stops are terminal state, not a property of the text. A tab character does not mean "insert
/// eight spaces" - it means "advance to the next stop", and a program can move those stops with
/// <c>HTS</c> and <c>TBC</c>. Tabular output from tools that set their own stops only lines up if
/// the emulator honours them.
/// </para>
/// <para>
/// Stops are stored as a flag per column. A screen has at most a few thousand columns, so the array
/// is tiny and the lookup is a single indexed read.
/// </para>
/// </remarks>
public sealed class TabStops
{
    /// <summary>The conventional distance between default tab stops.</summary>
    public const int DefaultInterval = 8;

    private bool[] _stops;

    /// <summary>Creates default stops every <paramref name="interval"/> columns.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    public TabStops(int columns, int interval = DefaultInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, 1);

        Interval = interval;
        _stops = new bool[columns];
        Reset();
    }

    /// <summary>The distance between stops used when they are reset.</summary>
    public int Interval { get; }

    /// <summary>The number of columns covered.</summary>
    public int Columns => _stops.Length;

    /// <summary>Tests whether a column is a tab stop.</summary>
    public bool IsStop(int column) => (uint)column < (uint)_stops.Length && _stops[column];

    /// <summary>Sets a stop at a column, as <c>HTS</c> does.</summary>
    public void Set(int column)
    {
        if ((uint)column < (uint)_stops.Length)
        {
            _stops[column] = true;
        }
    }

    /// <summary>Clears the stop at a column, as <c>TBC 0</c> does.</summary>
    public void Clear(int column)
    {
        if ((uint)column < (uint)_stops.Length)
        {
            _stops[column] = false;
        }
    }

    /// <summary>Clears every stop, as <c>TBC 3</c> does.</summary>
    public void ClearAll() => _stops.AsSpan().Clear();

    /// <summary>Restores stops at every <see cref="Interval"/> columns.</summary>
    public void Reset()
    {
        _stops.AsSpan().Clear();

        for (var column = Interval; column < _stops.Length; column += Interval)
        {
            _stops[column] = true;
        }
    }

    /// <summary>
    /// Returns the next stop strictly after <paramref name="column"/>, or the last column when
    /// there is none.
    /// </summary>
    /// <remarks>
    /// Falling back to the last column - rather than wrapping or standing still - is what real
    /// terminals do, and it guarantees a tab always makes progress so a line of tabs cannot loop.
    /// </remarks>
    public int Next(int column)
    {
        var lastColumn = _stops.Length - 1;

        for (var candidate = column + 1; candidate <= lastColumn; candidate++)
        {
            if (_stops[candidate])
            {
                return candidate;
            }
        }

        return lastColumn;
    }

    /// <summary>
    /// Returns the previous stop strictly before <paramref name="column"/>, or column zero when
    /// there is none. This is what a back tab (<c>CBT</c>) uses.
    /// </summary>
    public int Previous(int column)
    {
        var start = Math.Min(column - 1, _stops.Length - 1);

        for (var candidate = start; candidate > 0; candidate--)
        {
            if (_stops[candidate])
            {
                return candidate;
            }
        }

        return 0;
    }

    /// <summary>
    /// Changes the number of columns covered, keeping existing stops and giving any newly exposed
    /// columns default stops.
    /// </summary>
    public void Resize(int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        if (columns == _stops.Length)
        {
            return;
        }

        var resized = new bool[columns];
        var copied = Math.Min(columns, _stops.Length);
        _stops.AsSpan(0, copied).CopyTo(resized);

        // Columns the screen did not previously have get the default stops, so widening a window
        // does not leave the new area without any.
        var firstNewStop = ((copied + Interval - 1) / Interval) * Interval;
        for (var column = firstNewStop; column < columns; column += Interval)
        {
            resized[column] = true;
        }

        _stops = resized;
    }
}
