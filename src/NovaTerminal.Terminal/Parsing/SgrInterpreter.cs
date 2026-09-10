using NovaTerminal.Core;

namespace NovaTerminal.Terminal.Parsing;

/// <summary>
/// Applies Select Graphic Rendition (<c>SGR</c>, <c>CSI ... m</c>) parameters to a pen.
/// </summary>
/// <remarks>
/// <para>
/// SGR is how a terminal is told what subsequent text should look like. It is cumulative: each
/// sequence modifies the current pen rather than replacing it, which is why <c>SGR 1</c> followed by
/// <c>SGR 31</c> produces bold red rather than plain red.
/// </para>
/// <para>
/// Extended colours arrive in two spellings that mean the same thing. <c>38;5;n</c> and
/// <c>38;2;r;g;b</c> use semicolons, so their arguments look like ordinary parameters;
/// <c>38:5:n</c> and <c>38:2::r:g:b</c> use colons, making them sub-parameters of the 38. The colon
/// form is the one ECMA-48 actually intended and it can carry a colour-space identifier that the
/// semicolon form has no room for. Both appear in real output, so both are accepted.
/// </para>
/// <para>
/// Unknown parameters are ignored rather than treated as errors. New SGR codes appear regularly and
/// a terminal that broke on one it did not recognise would be useless.
/// </para>
/// </remarks>
public static class SgrInterpreter
{
    private const int Reset = 0;
    private const int Bold = 1;
    private const int Faint = 2;
    private const int Italic = 3;
    private const int Underline = 4;
    private const int Blink = 5;
    private const int RapidBlink = 6;
    private const int Inverse = 7;
    private const int Invisible = 8;
    private const int Strikethrough = 9;
    private const int DoubleUnderline = 21;
    private const int NormalIntensity = 22;
    private const int NotItalic = 23;
    private const int NotUnderlined = 24;
    private const int NotBlinking = 25;
    private const int NotInverse = 27;
    private const int Reveal = 28;
    private const int NotStrikethrough = 29;

    private const int ForegroundLow = 30;
    private const int ForegroundHigh = 37;
    private const int ExtendedForeground = 38;
    private const int DefaultForeground = 39;
    private const int BackgroundLow = 40;
    private const int BackgroundHigh = 47;
    private const int ExtendedBackground = 48;
    private const int DefaultBackground = 49;
    private const int BrightForegroundLow = 90;
    private const int BrightForegroundHigh = 97;
    private const int BrightBackgroundLow = 100;
    private const int BrightBackgroundHigh = 107;

    private const int ExtendedColorIndexed = 5;
    private const int ExtendedColorRgb = 2;
    private const int BrightColorOffset = 8;
    private const int MaxColorComponent = 255;

    /// <summary>
    /// Returns the pen produced by applying <paramref name="sequence"/> to <paramref name="style"/>.
    /// </summary>
    /// <remarks>
    /// An SGR sequence with no parameters at all means <c>SGR 0</c>: a bare <c>CSI m</c> resets.
    /// </remarks>
    public static CellStyle Apply(CellStyle style, in CsiSequence sequence)
    {
        if (sequence.Count == 0)
        {
            return CellStyle.Default;
        }

        for (var index = 0; index < sequence.Count; index++)
        {
            // Sub-parameters are consumed by the parameter that owns them.
            if (sequence.IsSubParameter(index))
            {
                continue;
            }

            index = ApplyParameter(ref style, in sequence, index);
        }

        return style;
    }

    /// <summary>
    /// Applies one parameter and returns the index of the last parameter it consumed, which is
    /// more than its own when it introduces an extended colour.
    /// </summary>
    private static int ApplyParameter(ref CellStyle style, in CsiSequence sequence, int index)
    {
        var parameter = sequence[index];

        switch (parameter)
        {
            case Reset:
                style = CellStyle.Default;
                return index;

            case Bold:
                style = style.WithAttributes(TextAttributes.Bold);
                return index;
            case Faint:
                style = style.WithAttributes(TextAttributes.Faint);
                return index;
            case Italic:
                style = style.WithAttributes(TextAttributes.Italic);
                return index;
            case Underline:
            case DoubleUnderline:
                // A double underline is still an underline; the engine models one weight of it.
                // The colon form "4:0" switches underlining off, which is handled below.
                style = ApplyUnderline(style, in sequence, index);
                return sequence.GetGroupEnd(index);
            case Blink:
            case RapidBlink:
                style = style.WithAttributes(TextAttributes.Blink);
                return index;
            case Inverse:
                style = style.WithAttributes(TextAttributes.Inverse);
                return index;
            case Invisible:
                style = style.WithAttributes(TextAttributes.Invisible);
                return index;
            case Strikethrough:
                style = style.WithAttributes(TextAttributes.Strikethrough);
                return index;

            case NormalIntensity:
                style = style.WithoutAttributes(TextAttributes.Bold | TextAttributes.Faint);
                return index;
            case NotItalic:
                style = style.WithoutAttributes(TextAttributes.Italic);
                return index;
            case NotUnderlined:
                style = style.WithoutAttributes(TextAttributes.Underline);
                return index;
            case NotBlinking:
                style = style.WithoutAttributes(TextAttributes.Blink);
                return index;
            case NotInverse:
                style = style.WithoutAttributes(TextAttributes.Inverse);
                return index;
            case Reveal:
                style = style.WithoutAttributes(TextAttributes.Invisible);
                return index;
            case NotStrikethrough:
                style = style.WithoutAttributes(TextAttributes.Strikethrough);
                return index;

            case DefaultForeground:
                style = style.WithForeground(TerminalColor.Default);
                return index;
            case DefaultBackground:
                style = style.WithBackground(TerminalColor.Default);
                return index;

            case ExtendedForeground:
                return ApplyExtendedColor(ref style, in sequence, index, foreground: true);
            case ExtendedBackground:
                return ApplyExtendedColor(ref style, in sequence, index, foreground: false);
        }

        if (parameter is >= ForegroundLow and <= ForegroundHigh)
        {
            style = style.WithForeground(TerminalColor.FromIndex((byte)(parameter - ForegroundLow)));
        }
        else if (parameter is >= BackgroundLow and <= BackgroundHigh)
        {
            style = style.WithBackground(TerminalColor.FromIndex((byte)(parameter - BackgroundLow)));
        }
        else if (parameter is >= BrightForegroundLow and <= BrightForegroundHigh)
        {
            style = style.WithForeground(
                TerminalColor.FromIndex((byte)(parameter - BrightForegroundLow + BrightColorOffset)));
        }
        else if (parameter is >= BrightBackgroundLow and <= BrightBackgroundHigh)
        {
            style = style.WithBackground(
                TerminalColor.FromIndex((byte)(parameter - BrightBackgroundLow + BrightColorOffset)));
        }

        // Anything else is a rendition this terminal does not implement, such as overline or a
        // font selection. Ignoring it is the correct behaviour, not a failure.
        return index;
    }

    private static CellStyle ApplyUnderline(CellStyle style, in CsiSequence sequence, int index)
    {
        // "SGR 4:0" turns underlining off; "4:1" through "4:5" select a style this engine renders
        // as a single underline.
        var hasSubParameter = sequence.IsSubParameter(index + 1);
        var requestedStyle = hasSubParameter ? sequence[index + 1] : 1;

        return requestedStyle == 0
            ? style.WithoutAttributes(TextAttributes.Underline)
            : style.WithAttributes(TextAttributes.Underline);
    }

    private static int ApplyExtendedColor(
        ref CellStyle style, in CsiSequence sequence, int index, bool foreground)
    {
        var groupEnd = sequence.GetGroupEnd(index);
        var usesSubParameters = groupEnd > index;

        // Colon form: every argument is a sub-parameter of this one. Semicolon form: the arguments
        // are the parameters that follow it.
        var argumentStart = index + 1;
        var argumentEnd = usesSubParameters ? groupEnd : sequence.Count - 1;
        var available = argumentEnd - argumentStart + 1;

        if (available < 1)
        {
            return index;
        }

        var kind = sequence[argumentStart];

        switch (kind)
        {
            case ExtendedColorIndexed when available >= 2:
                {
                    var color = TerminalColor.FromIndex(ToColorComponent(sequence[argumentStart + 1]));
                    style = SetColor(style, color, foreground);
                    return argumentStart + 1;
                }

            case ExtendedColorRgb when available >= 4:
                {
                    // The colon form may carry a colour-space identifier before the components, as in
                    // "38:2::255:0:0". It is not used, but it does shift where the components start.
                    var componentStart = usesSubParameters && available >= 5
                        ? argumentStart + 2
                        : argumentStart + 1;

                    var color = TerminalColor.FromRgb(
                        ToColorComponent(sequence[componentStart]),
                        ToColorComponent(sequence[componentStart + 1]),
                        ToColorComponent(sequence[componentStart + 2]));

                    style = SetColor(style, color, foreground);
                    return componentStart + 2;
                }

            default:
                // Truncated or unknown colour specification. Consume what belongs to it and leave
                // the pen alone rather than painting an arbitrary colour.
                return usesSubParameters ? groupEnd : argumentStart;
        }
    }

    private static CellStyle SetColor(CellStyle style, TerminalColor color, bool foreground)
        => foreground ? style.WithForeground(color) : style.WithBackground(color);

    /// <summary>
    /// Clamps a parameter into a colour component. Parameters come from untrusted output and are
    /// only bounded by the parser's maximum, so a value of 9999 must become 255 rather than wrap.
    /// </summary>
    private static byte ToColorComponent(int value) => (byte)Math.Clamp(value, 0, MaxColorComponent);
}
