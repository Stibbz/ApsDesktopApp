using System.Windows;
using System.Windows.Controls;
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
        SelectRegion(_settings.Region);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClientId = ClientIdBox.Text.Trim();
        _settings.Region = (RegionBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "US";
        _settings.Save();
        DialogResult = true;
        Close();
    }

    // Selects the combo item whose Tag matches the saved region code.
    private void SelectRegion(string region)
    {
        foreach (ComboBoxItem item in RegionBox.Items)
        {
            if ((item.Tag as string) == region)
            {
                RegionBox.SelectedItem = item;
                return;
            }
        }
        RegionBox.SelectedIndex = 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
