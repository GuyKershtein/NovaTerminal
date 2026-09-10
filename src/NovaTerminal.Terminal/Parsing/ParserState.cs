namespace NovaTerminal.Terminal.Parsing;

/// <summary>
/// States of the escape-sequence state machine, following Paul Williams' VT500 parser model.
/// </summary>
/// <remarks>
/// The state is what allows a sequence to be split across reads: it persists between calls, so a
/// parser that has consumed <c>ESC [ 3</c> and nothing else is simply sitting in
/// <see cref="CsiParam"/> with one partial parameter, waiting.
/// </remarks>
internal enum ParserState : byte
{
    /// <summary>Ordinary text. Printable bytes are printed and C0 controls executed.</summary>
    Ground,

    /// <summary>An <c>ESC</c> has been seen and the sequence type is not yet known.</summary>
    Escape,

    /// <summary>Collecting intermediate bytes of an escape sequence, as in <c>ESC ( B</c>.</summary>
    EscapeIntermediate,

    /// <summary>A control sequence has begun; the first byte decides what follows.</summary>
    CsiEntry,

    /// <summary>Accumulating the numeric parameters of a control sequence.</summary>
    CsiParam,

    /// <summary>Collecting intermediate bytes of a control sequence, as in <c>CSI Ps SP q</c>.</summary>
    CsiIntermediate,

    /// <summary>
    /// The control sequence is malformed. Bytes are consumed until its final byte, which is then
    /// discarded rather than dispatched. Recovering at the right place is what stops one bad
    /// sequence from corrupting the text that follows it.
    /// </summary>
    CsiIgnore,

    /// <summary>Accumulating the payload of an operating system command, such as a window title.</summary>
    OscString,

    /// <summary>A device control string has begun.</summary>
    DcsEntry,

    /// <summary>Accumulating the parameters of a device control string.</summary>
    DcsParam,

    /// <summary>Collecting intermediate bytes of a device control string.</summary>
    DcsIntermediate,

    /// <summary>
    /// Consuming a device control string's payload. NovaTerminal implements no DCS functions, so
    /// the payload is discarded - but it must still be recognised, or its contents would be
    /// executed as if they were terminal commands.
    /// </summary>
    DcsPassthrough,

    /// <summary>Discarding a malformed device control string.</summary>
    DcsIgnore,

    /// <summary>
    /// Discarding a start-of-string, privacy-message or application-program-command string. These
    /// have no effect here but must be consumed to their terminator.
    /// </summary>
    SosPmApcString,
}
