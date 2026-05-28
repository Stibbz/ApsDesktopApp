using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ApsDesktopApp.Services;
using ApsDesktopApp.ViewModels;

namespace ApsDesktopApp;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ApsAuthService _auth;
    private readonly TwoLeggedTokenService _twoLegged;

    public MainWindow(MainViewModel viewModel, ApsAuthService auth, TwoLeggedTokenService twoLegged)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _auth = auth;
        _twoLegged = twoLegged;

        _viewModel.ConfigurationRequested += OnConfigurationRequested;

        // Apply dark title bar once the HWND exists.
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    // Tells DWM to use the dark (immersive) caption colour.
    // Attribute 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 1903+, Windows 11).
    private void ApplyDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int value = 1;
        DwmSetWindowAttribute(hwnd, 20, ref value, Marshal.SizeOf(value));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private bool OpenSettings()
    {
        var dialog = new SettingsWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _auth.ReloadSettings();
            _twoLegged.ReloadSettings();
            return true;
        }
        return false;
    }

    private void OnConfigurationRequested(object? sender, EventArgs e)
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
