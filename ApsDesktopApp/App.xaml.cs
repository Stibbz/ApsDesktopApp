using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ApsDesktopApp.Services;
using ApsDesktopApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ApsDesktopApp;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // App.xaml's <Application.Resources> are NOT compiled/loaded because this
        // App.xaml has no StartupUri (we create MainWindow via DI instead). Register
        // application-scoped resources here so {StaticResource ...} lookups resolve.
        Resources.Add("BoolToVisibility", new BooleanToVisibilityConverter());

        var services = new ServiceCollection();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<TokenStorage>();
        services.AddSingleton<ApsAuthService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();
    }
}
