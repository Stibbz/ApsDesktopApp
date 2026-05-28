using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ApsDesktopApp.ViewModels;

namespace ApsDesktopApp.Views;

public partial class DataBrowserView : UserControl
{
    private LogViewerWindow? _logWindow;

    public DataBrowserView()
    {
        InitializeComponent();

        BrowserTree.AddHandler(TreeViewItem.ExpandedEvent,
            new RoutedEventHandler(OnTreeItemExpanded));

        DataContextChanged += OnDataContextChanged;
    }

    private DataBrowserViewModel? ViewModel => DataContext as DataBrowserViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DataBrowserViewModel old)
            old.ConvertFileRequested -= OnConvertFileRequested;
        if (e.NewValue is DataBrowserViewModel vm)
            vm.ConvertFileRequested += OnConvertFileRequested;
    }

    private void OnConvertFileRequested()
    {
        if (ViewModel is null) return;
        var dialog = new ConvertFileWindow
        {
            DataContext = ViewModel.FileConverter,
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

    // Lazy-load subfolders the first time a folder node is expanded in the tree.
    private async void OnTreeItemExpanded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || e.OriginalSource is not TreeViewItem item)
            return;

        if (item.DataContext is FolderNode folder && !folder.IsPlaceholder)
            await ViewModel.LoadSubFoldersAsync(folder);
    }

    // Selecting a folder in the tree loads its contents in the right panel.
    private async void OnTreeSelectionChanged(
        object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel is not null && e.NewValue is FolderNode folder && !folder.IsPlaceholder)
            await ViewModel.ShowFolderContentsAsync(folder);
    }

    // Double-clicking a folder row in the content grid navigates into it.
    private async void OnFileGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.SelectedFile is { IsFolder: true } row)
            await ViewModel.NavigateIntoFolderAsync(row);
    }

    // Right-clicking a file row selects it so the context menu always reflects
    // the clicked row (WPF DataGrid doesn't do this by default).
    private void OnFileGridRightClick(object sender, MouseButtonEventArgs e)
    {
        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null and not DataGridRow)
            hit = VisualTreeHelper.GetParent(hit);

        if (hit is DataGridRow row)
            row.IsSelected = true;
    }

    private void ShowLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_logWindow is null)
        {
            _logWindow = new LogViewerWindow
            {
                Owner = Window.GetWindow(this)
            };
            _logWindow.Closed += (_, _) => _logWindow = null;
        }
        _logWindow.Show();
        _logWindow.Activate();
    }
}
