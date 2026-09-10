using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NovaTerminal.App.Views;

namespace NovaTerminal.App;

/// <summary>
/// The Avalonia application object. It owns the service provider built by the composition root and
/// hands dependencies to the views it creates, so no view has to locate services for itself.
/// </summary>
public partial class App : Application
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the application with the services it should resolve views from.</summary>
    public App(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = ActivatorUtilities.CreateInstance<MainWindow>(_services);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
