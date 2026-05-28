using System.Windows;
using System.Windows.Controls;
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
    }

    private DataBrowserViewModel? ViewModel => DataContext as DataBrowserViewModel;

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
}
