using Avalonia.Input;
using NovaTerminal.Input;
using TerminalModifiers = NovaTerminal.Input.KeyModifiers;
using ToolkitModifiers = Avalonia.Input.KeyModifiers;

namespace NovaTerminal.App.Views;

/// <summary>
/// Translates Avalonia's key events into the toolkit-neutral form the input layer understands.
/// </summary>
/// <remarks>
/// This is the entire GUI-specific part of key handling, and it is deliberately trivial: a pair of
/// lookups and nothing else. All the difficult knowledge - what an arrow key sends, how modifiers
/// are encoded, what changes under application cursor mode - lives in
/// <see cref="KeyEncoder"/>, where it can be tested without a window.
/// </remarks>
internal static class KeyMapping
{
    /// <summary>Maps an Avalonia key to a terminal key, or <see cref="TerminalKey.None"/>.</summary>
    public static TerminalKey ToTerminalKey(Key key) => key switch
    {
        Key.Enter => TerminalKey.Enter,
        Key.Back => TerminalKey.Backspace,
        Key.Tab => TerminalKey.Tab,
        Key.Escape => TerminalKey.Escape,
        Key.Delete => TerminalKey.Delete,
        Key.Insert => TerminalKey.Insert,
        Key.Home => TerminalKey.Home,
        Key.End => TerminalKey.End,
        Key.PageUp => TerminalKey.PageUp,
        Key.PageDown => TerminalKey.PageDown,
        Key.Up => TerminalKey.Up,
        Key.Down => TerminalKey.Down,
        Key.Left => TerminalKey.Left,
        Key.Right => TerminalKey.Right,
        Key.F1 => TerminalKey.F1,
        Key.F2 => TerminalKey.F2,
        Key.F3 => TerminalKey.F3,
        Key.F4 => TerminalKey.F4,
        Key.F5 => TerminalKey.F5,
        Key.F6 => TerminalKey.F6,
        Key.F7 => TerminalKey.F7,
        Key.F8 => TerminalKey.F8,
        Key.F9 => TerminalKey.F9,
        Key.F10 => TerminalKey.F10,
        Key.F11 => TerminalKey.F11,
        Key.F12 => TerminalKey.F12,
        _ => TerminalKey.None,
    };

    /// <summary>Maps Avalonia's modifier flags to the terminal's.</summary>
    public static TerminalModifiers ToTerminalModifiers(ToolkitModifiers modifiers)
    {
        var result = TerminalModifiers.None;

        if (modifiers.HasFlag(ToolkitModifiers.Shift))
        {
            result |= TerminalModifiers.Shift;
        }

        if (modifiers.HasFlag(ToolkitModifiers.Alt))
        {
            result |= TerminalModifiers.Alt;
        }

        if (modifiers.HasFlag(ToolkitModifiers.Control))
        {
            result |= TerminalModifiers.Control;
        }

        if (modifiers.HasFlag(ToolkitModifiers.Meta))
        {
            result |= TerminalModifiers.Super;
        }

        return result;
    }
}
