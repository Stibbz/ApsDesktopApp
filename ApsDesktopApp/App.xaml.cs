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
        // Plain HttpClient: the unauthenticated token endpoints use this.
        services.AddSingleton<HttpClient>();
        services.AddSingleton<TokenStorage>();
        services.AddSingleton<ApsAuthHandler>();

        // ApsAuthService gets two clients: the plain one above for token calls,
        // and a data client whose ApsAuthHandler injects the bearer token (and
        // refreshes on 401). The handler resolves ApsAuthService lazily, so this
        // factory wiring is not a construction cycle.
        services.AddSingleton<ApsAuthService>(sp =>
        {
            var handler = sp.GetRequiredService<ApsAuthHandler>();
            handler.InnerHandler = new HttpClientHandler();
            var dataClient = new HttpClient(handler);
            return new ApsAuthService(
                sp.GetRequiredService<HttpClient>(),
                dataClient,
                sp.GetRequiredService<TokenStorage>());
        });

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();
    }
}
