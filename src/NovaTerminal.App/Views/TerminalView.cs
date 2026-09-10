using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NovaTerminal.Core;
using NovaTerminal.Core.Configuration;
using NovaTerminal.Input;
using NovaTerminal.Rendering;
using NovaTerminal.Terminal;

namespace NovaTerminal.App.Views;

/// <summary>
/// Draws a <see cref="TerminalState"/>.
/// </summary>
/// <remarks>
/// <para>
/// This control is an adapter and nothing more. It reads terminal state and paints it; it never
/// changes it. Everything about how a terminal behaves lives in the engine, which is why the engine
/// can be tested exhaustively without ever creating a window.
/// </para>
/// <para>
/// Drawing is one custom-drawn surface rather than a tree of text elements. A 200×50 screen is
/// 10,000 cells; a control per cell, or even per row, would spend all its time in layout. Instead
/// the whole grid is painted in one pass over coalesced runs.
/// </para>
/// </remarks>
public sealed class TerminalView : Control
{
    private const double FaintBlendAmount = 0.45;
    private const double UnderlineThicknessFactor = 0.07;
    private const double UnderlineOffsetFactor = 0.12;
    private const double BarCursorWidthFactor = 0.15;
    private const double UnderlineCursorHeightFactor = 0.12;
    private const double CursorBlinkIntervalSeconds = 0.53;

    private readonly RowRunBuilder _runBuilder = new();
    private readonly Dictionary<RgbColor, IBrush> _brushCache = [];
    private readonly DispatcherTimer _blinkTimer;

    private TerminalState _terminal;
    private TerminalTheme _theme;
    private AppearanceOptions _appearance;
    private Typeface _typeface;
    private CellMetrics _metrics;
    private bool _cursorOn = true;

    /// <summary>Creates a view over a terminal.</summary>
    public TerminalView(TerminalState terminal, TerminalTheme theme, AppearanceOptions appearance)
    {
        _terminal = terminal;
        _theme = theme;
        _appearance = appearance;
        _typeface = CreateTypeface(appearance);
        _metrics = MeasureCell(_typeface, appearance);

        Focusable = true;

        _blinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(CursorBlinkIntervalSeconds),
        };
        _blinkTimer.Tick += OnBlinkTick;
    }

    /// <summary>
    /// Raised when the user produced input that should reach the shell.
    /// </summary>
    /// <remarks>
    /// The view does not write to the shell itself. It reports what happened and lets the session
    /// decide, which keeps the control usable in a window that has no shell attached at all.
    /// </remarks>
    public event EventHandler<ReadOnlyMemory<byte>>? InputProduced;

    /// <summary>Raised when the viewport's size in cells changes.</summary>
    /// <remarks>
    /// The view reports the new size rather than acting on it. Resizing the terminal means resizing
    /// the pseudo-terminal too, and the order of those operations matters; the session owns that
    /// decision.
    /// </remarks>
    public event EventHandler<TerminalSize>? ViewportSizeChanged;

    /// <summary>The geometry of one cell, in device-independent pixels.</summary>
    public CellMetrics CellMetrics => _metrics;

    /// <summary>
    /// The engine modes that change how a key press is encoded, read at the moment of the press.
    /// </summary>
    public TerminalInputModes InputModes => new(
        _terminal.ApplicationCursorKeys,
        _terminal.ApplicationKeypad,
        BracketedPaste: false);

    /// <summary>The terminal being displayed.</summary>
    public TerminalState Terminal
    {
        get => _terminal;
        set
        {
            _terminal = value;
            InvalidateVisual();
        }
    }

    /// <summary>The colours in use.</summary>
    public TerminalTheme ColorTheme
    {
        get => _theme;
        set
        {
            _theme = value;
            _brushCache.Clear();
            InvalidateVisual();
        }
    }

    /// <summary>Applies new font and cursor settings, remeasuring the cell.</summary>
    public void UpdateAppearance(AppearanceOptions appearance)
    {
        _appearance = appearance;
        _typeface = CreateTypeface(appearance);
        _metrics = MeasureCell(_typeface, appearance);

        ReportViewportSize();
        InvalidateVisual();
    }

    /// <summary>Repaints the parts of the screen the engine has marked as changed.</summary>
    /// <remarks>
    /// Avalonia composites whole controls, so this currently invalidates the entire view. The
    /// engine's per-row damage set is still what decides <em>whether</em> to draw at all, which is
    /// the difference that matters when a command produces thousands of lines a second.
    /// </remarks>
    public void InvalidateDamagedRows()
    {
        if (!_terminal.Buffer.HasDamage)
        {
            return;
        }

        _terminal.Buffer.ClearDamage();
        RestartCursorBlink();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(GetBrush(_theme.Background), bounds);

        var size = _terminal.Size;
        var rows = Math.Min(size.Rows, (int)Math.Ceiling(bounds.Height / _metrics.Height));

        for (var row = 0; row < rows; row++)
        {
            RenderRow(context, row);
        }

        RenderCursor(context);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var modifiers = KeyMapping.ToTerminalModifiers(e.KeyModifiers);
        var key = KeyMapping.ToTerminalKey(e.Key);

        if (key == TerminalKey.None)
        {
            // Ctrl+C, Alt+F and the like never produce a text input event, so they are encoded from
            // the key itself. Without this, a terminal could not be interrupted.
            if (TryEncodeCharacterCombination(e.Key, modifiers, out var combination))
            {
                InputProduced?.Invoke(this, combination);
                e.Handled = true;
            }

            return;
        }

        var encoded = KeyEncoder.Encode(key, modifiers, InputModes);

        if (encoded is null)
        {
            return;
        }

        InputProduced?.Invoke(this, encoded);

        // Marking the event handled stops the key also being delivered as text, and stops Avalonia
        // treating Tab as a request to move focus out of the terminal.
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        // Text input already reflects the keyboard layout and any dead keys, which is why typed
        // characters are taken from here rather than reconstructed from key codes.
        // Text input carries no modifiers: a control or alt combination never reaches it at all,
        // which is why those are handled in OnKeyDown instead.
        var encoded = KeyEncoder.EncodeText(e.Text);

        if (encoded is null)
        {
            return;
        }

        InputProduced?.Invoke(this, encoded);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => availableSize;

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ReportViewportSize();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_appearance.CursorBlink)
        {
            _blinkTimer.Start();
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _blinkTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Encodes a modifier combination applied to an ordinary character key.
    /// </summary>
    /// <remarks>
    /// Only control and alt combinations are handled here. Plain characters, and anything involving
    /// a keyboard layout or a dead key, come through text input instead, where the operating system
    /// has already worked out which character was meant.
    /// </remarks>
    private static bool TryEncodeCharacterCombination(
        Key key, NovaTerminal.Input.KeyModifiers modifiers, out byte[] encoded)
    {
        encoded = [];

        var wantsControl = modifiers.HasFlag(NovaTerminal.Input.KeyModifiers.Control);
        var wantsAlt = modifiers.HasFlag(NovaTerminal.Input.KeyModifiers.Alt);

        if (!wantsControl && !wantsAlt)
        {
            return false;
        }

        var character = key switch
        {
            >= Key.A and <= Key.Z => (char)('a' + (key - Key.A)),
            >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
            Key.Space => ' ',
            Key.OemOpenBrackets => '[',
            Key.OemCloseBrackets => ']',
            Key.OemBackslash or Key.Oem5 => '\\',
            Key.OemMinus => '-',
            Key.Oem2 => '/',
            _ => '\0',
        };

        if (character == '\0')
        {
            return false;
        }

        var result = KeyEncoder.EncodeText(character.ToString(), modifiers);

        if (result is null)
        {
            return false;
        }

        encoded = result;
        return true;
    }

    private void RenderRow(DrawingContext context, int row)
    {
        var cells = _terminal.Buffer.GetRow(row);
        var runs = _runBuilder.Build(cells);
        var y = row * _metrics.Height;

        foreach (var run in runs)
        {
            var (foreground, background) = ResolveColors(run.Style);
            var x = run.Column * _metrics.Width;
            var width = run.Length * _metrics.Width;

            // The whole view was already filled with the theme background, so a run that matches it
            // needs no rectangle of its own.
            if (background != _theme.Background)
            {
                context.FillRectangle(GetBrush(background), new Rect(x, y, width, _metrics.Height));
            }

            if (run.IsBlank || run.Style.HasAttributes(TextAttributes.Invisible))
            {
                continue;
            }

            var text = _runBuilder.GetRunText(cells, in run);
            if (text.Length == 0)
            {
                continue;
            }

            DrawRunText(context, text, run.Style, foreground, x, y, width);
        }
    }

    private void DrawRunText(
        DrawingContext context,
        string text,
        CellStyle style,
        RgbColor foreground,
        double x,
        double y,
        double width)
    {
        var typeface = style.HasAttributes(TextAttributes.Bold) || style.HasAttributes(TextAttributes.Italic)
            ? new Typeface(
                _typeface.FontFamily,
                style.HasAttributes(TextAttributes.Italic) ? FontStyle.Italic : FontStyle.Normal,
                style.HasAttributes(TextAttributes.Bold) ? FontWeight.Bold : FontWeight.Normal)
            : _typeface;

        var brush = GetBrush(foreground);

        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            _appearance.FontSize,
            brush);

        // The text is positioned by its baseline so that glyphs sit on a common line regardless of
        // their individual heights.
        context.DrawText(formatted, new Point(x, y + _metrics.Baseline - formatted.Baseline));

        if (style.HasAttributes(TextAttributes.Underline))
        {
            var thickness = Math.Max(1, _metrics.Height * UnderlineThicknessFactor);
            var underlineY = y + _metrics.Baseline + (_metrics.Height * UnderlineOffsetFactor);
            context.FillRectangle(brush, new Rect(x, underlineY, width, thickness));
        }

        if (style.HasAttributes(TextAttributes.Strikethrough))
        {
            var thickness = Math.Max(1, _metrics.Height * UnderlineThicknessFactor);
            var strikeY = y + (_metrics.Height / 2);
            context.FillRectangle(brush, new Rect(x, strikeY, width, thickness));
        }
    }

    private void RenderCursor(DrawingContext context)
    {
        var cursor = _terminal.Cursor;

        if (!cursor.IsVisible)
        {
            return;
        }

        if (_appearance.CursorBlink && !_cursorOn)
        {
            return;
        }

        var size = _terminal.Size;
        if (!size.Contains(cursor.Column, cursor.Row))
        {
            return;
        }

        var (x, y) = _metrics.GetCellOrigin(cursor.Column, cursor.Row);
        var brush = GetBrush(_theme.Cursor);

        switch (cursor.Style)
        {
            case CursorStyle.Bar:
                context.FillRectangle(
                    brush, new Rect(x, y, Math.Max(1, _metrics.Width * BarCursorWidthFactor), _metrics.Height));
                break;

            case CursorStyle.Underline:
                {
                    var height = Math.Max(1, _metrics.Height * UnderlineCursorHeightFactor);
                    context.FillRectangle(brush, new Rect(x, y + _metrics.Height - height, _metrics.Width, height));
                    break;
                }

            default:
                {
                    var cell = _terminal.Buffer[cursor.Column, cursor.Row];
                    var cellWidth = cell.IsWideLeading ? _metrics.Width * 2 : _metrics.Width;
                    context.FillRectangle(brush, new Rect(x, y, cellWidth, _metrics.Height));

                    if (!cell.IsEmpty)
                    {
                        // Redraw the character in the cursor's contrasting colour so it stays legible
                        // underneath a solid block.
                        var formatted = new FormattedText(
                            cell.DisplayCharacter.ToString(),
                            System.Globalization.CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            _typeface,
                            _appearance.FontSize,
                            GetBrush(_theme.CursorText));

                        context.DrawText(formatted, new Point(x, y + _metrics.Baseline - formatted.Baseline));
                    }

                    break;
                }
        }
    }

    private (RgbColor Foreground, RgbColor Background) ResolveColors(CellStyle style)
    {
        var foreground = _theme.Resolve(style.Foreground, isBackground: false);
        var background = _theme.Resolve(style.Background, isBackground: true);

        if (style.HasAttributes(TextAttributes.Inverse))
        {
            (foreground, background) = (background, foreground);
        }

        if (style.HasAttributes(TextAttributes.Faint))
        {
            // Faint is defined as reduced intensity, which means blending toward the background
            // rather than picking a fixed grey - the result has to work on any theme.
            foreground = foreground.Blend(background, FaintBlendAmount);
        }

        return (foreground, background);
    }

    private IBrush GetBrush(RgbColor color)
    {
        if (_brushCache.TryGetValue(color, out var brush))
        {
            return brush;
        }

        brush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
        _brushCache[color] = brush;
        return brush;
    }

    private void ReportViewportSize()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        ViewportSizeChanged?.Invoke(this, _metrics.MeasureViewport(Bounds.Width, Bounds.Height));
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        _cursorOn = !_cursorOn;
        InvalidateVisual();
    }

    private void RestartCursorBlink()
    {
        // Output or typing should leave the cursor visible rather than catching it mid-blink.
        _cursorOn = true;

        if (!_blinkTimer.IsEnabled)
        {
            return;
        }

        _blinkTimer.Stop();
        _blinkTimer.Start();
    }

    private static Typeface CreateTypeface(AppearanceOptions appearance)
        => new(FontFamily.Parse(appearance.FontFamily), FontStyle.Normal, FontWeight.Normal);

    /// <summary>
    /// Measures one cell from the font itself.
    /// </summary>
    /// <remarks>
    /// The grid's geometry has to come from the font, not from a guess: if the assumed advance
    /// width disagrees with the real one by even a fraction of a pixel, the error accumulates
    /// across a row and the right-hand columns drift out of alignment.
    /// </remarks>
    private static CellMetrics MeasureCell(Typeface typeface, AppearanceOptions appearance)
    {
        var reference = new FormattedText(
            "M",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            appearance.FontSize,
            Brushes.White);

        var width = Math.Max(1, reference.WidthIncludingTrailingWhitespace);
        var height = Math.Max(1, reference.Height * appearance.LineHeightFactor);
        var baseline = Math.Clamp(reference.Baseline, 0, height);

        return new CellMetrics(width, height, baseline);
    }
}
