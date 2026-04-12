using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeiSiteApp.Models;
using PeiSiteApp.Services;

namespace PeiSiteApp.ViewModels;

public partial class QueueViewModel : ObservableObject
{
    private readonly LocalServiceClient _localService;
    private DispatcherTimer? _pollTimer;

    public ObservableCollection<QueueEntryResponse> Entries { get; } = new();

    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isInFlight;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "";

    public string BrokerStatusText => IsPaused ? "Paused" : "Active";
    public string PauseResumeLabel => IsPaused ? "Resume" : "Pause";

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(BrokerStatusText));
        OnPropertyChanged(nameof(PauseResumeLabel));
    }

    public QueueViewModel(LocalServiceClient localService)
    {
        _localService = localService;
    }

    public void Start()
    {
        _ = RefreshAsync();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
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
        var queueTask = _localService.GetQueueAsync();
        var statusTask = _localService.GetQueueStatusAsync();
        await Task.WhenAll(queueTask, statusTask);

        var queue = await queueTask;
        var status = await statusTask;

        if (status != null)
        {
            IsPaused = status.Paused;
            IsInFlight = status.InFlight;
            PendingCount = status.PendingCount;
        }

        if (queue != null)
        {
            // Sync list in-place to avoid flicker — replace all (list is small)
            Entries.Clear();
            foreach (var e in queue.Entries)
                Entries.Add(e);
        }

        StatusMessage = queue == null ? "Unable to reach service" : "";
    }

    [RelayCommand]
    private async Task PauseResume()
    {
        if (IsPaused)
            await _localService.QueueResumeAsync();
        else
            await _localService.QueuePauseAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Retry(QueueEntryResponse? entry)
    {
        if (entry == null) return;
        await _localService.QueueRetryAsync(entry.Id);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ClearHistory()
    {
        await _localService.QueueClearHistoryAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        IsLoading = true;
        await RefreshAsync();
        IsLoading = false;
    }

    // ── Formatting helpers (called from XAML converters via binding) ──────────

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }

    public static string FormatTimestamp(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("MM/dd HH:mm:ss");
        return iso;
    }

    public static string StatusColor(string status) => status switch
    {
        "success"          => "#22C55E",
        "sending"          => "#3B82F6",
        "retrying"         => "#F59E0B",
        "permanentlyFailed"=> "#EF4444",
        _                  => "#64748B"   // pending / unknown
    };

    public static string StatusLabel(string status) => status switch
    {
        "pending"          => "Pending",
        "sending"          => "Sending",
        "retrying"         => "Retrying",
        "success"          => "Success",
        "permanentlyFailed"=> "Failed",
        _                  => status
    };
}
