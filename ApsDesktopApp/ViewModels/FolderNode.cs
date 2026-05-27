using System.Collections.ObjectModel;
using ApsDesktopApp.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApsDesktopApp.ViewModels;

// A folder in the project directory tree. Children are loaded lazily: a folder
// starts with a single "Loading..." placeholder so the TreeView draws an
// expander arrow, and the placeholder is swapped for real subfolders the first
// time the node is expanded (see MainViewModel.LoadFolderContentsAsync).
public partial class FolderNode : ObservableObject
{
    public FolderNode(FolderEntry entry, string projectId)
    {
        Name = entry.Name;
        FolderId = entry.Id;
        ProjectId = projectId;
        Children.Add(NewPlaceholder());
    }

    // Placeholder ctor: a non-loadable stub node that only exists to make the
    // parent show an expander before its real children are fetched.
    private FolderNode()
    {
        Name = "Loading...";
        IsPlaceholder = true;
    }

    public string Name { get; }
    public string FolderId { get; } = string.Empty;
    public string ProjectId { get; } = string.Empty;
    public bool IsPlaceholder { get; }

    // True once the real subfolders have been fetched, so we don't refetch on
    // every re-expand.
    public bool IsLoaded { get; set; }

    public ObservableCollection<FolderNode> Children { get; } = new();

    public static FolderNode NewPlaceholder() => new();
}
