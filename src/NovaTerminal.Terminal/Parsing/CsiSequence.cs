namespace NovaTerminal.Terminal.Parsing;

/// <summary>
/// A fully parsed control sequence, handed to the handler for the instant it is being dispatched.
/// </summary>
/// <remarks>
/// <para>
/// A control sequence has the shape
/// <c>CSI [private marker] [parameters] [intermediates] final</c>. For example
/// <c>CSI ? 25 h</c> has private marker <c>?</c>, one parameter and final byte <c>h</c>.
/// </para>
/// <para>
/// This is a <see langword="ref"/> <see langword="struct"/> deliberately. It borrows the parser's
/// parameter buffers rather than copying them, so dispatching a sequence allocates nothing even
/// under a flood of output. The compiler then guarantees no handler can stash it somewhere it would
/// outlive those buffers.
/// </para>
/// </remarks>
public readonly ref struct CsiSequence
{
    /// <summary>Value meaning "no private marker" or "no intermediate byte".</summary>
    public const char None = '\0';

    private readonly ReadOnlySpan<int> _parameters;
    private readonly ReadOnlySpan<bool> _subParameterFlags;

    /// <summary>Creates a sequence. Public so that callers can synthesise one for testing.</summary>
    /// <param name="parameters">Numeric parameters, in order.</param>
    /// <param name="subParameterFlags">
    /// For each parameter, whether it was separated from the previous one by a colon rather than a
    /// semicolon - that is, whether it is a sub-parameter of it.
    /// </param>
    /// <param name="privateMarker">One of <c>? &lt; = &gt;</c>, or <see cref="None"/>.</param>
    /// <param name="intermediate">An intermediate byte such as the space in DECSCUSR, or <see cref="None"/>.</param>
    /// <param name="final">The final byte that identifies the function.</param>
    /// <exception cref="ArgumentException">The two spans have different lengths.</exception>
    public CsiSequence(
        ReadOnlySpan<int> parameters,
        ReadOnlySpan<bool> subParameterFlags,
        char privateMarker,
        char intermediate,
        char final)
    {
        if (parameters.Length != subParameterFlags.Length)
        {
            throw new ArgumentException(
                "Each parameter needs a matching sub-parameter flag.", nameof(subParameterFlags));
        }

        _parameters = parameters;
        _subParameterFlags = subParameterFlags;
        PrivateMarker = privateMarker;
        Intermediate = intermediate;
        Final = final;
    }

    /// <summary>The private-use marker, or <see cref="None"/> when the sequence is standard.</summary>
    public char PrivateMarker { get; }

    /// <summary>The intermediate byte, or <see cref="None"/>.</summary>
    public char Intermediate { get; }

    /// <summary>The final byte, which selects the control function.</summary>
    public char Final { get; }

    /// <summary>How many parameters the sequence carried.</summary>
    public int Count => _parameters.Length;

    /// <summary>Reads a parameter by position.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no parameter at that position.</exception>
    public int this[int index] => _parameters[index];

    /// <summary>
    /// Reads a parameter, substituting <paramref name="defaultValue"/> when it was omitted.
    /// </summary>
    /// <remarks>
    /// Omitted parameters are the norm: <c>CSI H</c> and <c>CSI 1;1H</c> mean the same thing. Note
    /// that a parameter explicitly written as zero is <em>not</em> omitted, and the convention that
    /// zero also means one is applied by the engine rather than here.
    /// </remarks>
    public int GetOrDefault(int index, int defaultValue)
        => (uint)index < (uint)_parameters.Length ? _parameters[index] : defaultValue;

    /// <summary>
    /// Whether the parameter at <paramref name="index"/> was introduced by a colon, making it a
    /// sub-parameter of the one before it.
    /// </summary>
    /// <remarks>
    /// This is what distinguishes <c>SGR 38:2::255:0:0</c> - one parameter with five
    /// sub-parameters - from <c>SGR 38;2;255;0;0</c>, which is five separate parameters. Both mean
    /// the same colour, and both appear in the wild.
    /// </remarks>
    public bool IsSubParameter(int index)
        => (uint)index < (uint)_subParameterFlags.Length && _subParameterFlags[index];

    /// <summary>Returns the index of the last sub-parameter belonging to the group at <paramref name="index"/>.</summary>
    public int GetGroupEnd(int index)
    {
        var end = index;
        while (end + 1 < _parameters.Length && _subParameterFlags[end + 1])
        {
            end++;
        }

        return end;
    }

    /// <summary>Renders the sequence in a form suitable for a log message.</summary>
    public override string ToString()
    {
        var marker = PrivateMarker == None ? string.Empty : PrivateMarker.ToString();
        var intermediate = Intermediate == None ? string.Empty : Intermediate.ToString();
        var parameters = new string[_parameters.Length];

        for (var index = 0; index < _parameters.Length; index++)
        {
            var separator = index == 0 ? string.Empty : _subParameterFlags[index] ? ":" : ";";
            parameters[index] = separator + _parameters[index].ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return $"CSI {marker}{string.Concat(parameters)}{intermediate}{Final}";
    }
}
