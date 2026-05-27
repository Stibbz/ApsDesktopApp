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
        // Merge the shared theme via a pack URI (the SDX Tools pattern), then add the
        // keyed converter on top.
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/ApsDesktopApp;component/Styles/AppStyles.xaml")
        });
        Resources.Add("BoolToVisibility", new BooleanToVisibilityConverter());
        Resources.Add("StringToVisibility", new Converters.StringToVisibilityConverter());

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
