using System.Windows;
using System.Windows.Controls;
using ApsDesktopApp.Services;

namespace ApsDesktopApp;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SecretStorage _secretStorage;

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        _secretStorage = new SecretStorage();

        ClientIdBox.Text = _settings.ClientId;
        RedirectUriBox.Text = _settings.RedirectUri;
        SelectRegion(_settings.Region);

        MdClientIdBox.Text = _settings.ModelDerivativeClientId;
        // Show a placeholder so the user knows a secret is already saved.
        if (_secretStorage.HasSecret)
            MdClientSecretBox.Password = "placeholder-not-changed";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClientId = ClientIdBox.Text.Trim();
        _settings.Region = (RegionBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "US";
        _settings.ModelDerivativeClientId = MdClientIdBox.Text.Trim();
        _settings.Save();

        // Only overwrite the stored secret if the user typed a new value.
        // The placeholder password "placeholder-not-changed" means leave as-is.
        var typedSecret = MdClientSecretBox.Password;
        if (typedSecret != "placeholder-not-changed")
        {
            if (string.IsNullOrWhiteSpace(typedSecret))
                _secretStorage.Clear();
            else
                _secretStorage.Save(typedSecret);
        }

        DialogResult = true;
        Close();
    }

    private void SelectRegion(string region)
    {
        // Settings saved before the APAC->AUS rename still carry the old value.
        if (region == "APAC")
            region = "AUS";
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