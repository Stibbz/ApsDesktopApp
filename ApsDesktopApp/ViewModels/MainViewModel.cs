using System;
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
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var hubs = await _auth.GetHubsAsync(cts.Token);

            var projectCount = 0;
            foreach (var hub in hubs)
            {
                var projects = await _auth.GetProjectsAsync(hub.Id, cts.Token);
                projectCount += projects.Count;
                Hubs.Add(new HubNode(hub, projects));
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

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        _auth.SignOut();
        UserDisplayName = string.Empty;
        Hubs.Clear();
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
