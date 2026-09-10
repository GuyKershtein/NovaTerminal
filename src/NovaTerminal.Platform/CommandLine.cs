using System.Text;

namespace NovaTerminal.Platform;

/// <summary>
/// Builds a Windows command line from a program and its arguments.
/// </summary>
/// <remarks>
/// <para>
/// Windows passes a command line to a process as one string and leaves the process to split it,
/// which is the opposite of the Unix convention. Getting the quoting wrong is not a cosmetic
/// problem: an argument containing a space silently becomes two arguments, and one ending in a
/// backslash can escape the quote that was meant to contain it.
/// </para>
/// <para>
/// The rules implemented here are the ones the C runtime uses to parse <c>argv</c>, which is what
/// almost every Windows program follows.
/// </para>
/// </remarks>
public static class CommandLine
{
    /// <summary>Builds a command line for a program and its arguments.</summary>
    public static string Build(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        var builder = new StringBuilder();
        AppendArgument(builder, executable);

        foreach (var argument in arguments)
        {
            builder.Append(' ');
            AppendArgument(builder, argument);
        }

        return builder.ToString();
    }

    /// <summary>Appends one argument, quoting and escaping it only when necessary.</summary>
    public static void AppendArgument(StringBuilder builder, string argument)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(argument);

        if (argument.Length > 0 && !ContainsAny(argument, ' ', '\t', '"'))
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');

        for (var index = 0; index < argument.Length; index++)
        {
            var backslashes = 0;

            while (index < argument.Length && argument[index] == '\\')
            {
                index++;
                backslashes++;
            }

            if (index == argument.Length)
            {
                // Backslashes immediately before the closing quote must be doubled, or the last one
                // would escape the quote and swallow the rest of the command line.
                builder.Append('\\', backslashes * 2);
                break;
            }

            if (argument[index] == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(argument[index]);
            }
        }

        builder.Append('"');
    }

    private static bool ContainsAny(string value, params char[] characters)
        => value.AsSpan().IndexOfAny(characters) >= 0;
}
