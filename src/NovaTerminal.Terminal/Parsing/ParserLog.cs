using Microsoft.Extensions.Logging;
using NovaTerminal.Core.Diagnostics;

namespace NovaTerminal.Terminal.Parsing;

/// <summary>
/// Source-generated log methods for the parser and interpreter.
/// </summary>
/// <remarks>
/// Everything here is logged at debug level and none of it is per-character. Terminal output is
/// untrusted and can be produced faster than any log sink can absorb it, so the engine never logs
/// on the ordinary path - only when it meets something it does not implement or cannot parse, and
/// even then the interpreter reports each distinct sequence only once.
/// </remarks>
internal static partial class ParserLog
{
    [LoggerMessage(
        EventId = LogEvents.MalformedSequence,
        EventName = "ParametersTruncated",
        Level = LogLevel.Debug,
        Message = "Control sequence ending in '{Final}' carried more than {Limit} parameters; the excess was dropped.")]
    public static partial void ParametersTruncated(this ILogger logger, char final, int limit);

    [LoggerMessage(
        EventId = LogEvents.MalformedSequence,
        EventName = "StringTruncated",
        Level = LogLevel.Debug,
        Message = "A string payload exceeded {Limit} bytes and was truncated.")]
    public static partial void StringTruncated(this ILogger logger, int limit);

    [LoggerMessage(
        EventId = LogEvents.UnsupportedSequence,
        EventName = nameof(LogEvents.UnsupportedSequence),
        Level = LogLevel.Debug,
        Message = "Ignoring unsupported sequence {Sequence}.")]
    public static partial void UnsupportedSequence(this ILogger logger, string sequence);

    [LoggerMessage(
        EventId = LogEvents.MalformedSequence,
        EventName = "InvalidParameters",
        Level = LogLevel.Debug,
        Message = "Ignoring sequence {Sequence} with out-of-range parameters.")]
    public static partial void InvalidParameters(this ILogger logger, string sequence);
}
