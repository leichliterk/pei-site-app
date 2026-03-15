using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PeiSiteApp.Models;
using PeiSiteApp.Services;
using SkiaSharp;

namespace PeiSiteApp.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly LocalServiceClient _localService;
    private readonly SiteApiService _siteApiService;
    private readonly FtpStatusService _ftpStatusService;
    private readonly SettingsManager _settingsManager;
    private DispatcherTimer? _updateTimer;
    private bool _started;

    [ObservableProperty]
    private ConnectionStatus _wsStatus = ConnectionStatus.Disconnected;

    [ObservableProperty]
    private string _currentUptime = "--";

    [ObservableProperty]
    private string _connectionSource = "Background Service";

    [ObservableProperty]
    private string _connectedAtText = "--";

    [ObservableProperty]
    private bool _isServiceRunning;

    [ObservableProperty]
    private FtpStatus _ftpStatus = FtpStatus.Disabled;

    [ObservableProperty]
    private string _ftpStatusText = "Disabled";

    // Chart data
    private readonly ObservableCollection<ObservablePoint> _chartValues = new();

    public ISeries[] ChartSeries { get; }

    public Axis[] XAxes { get; } = new Axis[]
    {
        new Axis
        {
            Labels = null,
            Labeler = value =>
            {
                var secondsAgo = 300 - (int)value;
                return secondsAgo switch
                {
                    300 => "-5m",
                    240 => "-4m",
                    180 => "-3m",
                    120 => "-2m",
                    60 => "-1m",
                    0 => "Now",
                    _ => ""
                };
            },
            MinStep = 60,
            MinLimit = 0,
            MaxLimit = 299,
            ShowSeparatorLines = false
        }
    };

    public Axis[] YAxes { get; } = new Axis[]
    {
        new Axis
        {
            MinLimit = -0.1,
            MaxLimit = 1.1,
            MinStep = 1,
            Labeler = value => value >= 0.5 ? "Up" : "Down",
            ShowSeparatorLines = false
        }
    };

    public HomeViewModel(
        LocalServiceClient localService,
        SiteApiService siteApiService,
        FtpStatusService ftpStatusService,
        SettingsManager settingsManager)
    {
        _localService = localService;
        _siteApiService = siteApiService;
        _ftpStatusService = ftpStatusService;
        _settingsManager = settingsManager;

        // Initialize chart with 300 zero points
        for (int i = 0; i < 300; i++)
            _chartValues.Add(new ObservablePoint(i, 0));

        ChartSeries = new ISeries[]
        {
            new StepLineSeries<ObservablePoint>
            {
                Values = _chartValues,
                Fill = new SolidColorPaint(SKColor.Parse("#3822C55E")),
                Stroke = new SolidColorPaint(SKColor.Parse("#22C55E")) { StrokeThickness = 2 },
                GeometrySize = 0,
                GeometryFill = null,
                GeometryStroke = null,
                AnimationsSpeed = TimeSpan.Zero
            }
        };
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        _localService.StatusChanged += OnServiceStatusChanged;
        _ftpStatusService.StatusChanged += HandleFtpStatusChanged;

        IsServiceRunning = _localService.IsServiceAvailable;
        _localService.StartPolling();

        _ = LoadUptimeDataAsync();

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _updateTimer.Tick += (s, e) =>
        {
            UpdateUptime();
            UpdateChartData();
        };
        _updateTimer.Start();
    }

    public void Stop()
    {
        _started = false;
        _updateTimer?.Stop();
        _updateTimer = null;
        _localService.StatusChanged -= OnServiceStatusChanged;
        _ftpStatusService.StatusChanged -= HandleFtpStatusChanged;
    }

    private void OnServiceStatusChanged(ServiceStatusResponse? status)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (status != null)
            {
                IsServiceRunning = true;
                WsStatus = MapServiceStatus(status.Status);
                ConnectedAtText = status.ConnectedAt != null
                    ? DateTime.Parse(status.ConnectedAt).ToLocalTime().ToString("g")
                    : "--";
            }
            else
            {
                IsServiceRunning = false;
                WsStatus = ConnectionStatus.Disconnected;
                ConnectedAtText = "--";
            }
        });
    }

    private void HandleFtpStatusChanged(FtpStatus status)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            FtpStatus = status;
            FtpStatusText = status switch
            {
                Models.FtpStatus.Connected => "Connected",
                Models.FtpStatus.Connecting => "Connecting...",
                Models.FtpStatus.Error => "Error",
                Models.FtpStatus.Disabled => "Disabled",
                _ => "Disconnected"
            };
        });
    }

    private static ConnectionStatus MapServiceStatus(string status)
    {
        return status switch
        {
            "connected" => ConnectionStatus.Connected,
            "connecting" => ConnectionStatus.Connecting,
            "error" => ConnectionStatus.Error,
            _ => ConnectionStatus.Disconnected
        };
    }

    private void UpdateUptime()
    {
        var svc = _localService.CurrentStatus;
        if (svc?.Status == "connected" && svc.CurrentUptime > 0)
        {
            CurrentUptime = FormatDuration(svc.CurrentUptime);
            return;
        }
        CurrentUptime = "--";
    }

    private void UpdateChartData()
    {
        var history = _localService.CurrentStatus?.ConnectionHistory;
        if (history == null || history.Length != 300) return;

        for (int i = 0; i < 300; i++)
            _chartValues[i].Y = history[i];
    }

    private async Task LoadUptimeDataAsync()
    {
        var settings = _settingsManager.Settings;
        var uptimeData = await _siteApiService.GetUptimeAsync(settings.TenantId, settings.SiteNumber, 7);
        if (uptimeData?.Sessions != null && uptimeData.Sessions.Count > 0)
            await _localService.PrepopulateHistoryAsync(uptimeData.Sessions);
    }

    private static string FormatDuration(double ms)
    {
        var totalSeconds = (int)(ms / 1000);
        var days = totalSeconds / 86400;
        var hours = (totalSeconds % 86400) / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return $"{days:D2}:{hours:D2}:{minutes:D2}:{seconds:D2}";
    }
}
