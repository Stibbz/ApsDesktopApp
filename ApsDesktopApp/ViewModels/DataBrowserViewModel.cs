using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApsDesktopApp.Models;
using ApsDesktopApp.Services;
using ApsDesktopApp.Services.Naming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApsDesktopApp.ViewModels;

// Data Management Browser: folder tree + file/folder content grid + version history.
// Navigation starts at the top folders of the globally-selected project; switching
// projects in the menu-bar picker refreshes the tree automatically.
public partial class DataBrowserViewModel : ObservableObject, IToolLifecycle
{
    private const string LogCategory = "DataBrowser";

    private readonly ApsDataService _data;
    private readonly NamingRuleEngine _namingRules;
    private readonly FileConverterViewModel _fileConverter;
    private readonly ProjectContextViewModel _projectContext;
    private readonly AppLogger _log;

    // Latest-call-wins guards: each load family cancels its predecessor so a
    // slow older request can never repopulate the UI after a newer one (e.g.
    // rapid project switching or fast file selection).
    private CancellationTokenSource? _foldersCts;
    private CancellationTokenSource? _filesCts;
    private CancellationTokenSource? _versionsCts;

    // Raised when the user chooses to convert the selected file.
    public event Action? ConvertFileRequested;

    public DataBrowserViewModel(
        ApsDataService data,
        NamingRuleEngine namingRules,
        FileConverterViewModel fileConverter,
        ProjectContextViewModel projectContext,
        AppLogger log)
    {
        _data           = data;
        _namingRules    = namingRules;
        _fileConverter  = fileConverter;
        _projectContext = projectContext;
        _log            = log;

        _projectContext.PropertyChanged += OnProjectContextChanged;
    }

    // Cancels the previous load of a family and installs a fresh 30s-capped
    // token source in its place.
    private static CancellationTokenSource ReplaceCts(ref CancellationTokenSource? slot)
    {
        slot?.Cancel();
        slot?.Dispose();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        slot = cts;
        return cts;
    }

    // Exposed so DataBrowserView.xaml.cs can pass it as DataContext to the dialog.
    public FileConverterViewModel FileConverter => _fileConverter;

    // Top-level folders of the selected project, shown in the left tree.
    public ObservableCollection<FolderNode> Folders { get; } = new();

    // Current folder path: each entry is a FolderNode in the navigation stack.
    // The first entry is the top-level (tree) folder; subsequent entries are
    // subfolders navigated via double-click in the content panel.
    public ObservableCollection<FolderNode> NavigationPath { get; } = new();

    [ObservableProperty]
    private bool _isLoadingFolders;

    [ObservableProperty]
    private string _projectsStatus = string.Empty;

    // Content panel: subfolders and files of the current folder.
    public ObservableCollection<FileRow> Files { get; } = new();

    [ObservableProperty]
    private bool _isLoadingFiles;

    [ObservableProperty]
    private string _filesStatus = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenConverterCommand))]
    private FileRow? _selectedFile;

    public ObservableCollection<VersionRow> SelectedFileVersions { get; } = new();

    [ObservableProperty]
    private bool _isLoadingVersions;

    [ObservableProperty]
    private string _versionsStatus = string.Empty;

    public ObservableCollection<NamingViolation> NamingViolations { get; } = new();

    [ObservableProperty]
    private string _namingStatus = string.Empty;

    // IToolLifecycle -------------------------------------------------------

    public async Task ActivateAsync()
    {
        if (_projectContext.SelectedProject is not null && Folders.Count == 0)
            await LoadTopFoldersAsync();
    }

    public void Reset()
    {
        _foldersCts?.Cancel();
        _filesCts?.Cancel();
        _versionsCts?.Cancel();
        Folders.Clear();
        NavigationPath.Clear();
        Files.Clear();
        SelectedFile = null;
        FilesStatus = string.Empty;
        ProjectsStatus = string.Empty;
        NamingViolations.Clear();
        NamingStatus = string.Empty;
        CheckNamingCommand.NotifyCanExecuteChanged();
    }

    // React to project changes from the menu-bar picker -----------------------

    private void OnProjectContextChanged(
        object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProjectContextViewModel.SelectedProject)) return;

        // Cancel any in-flight loads so a slow response for the OLD project can
        // never repopulate the collections we are about to clear.
        _foldersCts?.Cancel();
        _filesCts?.Cancel();
        _versionsCts?.Cancel();

        Folders.Clear();
        NavigationPath.Clear();
        Files.Clear();
        SelectedFile = null;
        FilesStatus = string.Empty;
        NamingViolations.Clear();
        NamingStatus = string.Empty;

        if (_projectContext.SelectedProject is null)
        {
            ProjectsStatus =
                "Select a project from the project picker to start browsing.";
            return;
        }

        LoadTopFoldersAsync().LogFaults(_log, LogCategory);
    }

    [RelayCommand]
    private async Task RefreshFoldersAsync()
    {
        if (_projectContext.SelectedProject is null) return;
        Folders.Clear();
        NavigationPath.Clear();
        Files.Clear();
        FilesStatus = string.Empty;
        NamingViolations.Clear();
        NamingStatus = string.Empty;
        await LoadTopFoldersAsync();
    }

    private async Task LoadTopFoldersAsync()
    {
        var project = _projectContext.SelectedProject;
        if (project is null) return;

        var cts = ReplaceCts(ref _foldersCts);

        IsLoadingFolders = true;
        ProjectsStatus = string.Empty;
        try
        {
            var folders = await _data.GetTopFoldersAsync(
                project.HubId, project.ProjectId, cts.Token);
            if (cts.Token.IsCancellationRequested) return; // superseded

            Folders.Clear();
            foreach (var folder in folders)
                Folders.Add(new FolderNode(folder, project.ProjectId));

            if (Folders.Count == 0)
                ProjectsStatus = "No folders found in this project.";
        }
        catch (OperationCanceledException) when (cts != _foldersCts)
        {
            // A newer load took over; leave the UI to it.
        }
        catch (OperationCanceledException)
        {
            ProjectsStatus = "Loading folders timed out.";
        }
        catch (Exception ex)
        {
            ProjectsStatus = $"Could not load folders: {ex.Message}";
        }
        finally
        {
            if (cts == _foldersCts)
                IsLoadingFolders = false;
        }
    }

    // Tree navigation: load subfolders lazily on expand ----------------------

    public async Task LoadSubFoldersAsync(FolderNode folder)
    {
        if (folder.IsLoaded) return;
        folder.IsLoaded = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var contents = await _data.GetFolderContentsAsync(
                folder.ProjectId, folder.FolderId, cts.Token);
            folder.Children.Clear();
            foreach (var sub in contents.Folders)
                folder.Children.Add(new FolderNode(sub, folder.ProjectId));
        }
        catch (Exception ex)
        {
            // Reset IsLoaded so collapsing and re-expanding retries the fetch,
            // and tell the user -- an empty node is indistinguishable from an
            // empty folder otherwise.
            folder.Children.Clear();
            folder.IsLoaded = false;
            _log.Warn(LogCategory, $"Expanding folder '{folder.Name}' failed: {ex.Message}");
            ProjectsStatus = $"Could not expand '{folder.Name}': {ex.Message}";
        }
    }

    // Content panel navigation -----------------------------------------------

    // Called by tree selection: reset the breadcrumb to just this folder.
    public async Task ShowFolderContentsAsync(FolderNode folder)
    {
        NavigationPath.Clear();
        NavigationPath.Add(folder);
        await LoadFolderContentsInternalAsync(folder);
    }

    // Called by double-click on a folder row: push one level deeper.
    public async Task NavigateIntoFolderAsync(FileRow row)
    {
        if (!row.IsFolder || string.IsNullOrEmpty(row.FolderId)) return;
        var entry = new FolderEntry(row.FolderId, row.Name);
        var node  = new FolderNode(entry, row.ProjectId);
        NavigationPath.Add(node);
        await LoadFolderContentsInternalAsync(node);
    }

    // Called by breadcrumb click: trim everything after the target and reload.
    [RelayCommand]
    private async Task NavigateToPathEntryAsync(FolderNode node)
    {
        var idx = NavigationPath.IndexOf(node);
        if (idx < 0) return;
        // Pop all entries that are deeper than the clicked one.
        while (NavigationPath.Count > idx + 1)
            NavigationPath.RemoveAt(NavigationPath.Count - 1);
        await LoadFolderContentsInternalAsync(node);
    }

    // Core: fetch and populate the content panel for any FolderNode.
    private async Task LoadFolderContentsInternalAsync(FolderNode folder)
    {
        var cts = ReplaceCts(ref _filesCts);

        IsLoadingFiles = true;
        FilesStatus = string.Empty;
        SelectedFile = null;
        Files.Clear();
        NamingViolations.Clear();
        NamingStatus = string.Empty;
        try
        {
            var contents = await _data.GetFolderContentsAsync(
                folder.ProjectId, folder.FolderId, cts.Token);
            if (cts.Token.IsCancellationRequested) return; // superseded

            // Subfolders first (Explorer convention), then files.
            foreach (var sub in contents.Folders)
                Files.Add(new FileRow(sub, folder.ProjectId));
            foreach (var file in contents.Files)
                Files.Add(new FileRow(file, folder.ProjectId));

            if (Files.Count == 0)
                FilesStatus = "This folder is empty.";
        }
        catch (OperationCanceledException) when (cts != _filesCts)
        {
            // A newer navigation took over; leave the UI to it.
        }
        catch (OperationCanceledException)
        {
            FilesStatus = "Loading contents timed out.";
        }
        catch (Exception ex)
        {
            FilesStatus = $"Could not load contents: {ex.Message}";
        }
        finally
        {
            if (cts == _filesCts)
            {
                IsLoadingFiles = false;
                CheckNamingCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // Commands ---------------------------------------------------------------

    private bool CanOpenConverter() =>
        SelectedFile is { IsFolder: false };

    [RelayCommand(CanExecute = nameof(CanOpenConverter))]
    private void OpenConverter()
    {
        if (SelectedFile is null || SelectedFile.IsFolder) return;
        _fileConverter.Reset();
        _fileConverter.FileName    = SelectedFile.Name;
        _fileConverter.VersionUrn  = SelectedFile.TipVersionUrn;
        ConvertFileRequested?.Invoke();
    }

    private bool CanCheckNaming() => Files.Any(f => !f.IsFolder);

    [RelayCommand(CanExecute = nameof(CanCheckNaming))]
    private void CheckNaming()
    {
        NamingViolations.Clear();
        var violations = _namingRules.Check(
            Files.Where(f => !f.IsFolder).Select(f => f.Name));
        foreach (var violation in violations)
            NamingViolations.Add(violation);

        NamingStatus = violations.Count == 0
            ? $"All {Files.Count(f => !f.IsFolder)} file(s) conform to the naming convention."
            : $"{violations.Count} naming issue(s).";
    }

    partial void OnSelectedFileChanged(FileRow? value)
    {
        if (value?.IsFolder == true)
        {
            SelectedFileVersions.Clear();
            VersionsStatus = string.Empty;
            return;
        }
        LoadVersionsAsync(value).LogFaults(_log, LogCategory);
    }

    private async Task LoadVersionsAsync(FileRow? file)
    {
        SelectedFileVersions.Clear();
        VersionsStatus = string.Empty;
        if (file is null) return;

        var cts = ReplaceCts(ref _versionsCts);

        IsLoadingVersions = true;
        try
        {
            var versions = await _data.GetItemVersionsAsync(
                file.ProjectId, file.ItemId, cts.Token);
            if (cts.Token.IsCancellationRequested) return; // superseded

            foreach (var version in versions)
                SelectedFileVersions.Add(new VersionRow(version));

            if (SelectedFileVersions.Count == 0)
                VersionsStatus = "No version history available.";
        }
        catch (OperationCanceledException) when (cts != _versionsCts)
        {
            // A newer selection took over; leave the UI to it.
        }
        catch (OperationCanceledException)
        {
            VersionsStatus = "Loading versions timed out.";
        }
        catch (Exception ex)
        {
            VersionsStatus = $"Could not load versions: {ex.Message}";
        }
        finally
        {
            if (cts == _versionsCts)
                IsLoadingVersions = false;
        }
    }
}
