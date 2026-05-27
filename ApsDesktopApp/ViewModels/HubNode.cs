using System.Collections.Generic;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.ViewModels;

// A hub plus the projects it contains, shaped for the hub/project TreeView.
// Immutable after load, so plain properties (no change notification) suffice.
public class HubNode
{
    public HubNode(Hub hub, IReadOnlyList<Project> projects)
    {
        Name = hub.Name;
        Id = hub.Id;
        Projects = projects;
    }

    public string Name { get; }
    public string Id { get; }
    public IReadOnlyList<Project> Projects { get; }
}
