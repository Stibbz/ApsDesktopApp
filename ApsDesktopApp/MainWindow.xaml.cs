using System.Windows;
using ApsDesktopApp.Services;
using ApsDesktopApp.ViewModels;

namespace ApsDesktopApp;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ApsAuthService _auth;

    public MainWindow(MainViewModel viewModel, ApsAuthService auth)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _auth = auth;

        _viewModel.ConfigurationRequested += OnConfigurationRequested;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private bool OpenSettings()
    {
        var dialog = new SettingsWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _auth.ReloadSettings();
            return true;
        }
        return false;
    }

    private void OnConfigurationRequested(object? sender, System.EventArgs e)
    {
        if (OpenSettings() && _auth.IsConfigured)
            Dispatcher.BeginInvoke(() =>
            {
                if (_viewModel.ConnectCommand.CanExecute(null))
                    _viewModel.ConnectCommand.Execute(null);
            });
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "APS Desktop App\n\nA platform for BIM coordination tools built on Autodesk APS.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
