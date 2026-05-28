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
        Resources.Add("InverseBoolToVisibility", new Converters.InverseBoolToVisibilityConverter());
        Resources.Add("StringToVisibility", new Converters.StringToVisibilityConverter());

        var services = new ServiceCollection();

        // Plain HttpClient for the unauthenticated token endpoints; ApsAuthService
        // resolves this one (no ApsAuthHandler, so the refresh call can't recurse).
        services.AddSingleton<HttpClient>();
        services.AddSingleton<TokenStorage>();
        services.AddSingleton<ApsAuthService>();
        services.AddSingleton<ApsAuthHandler>();

        // Keyed "data" HttpClient: wrapped with ApsAuthHandler (injects bearer,
        // retries on 401). Shared by every authenticated service. The handler
        // resolves ApsAuthService lazily, so this is not a construction cycle.
        services.AddKeyedSingleton<HttpClient>("data", (sp, _) =>
        {
            var handler = sp.GetRequiredService<ApsAuthHandler>();
            handler.InnerHandler = new HttpClientHandler();
            return new HttpClient(handler);
        });

        services.AddSingleton<ApsDataService>(sp => new ApsDataService(
            sp.GetRequiredKeyedService<HttpClient>("data"),
            sp.GetRequiredService<ApsAuthService>()));
        services.AddSingleton<ModelDerivativeService>(sp => new ModelDerivativeService(
            sp.GetRequiredKeyedService<HttpClient>("data")));

        // Naming-convention rules: register each INamingRule; the engine receives
        // them all via IEnumerable<INamingRule>. Add more rules by registering
        // more implementations here.
        services.AddSingleton<Services.Naming.INamingRule, Services.Naming.SegmentNamingRule>();
        services.AddSingleton<Services.Naming.NamingRuleEngine>();

        // Tools (one ViewModel each) + the shell that hosts them.
        services.AddSingleton<DataBrowserViewModel>();
        services.AddSingleton<ModelDerivativeViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();
    }
}
