using System.Collections.Generic;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.ViewModels;

// A hub plus the projects it contains, shaped for the hub/project TreeView.
// The hub itself is immutable after load (plain properties), but each project
// is a ProjectNode that lazily grows its own folder subtree.
public class HubNode
{
    public HubNode(Hub hub, IReadOnlyList<ProjectNode> projects)
    {
        Name = hub.Name;
        Id = hub.Id;
        Projects = projects;
    }

    public string Name { get; }
    public string Id { get; }
    public IReadOnlyList<ProjectNode> Projects { get; }
}
