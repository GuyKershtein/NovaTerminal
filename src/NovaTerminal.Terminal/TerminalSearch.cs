using System.Text;

namespace NovaTerminal.Terminal;

/// <summary>
/// Where a search match was found: a row of the whole history, and a span of columns within it.
/// </summary>
/// <param name="Line">
/// Index into the terminal's history, counting the oldest retained line as zero and continuing
/// through the live screen.
/// </param>
/// <param name="Column">First column of the match.</param>
/// <param name="Length">Length of the match in columns.</param>
public readonly record struct SearchMatch(int Line, int Column, int Length);

/// <summary>
/// Searches a terminal's scrollback and screen.
/// </summary>
/// <remarks>
/// <para>
/// Search operates on the terminal's <em>text</em>, not on what is currently drawn. A match may lie
/// thousands of lines back in history, and the caller scrolls to it afterwards.
/// </para>
/// <para>
/// Matching is done line by line rather than across line boundaries. A terminal line that wrapped
/// is still a single logical line to the user, so wrapped rows are joined before matching - which
/// means searching for a phrase finds it even when the terminal happened to break it across two
/// rows.
/// </para>
/// </remarks>
public static class TerminalSearch
{
    /// <summary>
    /// Finds every occurrence of <paramref name="query"/> in the terminal's history and screen.
    /// </summary>
    /// <param name="terminal">The terminal to search.</param>
    /// <param name="query">Text to look for. An empty query matches nothing.</param>
    /// <param name="caseSensitive">Whether case must match.</param>
    /// <param name="limit">
    /// Most matches to return. Searching a million-line scrollback for a single space would
    /// otherwise produce millions of results, so the count is bounded.
    /// </param>
    public static IReadOnlyList<SearchMatch> FindAll(
        TerminalState terminal,
        string query,
        bool caseSensitive = false,
        int limit = 1000)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(query);

        if (query.Length == 0 || limit <= 0)
        {
            return [];
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = new List<SearchMatch>();
        var totalLines = GetTotalLineCount(terminal);

        for (var line = 0; line < totalLines && matches.Count < limit; line++)
        {
            var text = GetLineText(terminal, line);

            var index = 0;
            while (matches.Count < limit
                   && (index = text.IndexOf(query, index, comparison)) >= 0)
            {
                matches.Add(new SearchMatch(line, index, query.Length));
                index += query.Length;
            }
        }

        return matches;
    }

    /// <summary>
    /// Returns the match at or after <paramref name="fromLine"/>, wrapping to the start when there
    /// is none. Returns <see langword="null"/> when nothing matches at all.
    /// </summary>
    public static SearchMatch? FindNext(IReadOnlyList<SearchMatch> matches, int fromLine)
    {
        ArgumentNullException.ThrowIfNull(matches);

        if (matches.Count == 0)
        {
            return null;
        }

        foreach (var match in matches)
        {
            if (match.Line >= fromLine)
            {
                return match;
            }
        }

        // Wrapping round is what a user expects from "find next" at the end of a document.
        return matches[0];
    }

    /// <summary>
    /// Returns the match at or before <paramref name="fromLine"/>, wrapping to the end.
    /// </summary>
    public static SearchMatch? FindPrevious(IReadOnlyList<SearchMatch> matches, int fromLine)
    {
        ArgumentNullException.ThrowIfNull(matches);

        if (matches.Count == 0)
        {
            return null;
        }

        for (var index = matches.Count - 1; index >= 0; index--)
        {
            if (matches[index].Line <= fromLine)
            {
                return matches[index];
            }
        }

        return matches[^1];
    }

    /// <summary>
    /// The total number of lines available to search: retained history plus the live screen.
    /// </summary>
    public static int GetTotalLineCount(TerminalState terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        return (terminal.Scrollback?.Count ?? 0) + terminal.Size.Rows;
    }

    /// <summary>
    /// Reads a line by its index in the combined history, where zero is the oldest retained line.
    /// </summary>
    public static string GetLineText(TerminalState terminal, int line)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        var history = terminal.Scrollback?.Count ?? 0;

        return line < history
            ? terminal.Scrollback![line].GetText()
            : terminal.Buffer.GetLineText(line - history);
    }

    /// <summary>
    /// Converts a line index in the combined history into the viewport offset needed to show it.
    /// </summary>
    public static int GetViewportOffsetFor(TerminalState terminal, int line)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        var history = terminal.Scrollback?.Count ?? 0;

        // A line on the live screen needs no scrolling; one in history needs the view moved back
        // far enough to bring it into the visible area.
        if (line >= history)
        {
            return 0;
        }

        return Math.Clamp(history - line, 0, terminal.MaxViewportOffset);
    }
}
