using System.Collections.ObjectModel;
using ApsDesktopApp.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApsDesktopApp.ViewModels;

// A project in the hub/project tree. Carries the HubId alongside the ProjectId
// because the top-folders endpoint is addressed under the hub, not the project.
// Top folders load lazily on first expand, same placeholder trick as FolderNode.
public partial class ProjectNode : ObservableObject
{
    public ProjectNode(Project project, string hubId)
    {
        Name = project.Name;
        ProjectId = project.Id;
        HubId = hubId;
        Folders.Add(FolderNode.NewPlaceholder());
    }

    public string Name { get; }
    public string ProjectId { get; }
    public string HubId { get; }

    public bool IsLoaded { get; set; }

    public ObservableCollection<FolderNode> Folders { get; } = new();
}
