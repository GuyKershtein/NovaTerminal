using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovaTerminal.App.Logging;
using NovaTerminal.App.Sessions;
using NovaTerminal.Core;
using NovaTerminal.Core.Configuration;
using NovaTerminal.Input;
using NovaTerminal.Platform;
using NovaTerminal.Process;
using NovaTerminal.Rendering;
using NovaTerminal.Terminal;
using TerminalTheme = NovaTerminal.Rendering.TerminalTheme;

namespace NovaTerminal.App.Views;

/// <summary>
/// The application window: a tab strip, a terminal surface, a search bar and a status line.
/// </summary>
/// <remarks>
/// The window owns tabs and routes user intent to whichever is active. It contains no terminal
/// logic: everything it does is create a tab, hand input to it, or ask it for text.
/// </remarks>
public partial class MainWindow : Window, IDisposable
{
    private const int PageScrollLines = 20;

    private readonly NovaTerminalOptions _options;
    private readonly ILogger<MainWindow> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ThemeCatalog _themes;
    private readonly List<TerminalTab> _tabs = [];

    private TerminalTheme _theme;
    private double _fontSize;

    private IShellBackend? _backend;
    private TerminalTab? _activeTab;
    private IReadOnlyList<SearchMatch> _matches = [];
    private int _matchIndex = -1;
    private bool _disposed;

    /// <summary>
    /// Design-time constructor, needed by the XAML previewer and the runtime XAML loader.
    /// </summary>
    public MainWindow()
        : this(new NovaTerminalOptions(), NullLogger<MainWindow>.Instance, NullLoggerFactory.Instance)
    {
    }

    /// <summary>Creates the window with its dependencies.</summary>
    [ActivatorUtilitiesConstructor]
    public MainWindow(NovaTerminalOptions options, ILogger<MainWindow> logger, ILoggerFactory loggerFactory)
    {
        _options = options;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _themes = new ThemeCatalog(options.Themes.AsReadOnly());
        _theme = _themes.GetOrDefault(options.Appearance.ThemeName);
        _fontSize = options.Appearance.FontSize;

        InitializeComponent();

        Background = new SolidColorBrush(
            Color.FromRgb(_theme.Background.Red, _theme.Background.Green, _theme.Background.Blue));

        Title = $"{AppInfo.Name} {AppInfo.Version}";

        NewTabButton.Click += async (_, _) => await AddTabAsync().ConfigureAwait(true);
        SearchBox.KeyDown += OnSearchBoxKeyDown;
        SearchBox.TextChanged += (_, _) => RunSearch();
        SearchNextButton.Click += (_, _) => StepMatch(1);
        SearchPreviousButton.Click += (_, _) => StepMatch(-1);

        Opened += OnOpened;
        Closing += OnClosing;
        KeyDown += OnWindowKeyDown;

        UpdateStatus();
    }

    /// <summary>The active tab's terminal, or null before the first tab exists.</summary>
    public TerminalState? Terminal => _activeTab?.Terminal;

    /// <summary>The tabs currently open.</summary>
    public IReadOnlyList<TerminalTab> Tabs => _tabs;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var tab in _tabs)
        {
            tab.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _tabs.Clear();
        GC.SuppressFinalize(this);
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            _backend = ShellBackendFactory.Create(_loggerFactory);
            await AddTabAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.SessionStartFailed(exception);
            ShowFailure(exception);
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var tabs = _tabs.ToArray();
        _tabs.Clear();
        _activeTab = null;

        foreach (var tab in tabs)
        {
            var id = tab.Session?.Shell.Id;
            await tab.DisposeAsync().ConfigureAwait(true);

            if (id is not null)
            {
                _logger.SessionClosed(id);
            }
        }
    }

    private async Task AddTabAsync()
    {
        if (_backend is null)
        {
            return;
        }

        var tab = new TerminalTab(_options, _theme, _loggerFactory);

        tab.View.ViewportSizeChanged += async (_, size) => await OnViewportSizeChanged(tab, size).ConfigureAwait(true);
        tab.View.InputProduced += async (_, data) => await tab.WriteAsync(data).ConfigureAwait(true);
        tab.View.SelectionChanged += (_, _) => UpdateStatus();
        tab.OutputApplied += OnTabOutputApplied;
        tab.TitleChanged += (_, _) => RefreshTabStrip();
        tab.Exited += OnTabExited;

        _tabs.Add(tab);
        ActivateTab(tab);
        RefreshTabStrip();

        try
        {
            await tab.StartAsync(_backend).ConfigureAwait(true);

            if (tab.Session is { } session)
            {
                _logger.SessionCreated(
                    session.Shell.Id, tab.Title, tab.Terminal.Size.Columns, tab.Terminal.Size.Rows);
            }
        }
        catch (Exception exception)
        {
            _logger.SessionStartFailed(exception);
            ShowFailure(exception);
        }

        UpdateStatus();
    }

    private async Task CloseTabAsync(TerminalTab tab)
    {
        if (!_tabs.Remove(tab))
        {
            return;
        }

        var id = tab.Session?.Shell.Id;
        await tab.DisposeAsync().ConfigureAwait(true);

        if (id is not null)
        {
            _logger.SessionClosed(id);
        }

        if (_tabs.Count == 0)
        {
            // Closing the last tab closes the window, which is what every terminal does.
            Close();
            return;
        }

        if (_activeTab == tab)
        {
            ActivateTab(_tabs[^1]);
        }

        RefreshTabStrip();
        UpdateStatus();
    }

    private void ActivateTab(TerminalTab tab)
    {
        _activeTab = tab;
        TerminalHost.Child = tab.View;
        Title = $"{tab.Title} - {AppInfo.Name}";

        ClearSearch();
        tab.View.Focus();
        RefreshTabStrip();
        UpdateStatus();
    }

    private void SelectTabByOffset(int offset)
    {
        if (_activeTab is null || _tabs.Count < 2)
        {
            return;
        }

        var index = _tabs.IndexOf(_activeTab);
        var next = ((index + offset) % _tabs.Count + _tabs.Count) % _tabs.Count;
        ActivateTab(_tabs[next]);
    }

    private void RefreshTabStrip()
    {
        // A single-session terminal should look like a terminal, not a tabbed application.
        TabStrip.IsVisible = _tabs.Count > 1;

        var buttons = new List<Control>(_tabs.Count);

        foreach (var tab in _tabs)
        {
            var isActive = tab == _activeTab;

            var label = new TextBlock
            {
                Text = Truncate(tab.Title, 22),
                FontSize = 12,
                Foreground = new SolidColorBrush(isActive ? Colors.White : Color.FromRgb(0x8A, 0x91, 0xA5)),
            };

            var close = new Button
            {
                Content = "x",
                FontSize = 10,
                Padding = new Avalonia.Thickness(4, 0),
                Margin = new Avalonia.Thickness(6, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = default,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0xA5)),
            };

            var closing = tab;
            close.Click += async (_, _) => await CloseTabAsync(closing).ConfigureAwait(true);

            var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            content.Children.Add(label);
            content.Children.Add(close);

            var button = new Button
            {
                Content = content,
                Padding = new Avalonia.Thickness(10, 5),
                Background = isActive
                    ? new SolidColorBrush(Color.FromRgb(0x11, 0x13, 0x1A))
                    : Brushes.Transparent,
                BorderThickness = default,
            };

            var selecting = tab;
            button.Click += (_, _) => ActivateTab(selecting);

            buttons.Add(button);
        }

        TabItems.ItemsSource = buttons;
    }

    private void OnTabOutputApplied(object? sender, EventArgs e)
    {
        if (sender is TerminalTab tab && tab == _activeTab)
        {
            tab.View.InvalidateDamagedRows();
            UpdateStatus();
        }
    }

    private void OnTabExited(object? sender, ShellExitedEventArgs e)
    {
        if (sender is not TerminalTab tab)
        {
            return;
        }

        var reason = e.ExitCode is { } code ? $"exited with code {code}" : e.Error?.Message ?? "ended";
        StatusText.Text = $"{tab.Title}: shell {reason}";
        tab.View.InvalidateDamagedRows();
    }

    private async Task OnViewportSizeChanged(TerminalTab tab, TerminalSize size)
    {
        if (size == tab.Terminal.Size)
        {
            return;
        }

        // The engine and the pseudo console are resized together; the session owns that ordering.
        await tab.ResizeAsync(size).ConfigureAwait(true);
        tab.View.InvalidateDamagedRows();
        UpdateStatus();
    }

    // ----- Shortcuts -----

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);

        // Ctrl+Shift is the terminal convention for the emulator's own shortcuts, precisely because
        // plain Ctrl combinations belong to the program running inside.
        if (control && shift)
        {
            switch (e.Key)
            {
                case Key.C:
                    await CopyAsync().ConfigureAwait(true);
                    e.Handled = true;
                    return;
                case Key.V:
                    await PasteAsync().ConfigureAwait(true);
                    e.Handled = true;
                    return;
                case Key.A:
                    _activeTab?.View.SelectAll();
                    e.Handled = true;
                    return;
                case Key.F:
                    ToggleSearch();
                    e.Handled = true;
                    return;
                case Key.T:
                    await AddTabAsync().ConfigureAwait(true);
                    e.Handled = true;
                    return;
                case Key.W when _activeTab is { } tab:
                    await CloseTabAsync(tab).ConfigureAwait(true);
                    e.Handled = true;
                    return;
                case Key.Tab:
                    SelectTabByOffset(shift ? -1 : 1);
                    e.Handled = true;
                    return;
                case Key.P:
                    CycleTheme();
                    e.Handled = true;
                    return;
            }
        }

        if (control && e.Key is Key.OemPlus or Key.Add)
        {
            AdjustFontSize(1);
            e.Handled = true;
            return;
        }

        if (control && e.Key is Key.OemMinus or Key.Subtract)
        {
            AdjustFontSize(-1);
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.D0)
        {
            AdjustFontSize(0);
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.Tab)
        {
            SelectTabByOffset(1);
            e.Handled = true;
            return;
        }

        if (shift && _activeTab is { } scrolled)
        {
            switch (e.Key)
            {
                case Key.PageUp:
                    scrolled.View.ScrollBy(PageScrollLines);
                    e.Handled = true;
                    return;
                case Key.PageDown:
                    scrolled.View.ScrollBy(-PageScrollLines);
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Escape && SearchBar.IsVisible && SearchBox.IsFocused)
        {
            ClearSearch();
            e.Handled = true;
        }
    }

    /// <summary>Switches to the next theme, applying it to every open tab.</summary>
    private void CycleTheme()
    {
        _theme = _themes.GetNext(_theme.Name);
        _options.Appearance.ThemeName = _theme.Name;

        Background = new SolidColorBrush(
            Color.FromRgb(_theme.Background.Red, _theme.Background.Green, _theme.Background.Blue));

        foreach (var tab in _tabs)
        {
            // Cells coloured "default" repaint in the new theme's colours precisely because the
            // engine stored the intent rather than a resolved colour.
            tab.View.ColorTheme = _theme;
        }

        UpdateStatus();
    }

    /// <summary>
    /// Changes the font size, or returns it to the configured value when the delta is zero.
    /// </summary>
    /// <remarks>
    /// Resizing the font resizes the terminal: the cell grows, so fewer cells fit, and the shell has
    /// to be told. That happens automatically, because remeasuring reports a new viewport size
    /// through the same path a window resize uses.
    /// </remarks>
    private void AdjustFontSize(int delta)
    {
        var target = delta == 0
            ? new NovaTerminalOptions().Appearance.FontSize
            : Math.Clamp(_fontSize + delta, AppearanceOptions.MinFontSize, AppearanceOptions.MaxFontSize);

        if (Math.Abs(target - _fontSize) < double.Epsilon)
        {
            return;
        }

        _fontSize = target;
        _options.Appearance.FontSize = target;

        foreach (var tab in _tabs)
        {
            tab.View.UpdateAppearance(_options.Appearance);
        }

        UpdateStatus();
    }

    private async Task CopyAsync()
    {
        if (_activeTab is not { } tab || !tab.View.HasSelection || Clipboard is null)
        {
            return;
        }

        await Clipboard.SetTextAsync(tab.View.SelectedText).ConfigureAwait(true);
    }

    private async Task PasteAsync()
    {
        if (_activeTab is not { } tab || Clipboard is null)
        {
            return;
        }

        var text = await Clipboard.TryGetTextAsync().ConfigureAwait(true);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Bracketed paste, when the program asked for it, lets a shell tell pasted text from typing
        // and refuse to execute a newline hidden inside it.
        await tab.WriteAsync(KeyEncoder.EncodePaste(text, tab.View.InputModes)).ConfigureAwait(true);
    }

    // ----- Search -----

    private void ToggleSearch()
    {
        if (SearchBar.IsVisible)
        {
            ClearSearch();
            return;
        }

        SearchBar.IsVisible = true;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void ClearSearch()
    {
        SearchBar.IsVisible = false;
        SearchStatus.Text = string.Empty;
        _matches = [];
        _matchIndex = -1;
        _activeTab?.View.Focus();
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            StepMatch(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClearSearch();
            e.Handled = true;
        }
    }

    private void RunSearch()
    {
        if (_activeTab is not { } tab)
        {
            return;
        }

        var query = SearchBox.Text ?? string.Empty;

        if (query.Length == 0)
        {
            _matches = [];
            _matchIndex = -1;
            SearchStatus.Text = string.Empty;
            return;
        }

        _matches = TerminalSearch.FindAll(tab.Terminal, query);
        _matchIndex = _matches.Count > 0 ? 0 : -1;

        ShowMatch();
    }

    private void StepMatch(int direction)
    {
        if (_matches.Count == 0)
        {
            RunSearch();
            return;
        }

        // Wrapping round is what a user expects from find-next at the end of the history.
        _matchIndex = ((_matchIndex + direction) % _matches.Count + _matches.Count) % _matches.Count;
        ShowMatch();
    }

    private void ShowMatch()
    {
        if (_activeTab is not { } tab)
        {
            return;
        }

        if (_matchIndex < 0 || _matches.Count == 0)
        {
            SearchStatus.Text = "no matches";
            return;
        }

        var match = _matches[_matchIndex];
        var offset = TerminalSearch.GetViewportOffsetFor(tab.Terminal, match.Line);

        tab.Terminal.ScrollViewToBottom();
        tab.Terminal.ScrollViewBack(offset);
        tab.View.InvalidateVisual();

        SearchStatus.Text = string.Create(
            CultureInfo.InvariantCulture, $"{_matchIndex + 1} of {_matches.Count}");
    }

    // ----- Status -----

    private void ShowFailure(Exception exception)
    {
        StatusText.Text = $"could not start a shell: {exception.Message}";
    }

    private void UpdateStatus()
    {
        if (_activeTab is not { } tab)
        {
            StatusText.Text = "starting";
            return;
        }

        var size = tab.Terminal.Size;
        var scrollback = tab.Terminal.Scrollback?.Count ?? 0;
        var state = tab.IsRunning ? "running" : "stopped";
        var selection = tab.View.HasSelection ? "   selection" : string.Empty;
        var scrolled = tab.Terminal.IsScrolledBack
            ? $"   scrolled back {tab.Terminal.ViewportOffset}"
            : string.Empty;

        StatusText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{size.Columns}x{size.Rows}   {scrollback} lines of history   {_tabs.Count} tab(s)   " +
            $"{_theme.Name} {_fontSize:0.#}pt   {state}{scrolled}{selection}");
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
