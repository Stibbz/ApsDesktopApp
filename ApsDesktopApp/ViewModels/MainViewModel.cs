using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ApsDesktopApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApsDesktopApp.ViewModels;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected
}

public partial class MainViewModel : ObservableObject
{
    private readonly ApsAuthService _auth;

    // Raised when the user tries to connect before a Client ID is configured.
    // The View handles this by opening the Settings window (no MessageBox).
    public event EventHandler? ConfigurationRequested;

    public MainViewModel(ApsAuthService auth)
    {
        _auth = auth;
        if (_auth.HasStoredToken)
            _ = InitializeFromStoredTokenAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshProjectsCommand))]
    private ConnectionState _state = ConnectionState.Disconnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _userDisplayName = string.Empty;

    // Hubs, each with the projects it contains, shown in the connected panel.
    public ObservableCollection<HubNode> Hubs { get; } = new();

    [ObservableProperty]
    private bool _isLoadingProjects;

    // Empty-state guidance shown when no hubs/projects come back. The most
    // common cause is the APS app not being provisioned on the account.
    [ObservableProperty]
    private string _projectsStatus = string.Empty;

    // Files in the currently selected folder, shown in the details grid.
    public ObservableCollection<FileRow> Files { get; } = new();

    // Name of the folder whose files are displayed (grid header).
    [ObservableProperty]
    private string _selectedFolderName = string.Empty;

    [ObservableProperty]
    private bool _isLoadingFiles;

    // Empty/error guidance for the file grid (e.g. "no files in this folder").
    [ObservableProperty]
    private string _filesStatus = string.Empty;

    public bool IsConnected => State == ConnectionState.Connected;
    public bool IsDisconnected => State == ConnectionState.Disconnected;

    public string StatusText => State switch
    {
        ConnectionState.Connected => $"Connected as {UserDisplayName}",
        ConnectionState.Connecting => "Connecting to APS...",
        _ => "Not connected"
    };

    private bool CanConnect() => State == ConnectionState.Disconnected;
    private bool CanDisconnect() => State == ConnectionState.Connected;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (!_auth.IsConfigured)
        {
            ConfigurationRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        State = ConnectionState.Connecting;
        try
        {
            using var cts = new CancellationTokenSource();
            await _auth.SignInAsync(cts.Token);
            await LoadProfileAsync(cts.Token);
            State = ConnectionState.Connected;
            await RefreshProjectsAsync();
        }
        catch (Exception ex)
        {
            State = ConnectionState.Disconnected;
            MessageBox.Show(ex.Message, "Sign-in failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Loads the hubs and their projects into the connected panel. Doubles as
    // the connection proof: recognizable project names mean the link works.
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
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
            var hubs = await _auth.GetHubsAsync(cts.Token);

            var projectCount = 0;
            foreach (var hub in hubs)
            {
                var projects = await _auth.GetProjectsAsync(hub.Id, cts.Token);
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

    // Loads a project's top-level folders on first expand. The TreeView seeds
    // each project with a single placeholder child; we swap it for the real
    // folders here, then mark the node loaded so re-expanding is a no-op.
    public async Task LoadTopFoldersAsync(ProjectNode project)
    {
        if (project.IsLoaded)
            return;
        project.IsLoaded = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var folders = await _auth.GetTopFoldersAsync(project.HubId, project.ProjectId, cts.Token);
            project.Folders.Clear();
            foreach (var folder in folders)
                project.Folders.Add(new FolderNode(folder, project.ProjectId));
        }
        catch
        {
            // Leave the placeholder removed; a failed load shows an empty node.
            project.Folders.Clear();
            project.IsLoaded = false; // allow a retry on next expand
        }
    }

    // Loads a folder's subfolders on first expand (same placeholder swap). File
    // contents are loaded separately on selection (see ShowFolderFilesAsync) so
    // expanding the tree doesn't disturb the details grid.
    public async Task LoadSubFoldersAsync(FolderNode folder)
    {
        if (folder.IsLoaded)
            return;
        folder.IsLoaded = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var contents = await _auth.GetFolderContentsAsync(folder.ProjectId, folder.FolderId, cts.Token);
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

    // Loads the selected folder's files into the details grid. Always refetches
    // (cheap, on explicit user action) so the listing stays current.
    public async Task ShowFolderFilesAsync(FolderNode folder)
    {
        SelectedFolderName = folder.Name;
        IsLoadingFiles = true;
        FilesStatus = string.Empty;
        Files.Clear();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var contents = await _auth.GetFolderContentsAsync(folder.ProjectId, folder.FolderId, cts.Token);
            foreach (var file in contents.Files)
                Files.Add(new FileRow(file));

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
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        _auth.SignOut();
        UserDisplayName = string.Empty;
        Hubs.Clear();
        Files.Clear();
        SelectedFolderName = string.Empty;
        FilesStatus = string.Empty;
        ProjectsStatus = string.Empty;
        State = ConnectionState.Disconnected;
    }

    private async Task InitializeFromStoredTokenAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await LoadProfileAsync(cts.Token);
            if (!string.IsNullOrEmpty(UserDisplayName))
            {
                State = ConnectionState.Connected;
                await RefreshProjectsAsync();
            }
        }
        catch
        {
            // Stored token unusable; stay disconnected and let the user reconnect.
        }
    }

    private async Task LoadProfileAsync(CancellationToken cancellationToken)
    {
        var profile = await _auth.GetUserProfileAsync(cancellationToken);
        UserDisplayName = profile?.Name ?? profile?.Email ?? "APS user";
    }
}
