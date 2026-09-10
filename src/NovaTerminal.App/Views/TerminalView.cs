using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using NovaTerminal.Core;
using NovaTerminal.Core.Configuration;
using NovaTerminal.Input;
using NovaTerminal.Rendering;
using NovaTerminal.Terminal;
using TerminalSelectionMode = NovaTerminal.Rendering.SelectionMode;

namespace NovaTerminal.App.Views;

/// <summary>
/// Draws a <see cref="TerminalState"/> and turns pointer and keyboard activity into terminal input.
/// </summary>
/// <remarks>
/// <para>
/// This control is an adapter. It reads terminal state and paints it, and it reports what the user
/// did; it never decides what any of it means. Everything about how a terminal behaves lives in the
/// engine, which is why the engine can be tested exhaustively without ever creating a window.
/// </para>
/// <para>
/// Drawing is one custom-drawn surface rather than a tree of text elements. A 200x50 screen is
/// 10,000 cells; a control per cell, or even per row, would spend all its time in layout.
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
    private const int WheelLinesPerNotch = 3;

    private readonly RowRunBuilder _runBuilder = new();
    private readonly Dictionary<RgbColor, IBrush> _brushCache = [];
    private readonly DispatcherTimer _blinkTimer;

    private TerminalState _terminal;
    private TerminalTheme _theme;
    private AppearanceOptions _appearance;
    private Typeface _typeface;
    private CellMetrics _metrics;
    private TerminalSelection? _selection;
    private bool _isSelecting;
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
        Cursor = new Cursor(StandardCursorType.Ibeam);

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CursorBlinkIntervalSeconds) };
        _blinkTimer.Tick += OnBlinkTick;
    }

    /// <summary>Raised when the viewport's size in cells changes.</summary>
    /// <remarks>
    /// The view reports the new size rather than acting on it: resizing the terminal means resizing
    /// the pseudo-terminal too, and the session owns the ordering that makes that safe.
    /// </remarks>
    public event EventHandler<TerminalSize>? ViewportSizeChanged;

    /// <summary>Raised when the user produced input that should reach the shell.</summary>
    public event EventHandler<ReadOnlyMemory<byte>>? InputProduced;

    /// <summary>Raised when the selection changed, so the window can enable or disable Copy.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The geometry of one cell, in device-independent pixels.</summary>
    public CellMetrics CellMetrics => _metrics;

    /// <summary>The terminal being displayed.</summary>
    public TerminalState Terminal
    {
        get => _terminal;
        set
        {
            _terminal = value;
            ClearSelection();
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

    /// <summary>The currently selected text, or an empty string when nothing is selected.</summary>
    public string SelectedText => _selection?.GetText(_terminal) ?? string.Empty;

    /// <summary>Whether anything is selected.</summary>
    public bool HasSelection => _selection is { IsEmpty: false };

    /// <summary>The engine modes that change how a key press is encoded.</summary>
    public TerminalInputModes InputModes => new(
        _terminal.ApplicationCursorKeys,
        _terminal.ApplicationKeypad,
        _terminal.BracketedPaste);

    /// <summary>Applies new font and cursor settings, remeasuring the cell.</summary>
    public void UpdateAppearance(AppearanceOptions appearance)
    {
        _appearance = appearance;
        _typeface = CreateTypeface(appearance);
        _metrics = MeasureCell(_typeface, appearance);

        ReportViewportSize();
        InvalidateVisual();
    }

    /// <summary>Repaints when the engine reports that something changed.</summary>
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

    /// <summary>Selects the entire visible screen.</summary>
    public void SelectAll()
    {
        var size = _terminal.Size;
        SetSelection(new TerminalSelection(
            new CellPosition(0, 0),
            new CellPosition(size.Columns, size.Rows - 1)));
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection()
    {
        if (_selection is null)
        {
            return;
        }

        _selection = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Scrolls the view through history and repaints.</summary>
    public void ScrollBy(int lines)
    {
        var moved = lines > 0 ? _terminal.ScrollViewBack(lines) : _terminal.ScrollViewForward(-lines);

        if (moved)
        {
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(GetBrush(_theme.Background), bounds);

        var rows = Math.Min(_terminal.Size.Rows, (int)Math.Ceiling(bounds.Height / _metrics.Height));

        for (var row = 0; row < rows; row++)
        {
            RenderRow(context, row);
        }

        RenderCursor(context);
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
                Send(combination);
                e.Handled = true;
            }

            return;
        }

        var encoded = KeyEncoder.Encode(key, modifiers, InputModes);

        if (encoded is null)
        {
            return;
        }

        Send(encoded);

        // Marking the event handled stops the key also arriving as text, and stops Avalonia
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
        // characters are taken from here rather than reconstructed from key codes. It carries no
        // modifiers: control and alt combinations are handled in OnKeyDown.
        var encoded = KeyEncoder.EncodeText(e.Text);

        if (encoded is null)
        {
            return;
        }

        Send(encoded);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var point = e.GetCurrentPoint(this);

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = HitTest(point.Position);

        if (e.ClickCount == 2)
        {
            SelectWordAt(position);
            return;
        }

        if (e.ClickCount >= 3)
        {
            SelectLineAt(position);
            return;
        }

        // Alt turns a drag into a rectangular selection, which is what you want for one column of
        // tabular output.
        var mode = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt)
            ? TerminalSelectionMode.Block
            : TerminalSelectionMode.Linear;

        _isSelecting = true;
        SetSelection(new TerminalSelection(position, position, mode));
        e.Pointer.Capture(this);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isSelecting || _selection is not { } selection)
        {
            return;
        }

        SetSelection(selection with { Focus = HitTest(e.GetPosition(this)) });
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        e.Pointer.Capture(null);
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (e.Delta.Y == 0)
        {
            return;
        }

        ScrollBy((int)Math.Round(e.Delta.Y) * WheelLinesPerNotch);
        e.Handled = true;
    }

    private void Send(ReadOnlyMemory<byte> data)
    {
        // Typing while reading history takes the user back to the prompt, where their keystrokes
        // are actually going.
        if (_terminal.ScrollViewToBottom())
        {
            InvalidateVisual();
        }

        InputProduced?.Invoke(this, data);
    }

    private CellPosition HitTest(Point point)
    {
        var (column, row) = _metrics.HitTest(point.X, point.Y, _terminal.Size);
        return new CellPosition(column, row);
    }

    private void SetSelection(TerminalSelection selection)
    {
        _selection = selection;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Selects the word under a position, as a double-click does.</summary>
    private void SelectWordAt(CellPosition position)
    {
        var cells = _terminal.GetViewRow(position.Row);

        if (cells.Length == 0)
        {
            return;
        }

        var column = Math.Clamp(position.Column, 0, cells.Length - 1);

        if (IsWordSeparator(cells[column]))
        {
            SetSelection(new TerminalSelection(position, position with { Column = position.Column + 1 }));
            return;
        }

        var start = column;
        while (start > 0 && !IsWordSeparator(cells[start - 1]))
        {
            start--;
        }

        var end = column;
        while (end + 1 < cells.Length && !IsWordSeparator(cells[end + 1]))
        {
            end++;
        }

        SetSelection(new TerminalSelection(
            new CellPosition(start, position.Row),
            new CellPosition(end + 1, position.Row)));
    }

    private void SelectLineAt(CellPosition position)
        => SetSelection(new TerminalSelection(
            new CellPosition(0, position.Row),
            new CellPosition(_terminal.Size.Columns, position.Row)));

    /// <summary>
    /// Whether a cell breaks a word. Paths and URLs are what people most often double-click in a
    /// terminal, so the separator set is deliberately narrow: dots, slashes and dashes are kept.
    /// </summary>
    private static bool IsWordSeparator(TerminalCell cell)
    {
        if (cell.IsEmpty)
        {
            return true;
        }

        var value = cell.Character.Value;
        return value is ' ' or '\t' or '"' or '\'' or '`' or '(' or ')' or '[' or ']' or '{' or '}' or '<' or '>';
    }

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
        // Rows come from the current view, which may be showing history rather than the live
        // screen. The renderer never needs to know which.
        var cells = _terminal.GetViewRow(row);
        var runs = _runBuilder.Build(cells);
        var y = row * _metrics.Height;

        foreach (var run in runs)
        {
            var (foreground, background) = ResolveColors(run.Style);
            var x = run.Column * _metrics.Width;
            var width = run.Length * _metrics.Width;

            if (background != _theme.Background)
            {
                context.FillRectangle(GetBrush(background), new Rect(x, y, width, _metrics.Height));
            }

            if (run.IsBlank || run.Style.HasAttributes(TextAttributes.Invisible))
            {
                continue;
            }

            var text = _runBuilder.GetRunText(cells, in run);

            if (text.Length > 0)
            {
                DrawRunText(context, text, run.Style, foreground, x, y, width);
            }
        }

        RenderSelection(context, row, cells.Length, y);
    }

    /// <summary>
    /// Paints the selection over the row.
    /// </summary>
    /// <remarks>
    /// Drawn as an overlay in the theme's selection colour rather than by re-colouring cells, so
    /// that a change to the selection never has to touch terminal state.
    /// </remarks>
    private void RenderSelection(DrawingContext context, int row, int columns, double y)
    {
        if (_selection is not { IsEmpty: false } selection)
        {
            return;
        }

        var start = -1;

        for (var column = 0; column <= columns; column++)
        {
            var selected = column < columns && selection.Contains(column, row);

            if (selected && start < 0)
            {
                start = column;
            }
            else if (!selected && start >= 0)
            {
                var x = start * _metrics.Width;
                var width = (column - start) * _metrics.Width;
                context.FillRectangle(GetBrush(_theme.SelectionBackground), new Rect(x, y, width, _metrics.Height));
                start = -1;
            }
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
        var isBold = style.HasAttributes(TextAttributes.Bold);
        var isItalic = style.HasAttributes(TextAttributes.Italic);

        var typeface = isBold || isItalic
            ? new Typeface(
                _typeface.FontFamily,
                isItalic ? FontStyle.Italic : FontStyle.Normal,
                isBold ? FontWeight.Bold : FontWeight.Normal)
            : _typeface;

        var brush = GetBrush(foreground);

        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            _appearance.FontSize,
            brush);

        // Text is positioned by its baseline so glyphs sit on a common line regardless of height.
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
            context.FillRectangle(brush, new Rect(x, y + (_metrics.Height / 2), width, thickness));
        }
    }

    private void RenderCursor(DrawingContext context)
    {
        var cursor = _terminal.Cursor;

        // While reading history there is no meaningful place for the cursor: the prompt it belongs
        // to is somewhere else entirely.
        if (!cursor.IsVisible || _terminal.IsScrolledBack)
        {
            return;
        }

        if (_appearance.CursorBlink && !_cursorOn)
        {
            return;
        }

        if (!_terminal.Size.Contains(cursor.Column, cursor.Row))
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
                        // Redraw the character in a contrasting colour so it stays legible under a
                        // solid block.
                        var formatted = new FormattedText(
                            cell.DisplayCharacter.ToString(),
                            CultureInfo.InvariantCulture,
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
            // Faint means reduced intensity, so it is computed against the actual background and
            // works on a light theme as well as a dark one.
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
    /// width disagrees with the real one by a fraction of a pixel, the error accumulates across a
    /// row and the right-hand columns drift out of alignment.
    /// </remarks>
    private static CellMetrics MeasureCell(Typeface typeface, AppearanceOptions appearance)
    {
        var reference = new FormattedText(
            "M",
            CultureInfo.InvariantCulture,
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
