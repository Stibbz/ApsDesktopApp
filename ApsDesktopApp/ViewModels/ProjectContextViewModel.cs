using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using ApsDesktopApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApsDesktopApp.ViewModels;

// Shared singleton ViewModel that owns the "current project" selection.
// Tools that work on a single project inject this and bind to SelectedProject.
// Selection is persisted in AppSettings so it survives restarts.
public partial class ProjectContextViewModel : ObservableObject
{
    private const string LogCategory = "ProjectContext";

    private readonly ApsDataService _data;
    private readonly AppLogger _log;

    // Flat list of all hub/project combinations; backing the unified ComboBox.
    public ObservableCollection<ProjectEntry> AllProjects { get; } = new();

    // Grouped view used by the menu-bar ComboBox: hubs are non-selectable headers,
    // projects are the selectable items indented beneath each hub.
    public ICollectionView GroupedProjects { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    private ProjectEntry? _selectedProject;

    [ObservableProperty]
    private bool _isLoading;

    public bool HasProject => SelectedProject is not null;

    public ProjectContextViewModel(ApsDataService data, AppLogger log)
    {
        _data = data;
        _log  = log;

        var cvs = new CollectionViewSource { Source = AllProjects };
        cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProjectEntry.HubName)));
        GroupedProjects = cvs.View;
    }

    partial void OnSelectedProjectChanged(ProjectEntry? value)
    {
        if (value is null) return;
        var settings = AppSettings.Load();
        settings.LastProjectId = value.ProjectId;
        settings.Save();
    }

    // Load all hubs and their projects, then restore the last selection.
    // Called by MainViewModel after a successful sign-in.
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        AllProjects.Clear();
        SelectedProject = null;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var hubs = await _data.GetHubsAsync(cts.Token);

            var projectTasks = hubs.Select(async h =>
            {
                try
                {
                    var projects = await _data.GetProjectsAsync(h.Id, cts.Token);
                    return projects.Select(p => new ProjectEntry(h.Id, h.Name, p.Id, p.Name))
                                   .ToList();
                }
                catch (Exception ex)
                {
                    _log.Warn(LogCategory, $"Failed to load projects for hub {h.Name}: {ex.Message}");
                    return new List<ProjectEntry>();
                }
            });

            var batches = await Task.WhenAll(projectTasks);
            foreach (var batch in batches)
                foreach (var entry in batch)
                    AllProjects.Add(entry);

            // Restore last selection.
            var settings = AppSettings.Load();
            if (!string.IsNullOrEmpty(settings.LastProjectId))
            {
                var last = AllProjects.FirstOrDefault(e => e.ProjectId == settings.LastProjectId);
                if (last is not null)
                    SelectedProject = last;
            }

            _log.Info(LogCategory, $"Loaded {AllProjects.Count} project(s) across {hubs.Count} hub(s)");
        }
        catch (Exception ex)
        {
            _log.Warn(LogCategory, $"Failed to load hubs: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Reset()
    {
        AllProjects.Clear();
        SelectedProject = null;
    }
}
