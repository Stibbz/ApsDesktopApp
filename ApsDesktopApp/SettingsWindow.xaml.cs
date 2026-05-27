using System.Windows;
using ApsDesktopApp.Services;

namespace ApsDesktopApp;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        ClientIdBox.Text = _settings.ClientId;
        RedirectUriBox.Text = _settings.RedirectUri;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClientId = ClientIdBox.Text.Trim();
        _settings.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
