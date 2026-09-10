namespace NovaTerminal.Terminal;

/// <summary>
/// How much of a line or screen an erase affects, as selected by the parameter of <c>EL</c> and
/// <c>ED</c>.
/// </summary>
/// <remarks>
/// The numeric values match the escape sequence parameters (<c>CSI 0 K</c>, <c>CSI 1 K</c>,
/// <c>CSI 2 K</c>), so the parser can cast a validated parameter straight to this enum instead of
/// carrying a translation table.
/// </remarks>
public enum EraseExtent
{
    /// <summary>From the cursor to the end, inclusive of the cursor cell. The default.</summary>
    ToEnd = 0,

    /// <summary>From the beginning to the cursor, inclusive of the cursor cell.</summary>
    ToStart = 1,

    /// <summary>Everything, leaving the cursor where it is.</summary>
    All = 2,
}
