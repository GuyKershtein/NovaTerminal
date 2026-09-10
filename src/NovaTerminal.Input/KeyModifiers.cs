namespace NovaTerminal.Input;

/// <summary>
/// Modifier keys held while another key was pressed.
/// </summary>
/// <remarks>
/// The flag values are not arbitrary. xterm encodes modifiers in escape sequences as a numeric
/// parameter equal to <c>1 + shift(1) + alt(2) + control(4) + super(8)</c> - so
/// <c>Shift+Alt+Right</c> becomes <c>CSI 1;4 C</c>. Matching those weights here means the encoder
/// computes the parameter by adding one to the raw flag value, with no lookup table to get wrong.
/// </remarks>
[Flags]
public enum KeyModifiers
{
    /// <summary>No modifier keys were held.</summary>
    None = 0,

    /// <summary>Shift.</summary>
    Shift = 1,

    /// <summary>Alt, sent to the terminal as an ESC prefix or as a modifier parameter.</summary>
    Alt = 2,

    /// <summary>Control.</summary>
    Control = 4,

    /// <summary>The Windows or Command key.</summary>
    Super = 8,
}
