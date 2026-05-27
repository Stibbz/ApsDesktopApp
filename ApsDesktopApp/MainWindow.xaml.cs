using System.Windows;
using System.Windows.Controls;
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

        // Connecting without a Client ID opens Settings instead of a popup.
        _viewModel.ConfigurationRequested += OnConfigurationRequested;

        // TreeView SelectedItem is read-only (not bindable), and per-node expand
        // needs the container event, so we bridge both to the ViewModel here.
        BrowserTree.AddHandler(TreeViewItem.ExpandedEvent,
            new RoutedEventHandler(OnTreeItemExpanded));
    }

    // Lazy-load children the first time a project or folder is expanded.
    private async void OnTreeItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem item)
            return;

        switch (item.DataContext)
        {
            case ProjectNode project:
                await _viewModel.LoadTopFoldersAsync(project);
                break;
            case FolderNode folder when !folder.IsPlaceholder:
                await _viewModel.LoadSubFoldersAsync(folder);
                break;
        }
    }

    // Selecting a folder loads its files into the details grid.
    private async void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderNode folder && !folder.IsPlaceholder)
            await _viewModel.ShowFolderFilesAsync(folder);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    // Opens the Settings dialog and reloads auth config if the user saved.
    // Returns true when settings were saved.
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
        // User clicked Connect without a Client ID set. Send them to Settings,
        // then auto-continue the sign-in if they saved a valid configuration.
        // Queue the retry on the dispatcher: ConnectAsync is still the running
        // command while this event fires, so a direct re-invoke would be a no-op.
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
