using System.Text;
using NovaTerminal.Input;

namespace NovaTerminal.Integration.Tests;

/// <summary>
/// The xterm key table, verified as data.
/// </summary>
/// <remarks>
/// Every one of these assertions is a promise made by advertising <c>TERM=xterm-256color</c>. A
/// program will send an arrow key and expect exactly these bytes back; getting one wrong shows up
/// as a shell where history does not work, or where Backspace deletes forwards.
/// </remarks>
public sealed class KeyEncoderTests
{
    private const string Esc = "\u001b";

    [Theory]
    [InlineData(TerminalKey.Up, Esc + "[A")]
    [InlineData(TerminalKey.Down, Esc + "[B")]
    [InlineData(TerminalKey.Right, Esc + "[C")]
    [InlineData(TerminalKey.Left, Esc + "[D")]
    [InlineData(TerminalKey.Home, Esc + "[H")]
    [InlineData(TerminalKey.End, Esc + "[F")]
    public void CursorKeys_UseTheCsiFormByDefault(TerminalKey key, string expected)
    {
        Assert.Equal(expected, Encode(key, KeyModifiers.None, TerminalInputModes.Default));
    }

    [Theory]
    [InlineData(TerminalKey.Up, Esc + "OA")]
    [InlineData(TerminalKey.Down, Esc + "OB")]
    [InlineData(TerminalKey.Right, Esc + "OC")]
    [InlineData(TerminalKey.Left, Esc + "OD")]
    public void CursorKeys_SwitchFormUnderApplicationCursorMode(TerminalKey key, string expected)
    {
        // Full-screen programs turn this on so they can tell an arrow key from a user typing the
        // same characters. A terminal that ignored the mode would break every one of them.
        var modes = new TerminalInputModes(ApplicationCursorKeys: true);

        Assert.Equal(expected, Encode(key, KeyModifiers.None, modes));
    }

    [Theory]
    [InlineData(KeyModifiers.Shift, 2)]
    [InlineData(KeyModifiers.Alt, 3)]
    [InlineData(KeyModifiers.Shift | KeyModifiers.Alt, 4)]
    [InlineData(KeyModifiers.Control, 5)]
    [InlineData(KeyModifiers.Shift | KeyModifiers.Control, 6)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Control, 7)]
    [InlineData(KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Control, 8)]
    public void ModifiedCursorKeys_EncodeTheModifierAsOnePlusItsWeight(KeyModifiers modifiers, int expected)
    {
        // The flag values were chosen so this parameter is the flags plus one, with no lookup table
        // that could disagree with the standard.
        Assert.Equal($"{Esc}[1;{expected}C", Encode(TerminalKey.Right, modifiers, TerminalInputModes.Default));
    }

    [Fact]
    public void ModifiedCursorKeys_UseTheCsiFormEvenInApplicationMode()
    {
        // The application form has nowhere to put a modifier parameter, so a modified key falls
        // back to the CSI form regardless of the mode.
        var modes = new TerminalInputModes(ApplicationCursorKeys: true);

        Assert.Equal($"{Esc}[1;5A", Encode(TerminalKey.Up, KeyModifiers.Control, modes));
    }

    [Theory]
    [InlineData(TerminalKey.Insert, Esc + "[2~")]
    [InlineData(TerminalKey.Delete, Esc + "[3~")]
    [InlineData(TerminalKey.PageUp, Esc + "[5~")]
    [InlineData(TerminalKey.PageDown, Esc + "[6~")]
    public void NavigationKeys_UseTheTildeForm(TerminalKey key, string expected)
    {
        Assert.Equal(expected, Encode(key, KeyModifiers.None, TerminalInputModes.Default));
    }

    [Theory]
    [InlineData(TerminalKey.F1, Esc + "OP")]
    [InlineData(TerminalKey.F4, Esc + "OS")]
    [InlineData(TerminalKey.F5, Esc + "[15~")]
    [InlineData(TerminalKey.F12, Esc + "[24~")]
    public void FunctionKeys_FollowTheirTwoDifferentConventions(TerminalKey key, string expected)
    {
        // F1 to F4 come from the VT100 keypad and use a different form from F5 onwards. The gap in
        // the numbering of the later keys is historical, not a mistake.
        Assert.Equal(expected, Encode(key, KeyModifiers.None, TerminalInputModes.Default));
    }

    [Fact]
    public void Enter_SendsACarriageReturnNotALineFeed()
    {
        // A terminal sends CR for Enter; the shell is what turns that into a new line. Sending LF
        // instead makes some shells appear to ignore the key.
        Assert.Equal("\r", Encode(TerminalKey.Enter, KeyModifiers.None, TerminalInputModes.Default));
    }

    [Fact]
    public void Backspace_SendsDeleteNotBackspace()
    {
        // The naming is a historical accident: the Backspace key sends DEL (0x7F). Sending BS
        // (0x08) is why some terminals appear to delete forwards.
        Assert.Equal("\u007f", Encode(TerminalKey.Backspace, KeyModifiers.None, TerminalInputModes.Default));
        Assert.Equal("\b", Encode(TerminalKey.Backspace, KeyModifiers.Control, TerminalInputModes.Default));
    }

    [Fact]
    public void ShiftTab_SendsBackTab()
    {
        Assert.Equal("\t", Encode(TerminalKey.Tab, KeyModifiers.None, TerminalInputModes.Default));
        Assert.Equal($"{Esc}[Z", Encode(TerminalKey.Tab, KeyModifiers.Shift, TerminalInputModes.Default));
    }

    [Fact]
    public void AltPrefixesAnEscape()
    {
        // "Meta" has been transmitted as an escape prefix since long before either name settled.
        Assert.Equal($"{Esc}\r", Encode(TerminalKey.Enter, KeyModifiers.Alt, TerminalInputModes.Default));
    }

    [Theory]
    [InlineData("c", 0x03)]  // Ctrl+C, the interrupt.
    [InlineData("d", 0x04)]  // Ctrl+D, end of input.
    [InlineData("a", 0x01)]
    [InlineData("z", 0x1A)]
    [InlineData("[", 0x1B)]  // Which is why Ctrl+[ is the same as Escape.
    [InlineData(" ", 0x00)]
    public void ControlCombinations_ClearTheUpperBitsOfTheCharacter(string text, int expected)
    {
        // A control code is the character with its upper bits cleared. That is a property of ASCII
        // rather than a table, which is why Ctrl+[ and Escape are indistinguishable to a program.
        var encoded = KeyEncoder.EncodeText(text, KeyModifiers.Control);

        Assert.NotNull(encoded);
        Assert.Equal([(byte)expected], encoded);
    }

    [Fact]
    public void ControlAndAltTogether_EscapePrefixTheControlCode()
    {
        var encoded = KeyEncoder.EncodeText("c", KeyModifiers.Control | KeyModifiers.Alt);

        Assert.NotNull(encoded);
        Assert.Equal([0x1B, 0x03], encoded);
    }

    [Fact]
    public void TypedText_IsSentAsUtf8()
    {
        Assert.Equal(Encoding.UTF8.GetBytes("中"), KeyEncoder.EncodeText("中")!);
        Assert.Equal(Encoding.UTF8.GetBytes("é"), KeyEncoder.EncodeText("é")!);
    }

    [Fact]
    public void EmptyInputProducesNothing()
    {
        Assert.Null(KeyEncoder.EncodeText(string.Empty));
        Assert.Null(KeyEncoder.Encode(TerminalKey.None, KeyModifiers.None, TerminalInputModes.Default));
    }

    [Fact]
    public void PastedText_NormalisesLineEndingsToCarriageReturns()
    {
        // Pasted line breaks have to look like the Enter key, or the shell sees a mixture it has to
        // guess about.
        var pasted = KeyEncoder.EncodePaste("one\r\ntwo\nthree", TerminalInputModes.Default);

        Assert.Equal("one\rtwo\rthree", Encoding.UTF8.GetString(pasted));
    }

    [Fact]
    public void PastedText_IsBracketedWhenTheProgramAskedForIt()
    {
        // Bracketing is a safety feature as much as a convenience: a shell that knows text was
        // pasted will not execute a newline hidden inside it.
        var modes = new TerminalInputModes(BracketedPaste: true);

        var pasted = Encoding.UTF8.GetString(KeyEncoder.EncodePaste("rm -rf /\n", modes));

        Assert.StartsWith($"{Esc}[200~", pasted, StringComparison.Ordinal);
        Assert.EndsWith($"{Esc}[201~", pasted, StringComparison.Ordinal);
    }

    private static string Encode(TerminalKey key, KeyModifiers modifiers, TerminalInputModes modes)
    {
        var encoded = KeyEncoder.Encode(key, modifiers, modes);

        return encoded is null ? string.Empty : Encoding.UTF8.GetString(encoded);
    }
}
