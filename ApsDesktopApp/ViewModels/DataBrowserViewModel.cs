using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApsDesktopApp.Services;
using ApsDesktopApp.Services.Naming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApsDesktopApp.ViewModels;

// Data Management Browser tool: hub/project/folder tree, file details grid,
// version-history inspector, and naming-convention check. Loads hubs on first
// activation; clears on disconnect (IToolLifecycle).
public partial class DataBrowserViewModel : ObservableObject, IToolLifecycle
{
    private readonly ApsDataService _data;
    private readonly NamingRuleEngine _namingRules;
    private readonly FileConverterViewModel _fileConverter;

    // Raised when the user chooses to convert the selected file. The View
    // handles this by opening ConvertFileWindow (WPF concern stays in the view).
    public event Action? ConvertFileRequested;

    public DataBrowserViewModel(
        ApsDataService data,
        NamingRuleEngine namingRules,
        FileConverterViewModel fileConverter)
    {
        _data = data;
        _namingRules = namingRules;
        _fileConverter = fileConverter;
    }

    // Exposed so DataBrowserView.xaml.cs can pass it as DataContext to the dialog.
    public FileConverterViewModel FileConverter => _fileConverter;

    // Hubs, each with the projects it contains, shown in the tree.
    public ObservableCollection<HubNode> Hubs { get; } = new();

    [ObservableProperty]
    private bool _isLoadingProjects;

    // Empty-state guidance (most common cause: app not provisioned on the account).
    [ObservableProperty]
    private string _projectsStatus = string.Empty;

    // Files in the currently selected folder, shown in the details grid.
    public ObservableCollection<FileRow> Files { get; } = new();

    [ObservableProperty]
    private string _selectedFolderName = string.Empty;

    [ObservableProperty]
    private bool _isLoadingFiles;

    [ObservableProperty]
    private string _filesStatus = string.Empty;

    // The file selected in the grid; selecting one loads its version history.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenConverterCommand))]
    private FileRow? _selectedFile;

    public ObservableCollection<VersionRow> SelectedFileVersions { get; } = new();

    [ObservableProperty]
    private bool _isLoadingVersions;

    [ObservableProperty]
    private string _versionsStatus = string.Empty;

    // Naming-convention violations for the files in the current folder.
    public ObservableCollection<NamingViolation> NamingViolations { get; } = new();

    [ObservableProperty]
    private string _namingStatus = string.Empty;

    // IToolLifecycle: load hubs the first time the tool is opened.
    public async Task ActivateAsync()
    {
        if (Hubs.Count == 0)
            await RefreshProjectsAsync();
    }

    // IToolLifecycle: wipe everything on disconnect.
    public void Reset()
    {
        Hubs.Clear();
        Files.Clear();
        SelectedFile = null;
        SelectedFolderName = string.Empty;
        FilesStatus = string.Empty;
        ProjectsStatus = string.Empty;
        NamingViolations.Clear();
        NamingStatus = string.Empty;
        CheckNamingCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task RefreshProjectsAsync()
    {
        IsLoadingProjects = true;
        ProjectsStatus = string.Empty;
        Hubs.Clear();
        Files.Clear();
        SelectedFolderName = string.Empty;
        FilesStatus = string.Empty;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var hubs = await _data.GetHubsAsync(cts.Token);

            var projectCount = 0;
            foreach (var hub in hubs)
            {
                var projects = await _data.GetProjectsAsync(hub.Id, cts.Token);
                projectCount += projects.Count;
                var nodes = new List<ProjectNode>(projects.Count);
                foreach (var project in projects)
                    nodes.Add(new ProjectNode(project, hub.Id));
                Hubs.Add(new HubNode(hub, nodes));
            }

            if (hubs.Count == 0)
                ProjectsStatus =
                    "No hubs returned. APS accepted your sign-in, but this app's "
                    + "Client ID is not provisioned on any account. An account admin "
                    + "must add it under ACC/BIM 360 > Account Admin > Settings > "
                    + "Custom Integrations.";
            else if (projectCount == 0)
                ProjectsStatus = "Connected, but no projects were found in your hub(s).";
        }
        catch (Exception ex)
        {
            ProjectsStatus = $"Could not load projects: {ex.Message}";
        }
        finally
        {
            IsLoadingProjects = false;
        }
    }

    // Loads a project's top-level folders on first expand (placeholder swap).
    public async Task LoadTopFoldersAsync(ProjectNode project)
    {
        if (project.IsLoaded)
            return;
        project.IsLoaded = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var folders = await _data.GetTopFoldersAsync(project.HubId, project.ProjectId, cts.Token);
            project.Folders.Clear();
            foreach (var folder in folders)
                project.Folders.Add(new FolderNode(folder, project.ProjectId));
        }
        catch
        {
            project.Folders.Clear();
            project.IsLoaded = false; // allow a retry on next expand
        }
    }

    // Loads a folder's subfolders on first expand (same placeholder swap).
    public async Task LoadSubFoldersAsync(FolderNode folder)
    {
        if (folder.IsLoaded)
            return;
        folder.IsLoaded = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var contents = await _data.GetFolderContentsAsync(folder.ProjectId, folder.FolderId, cts.Token);
            folder.Children.Clear();
            foreach (var sub in contents.Folders)
                folder.Children.Add(new FolderNode(sub, folder.ProjectId));
        }
        catch
        {
            folder.Children.Clear();
            folder.IsLoaded = false;
        }
    }

    // Loads the selected folder's files into the details grid.
    public async Task ShowFolderFilesAsync(FolderNode folder)
    {
        SelectedFolderName = folder.Name;
        IsLoadingFiles = true;
        FilesStatus = string.Empty;
        SelectedFile = null;
        Files.Clear();
        NamingViolations.Clear();
        NamingStatus = string.Empty;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var contents = await _data.GetFolderContentsAsync(folder.ProjectId, folder.FolderId, cts.Token);
            foreach (var file in contents.Files)
                Files.Add(new FileRow(file, folder.ProjectId));

            if (Files.Count == 0)
                FilesStatus = "No files in this folder.";
        }
        catch (Exception ex)
        {
            FilesStatus = $"Could not load files: {ex.Message}";
        }
        finally
        {
            IsLoadingFiles = false;
            CheckNamingCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanOpenConverter() => SelectedFile is not null;

    [RelayCommand(CanExecute = nameof(CanOpenConverter))]
    private void OpenConverter()
    {
        if (SelectedFile is null) return;
        _fileConverter.Reset();
        _fileConverter.FileName = SelectedFile.Name;
        _fileConverter.VersionUrn = SelectedFile.TipVersionUrn;
        ConvertFileRequested?.Invoke();
    }

    private bool CanCheckNaming() => Files.Count > 0;

    [RelayCommand(CanExecute = nameof(CanCheckNaming))]
    private void CheckNaming()
    {
        NamingViolations.Clear();
        var violations = _namingRules.Check(Files.Select(f => f.Name));
        foreach (var violation in violations)
            NamingViolations.Add(violation);

        NamingStatus = violations.Count == 0
            ? $"All {Files.Count} file(s) conform to the naming convention."
            : $"{violations.Count} naming issue(s) across {Files.Count} file(s).";
    }

    // Auto-loads version history when the grid selection changes (null clears).
    partial void OnSelectedFileChanged(FileRow? value)
    {
        _ = LoadVersionsAsync(value);
    }

    private async Task LoadVersionsAsync(FileRow? file)
    {
        SelectedFileVersions.Clear();
        VersionsStatus = string.Empty;

        if (file is null)
            return;

        IsLoadingVersions = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var versions = await _data.GetItemVersionsAsync(file.ProjectId, file.ItemId, cts.Token);
            foreach (var version in versions)
                SelectedFileVersions.Add(new VersionRow(version));

            if (SelectedFileVersions.Count == 0)
                VersionsStatus = "No version history available.";
        }
        catch (Exception ex)
        {
            VersionsStatus = $"Could not load versions: {ex.Message}";
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }
}
