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
        Resources.Add("InverseBoolToVisibility", new Views.Converters.InverseBoolToVisibilityConverter());
        Resources.Add("InverseBool", new Views.Converters.InverseBoolConverter());
        Resources.Add("StringToVisibility", new Views.Converters.StringToVisibilityConverter());

        var services = new ServiceCollection();

        services.AddSingleton<AppLogger>();

        // Plain HttpClient for the unauthenticated token endpoints; ApsAuthService
        // resolves this one (no ApsAuthHandler, so the refresh call can't recurse).
        // Plain HttpClient shared by unauthenticated token endpoints (3-legged
        // exchange/refresh and 2-legged client credentials). Must not carry any
        // auth handler or refresh calls would recurse.
        services.AddSingleton<HttpClient>();
        services.AddSingleton<TokenStorage>();
        services.AddSingleton<ApsAuthService>();
        services.AddSingleton<ApsAuthHandler>();

        // Keyed "data" HttpClient: 3-legged bearer via ApsAuthHandler.
        services.AddKeyedSingleton<HttpClient>("data", (sp, _) =>
        {
            var handler = sp.GetRequiredService<ApsAuthHandler>();
            handler.InnerHandler = new HttpClientHandler();
            return new HttpClient(handler);
        });

        // Keyed "modelderivative" HttpClient: 2-legged bearer via TwoLeggedAuthHandler.
        services.AddSingleton<SecretStorage>();
        services.AddSingleton<TwoLeggedTokenService>(sp => new TwoLeggedTokenService(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<SecretStorage>()));
        services.AddSingleton<TwoLeggedAuthHandler>();
        services.AddKeyedSingleton<HttpClient>("modelderivative", (sp, _) =>
        {
            var handler = sp.GetRequiredService<TwoLeggedAuthHandler>();
            handler.InnerHandler = new HttpClientHandler();
            return new HttpClient(handler);
        });

        services.AddSingleton<ApsDataService>(sp => new ApsDataService(
            sp.GetRequiredKeyedService<HttpClient>("data"),
            sp.GetRequiredService<ApsAuthService>()));
        services.AddSingleton<AccIssuesService>(sp => new AccIssuesService(
            sp.GetRequiredKeyedService<HttpClient>("data"),
            sp.GetRequiredService<ApsAuthService>(),
            sp.GetRequiredService<AppLogger>()));
        services.AddSingleton<AccMembersService>(sp => new AccMembersService(
            sp.GetRequiredKeyedService<HttpClient>("data"),
            sp.GetRequiredService<ApsAuthService>(),
            sp.GetRequiredService<AppLogger>()));
        services.AddSingleton<ModelDerivativeService>(sp => new ModelDerivativeService(
            sp.GetRequiredKeyedService<HttpClient>("modelderivative"),
            sp.GetRequiredService<AppLogger>()));

        // Naming-convention rules: register each INamingRule; the engine receives
        // them all via IEnumerable<INamingRule>. Add more rules by registering
        // more implementations here.
        services.AddSingleton<Services.Naming.INamingRule, Services.Naming.SegmentNamingRule>();
        services.AddSingleton<Services.Naming.NamingRuleEngine>();

        // Tools (one ViewModel each) + the shell that hosts them.
        services.AddSingleton<ProjectContextViewModel>();
        services.AddSingleton<DataBrowserViewModel>();
        services.AddSingleton<FileConverterViewModel>();
        services.AddSingleton<IssuesViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        // Static fallback logger for AppSettings.Load(), which is deliberately
        // not DI-injected (see CLAUDE.md) and so cannot receive the logger.
        AppSettings.Logger = Services.GetRequiredService<AppLogger>();

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();
    }
}
