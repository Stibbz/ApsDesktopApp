using System;
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
            MessageBox.Show(
                "Please set your APS Client ID in APS > Settings first.",
                "Not configured", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        State = ConnectionState.Connecting;
        try
        {
            using var cts = new CancellationTokenSource();
            await _auth.SignInAsync(cts.Token);
            await LoadProfileAsync(cts.Token);
            State = ConnectionState.Connected;
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
        State = ConnectionState.Disconnected;
    }

    private async Task InitializeFromStoredTokenAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await LoadProfileAsync(cts.Token);
            if (!string.IsNullOrEmpty(UserDisplayName))
                State = ConnectionState.Connected;
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
