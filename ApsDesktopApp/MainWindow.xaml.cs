using System.Windows;
using ApsDesktopApp.Services;
using ApsDesktopApp.ViewModels;

namespace ApsDesktopApp;

public partial class MainWindow : Window
{
    private readonly ApsAuthService _auth;

    public MainWindow(MainViewModel viewModel, ApsAuthService auth)
    {
        InitializeComponent();
        DataContext = viewModel;
        _auth = auth;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow { Owner = this };
        if (dialog.ShowDialog() == true)
            _auth.ReloadSettings();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "APS Desktop App\n\nA platform for BIM coordination tools built on Autodesk APS.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
