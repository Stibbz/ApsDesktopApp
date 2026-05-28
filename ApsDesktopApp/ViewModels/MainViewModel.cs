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

// Application shell: owns the APS connection state and hosts the tool hub. When
// connected it shows the home page (tool cards) until a tool is opened, then
// swaps the tool's view into the content area (see ActiveContent + DataTemplates
// in MainWindow.xaml).
public partial class MainViewModel : ObservableObject
{
    private readonly ApsAuthService _auth;
    private readonly ApsDataService _data;

    // Raised when the user tries to connect before a Client ID is configured.
    // The View handles this by opening the Settings window (no MessageBox).
    public event EventHandler? ConfigurationRequested;

    // Shared project selection used by all tools that operate on a single project.
    public ProjectContextViewModel ProjectContext { get; }

    public MainViewModel(
        ApsAuthService auth,
        ApsDataService data,
        ProjectContextViewModel projectContext,
        DataBrowserViewModel dataBrowser,
        IssuesViewModel issues)
    {
        ProjectContext = projectContext;
        _auth = auth;
        _data = data;

        Tools.Add(new ToolDescriptor(
            "Data Browser",
            "Browse hubs, projects and folders; inspect file metadata, version "
            + "history, and naming-convention compliance. Right-click any file "
            + "to convert it to IFC, DWG, OBJ, or STL.",
            "DM", dataBrowser));

        Tools.Add(new ToolDescriptor(
            "Issues Manager",
            "Load all ACC issues for a project into a sortable, searchable table. "
            + "Export to Excel for bulk editing, then import the workbook to push "
            + "changes back to ACC.",
            "IS", issues));

        if (_auth.HasStoredToken)
            _ = InitializeFromStoredTokenAsync();
    }

    // -- Tool hub ----------------------------------------------------------
    public ObservableCollection<ToolDescriptor> Tools { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome))]
    [NotifyPropertyChangedFor(nameof(ActiveContent))]
    [NotifyPropertyChangedFor(nameof(CurrentToolName))]
    private ToolDescriptor? _currentTool;

    public bool IsHome => CurrentTool is null;
    public string CurrentToolName => CurrentTool?.Name ?? "Home";

    // Content shown in the shell's ContentControl: the open tool's ViewModel, or
    // the shell itself (which the HomeView DataTemplate renders) when at home.
    public object ActiveContent => CurrentTool?.ViewModel ?? (object)this;

    [RelayCommand]
    private void OpenTool(ToolDescriptor tool)
    {
        CurrentTool = tool;
        if (tool.ViewModel is IToolLifecycle lifecycle)
            _ = lifecycle.ActivateAsync();
    }

    [RelayCommand]
    private void GoHome() => CurrentTool = null;

    // -- Connection --------------------------------------------------------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private ConnectionState _state = ConnectionState.Disconnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _userDisplayName = string.Empty;

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
            CurrentTool = null; // land on the home page
            _ = ProjectContext.LoadAsync();
        }
        catch (Exception ex)
        {
            State = ConnectionState.Disconnected;
            MessageBox.Show(ex.Message, "Sign-in failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        _auth.SignOut();
        UserDisplayName = string.Empty;
        CurrentTool = null;
        ProjectContext.Reset();
        foreach (var tool in Tools)
            (tool.ViewModel as IToolLifecycle)?.Reset();
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
                CurrentTool = null;
                _ = ProjectContext.LoadAsync();
            }
        }
        catch
        {
            // Stored token unusable; stay disconnected and let the user reconnect.
        }
    }

    private async Task LoadProfileAsync(CancellationToken cancellationToken)
    {
        var profile = await _data.GetUserProfileAsync(cancellationToken);
        UserDisplayName = profile?.Name ?? profile?.Email ?? "APS user";
    }
}
