using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeiSiteApp.Models;
using PeiSiteApp.Services;

namespace PeiSiteApp.ViewModels;

public partial class ScadaViewModel : ObservableObject
{
    private readonly LocalServiceClient _localService;
    private DispatcherTimer? _pollTimer;

    public ObservableCollection<PlcTagSnapshotItem> Tags { get; } = new();

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _ipAddress = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _lastUpdated = "";
    [ObservableProperty] private bool _isEnabled;

    public string ConnectionStatus => IsConnected ? "Connected" : "Disconnected";
    public string ConnectionColor => IsConnected ? "#22C55E" : "#EF4444";

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(ConnectionColor));
    }

    public ScadaViewModel(LocalServiceClient localService)
    {
        _localService = localService;
    }

    public void Start()
    {
        _ = RefreshAsync();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _pollTimer.Start();
    }

    public void Stop()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private async Task RefreshAsync()
    {
        var settings = await _localService.PlcGetSettingsAsync();
        if (settings == null)
        {
            StatusMessage = "Service unavailable";
            return;
        }

        IsEnabled = settings.Enabled;
        IpAddress = settings.IpAddress;

        if (!settings.Enabled)
        {
            StatusMessage = "PLC polling is disabled. Enable it in Settings.";
            Tags.Clear();
            return;
        }

        StatusMessage = "";
    }

    // Called by the view when a plc:snapshot WebSocket event arrives
    public void ApplySnapshot(PlcSnapshotResponse snapshot)
    {
        IsConnected = snapshot.Connected;
        LastUpdated = snapshot.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");

        // Sync tag list in-place
        for (int i = 0; i < snapshot.Tags.Count; i++)
        {
            if (i < Tags.Count)
                Tags[i] = snapshot.Tags[i];
            else
                Tags.Add(snapshot.Tags[i]);
        }
        while (Tags.Count > snapshot.Tags.Count)
            Tags.RemoveAt(Tags.Count - 1);
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await RefreshAsync();
    }
}
