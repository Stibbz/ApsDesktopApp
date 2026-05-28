using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ApsDesktopApp.ViewModels;

namespace ApsDesktopApp.Views;

public partial class DataBrowserView : UserControl
{
    public DataBrowserView()
    {
        InitializeComponent();

        // TreeView SelectedItem is read-only (not bindable), and per-node expand
        // needs the container event, so we bridge both to the ViewModel here.
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

    // Opens the convert dialog with the selected file pre-filled.
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

    // Lazy-load children the first time a project or folder is expanded.
    private async void OnTreeItemExpanded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || e.OriginalSource is not TreeViewItem item)
            return;

        switch (item.DataContext)
        {
            case ProjectNode project:
                await ViewModel.LoadTopFoldersAsync(project);
                break;
            case FolderNode folder when !folder.IsPlaceholder:
                await ViewModel.LoadSubFoldersAsync(folder);
                break;
        }
    }

    // Selecting a folder loads its files into the details grid.
    private async void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel is not null && e.NewValue is FolderNode folder && !folder.IsPlaceholder)
            await ViewModel.ShowFolderFilesAsync(folder);
    }

    // Right-clicking a row selects it (WPF DataGrid doesn't do this by default),
    // so the context menu always reflects the row the user clicked on.
    private void OnFileGridRightClick(object sender, MouseButtonEventArgs e)
    {
        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null and not DataGridRow)
            hit = VisualTreeHelper.GetParent(hit);

        if (hit is DataGridRow row)
            row.IsSelected = true;
    }
}
