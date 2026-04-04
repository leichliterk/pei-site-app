using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeiSiteApp.Services;

namespace PeiSiteApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private readonly LocalServiceClient _localService;
    private readonly SiteApiService _siteApiService;
    private readonly FtpStatusService _ftpStatusService;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private string _siteNumber = "";

    [ObservableProperty]
    private string _siteName = "";

    [ObservableProperty]
    private string _windowTitle = "PEI Site App";

    [ObservableProperty]
    private int _unreadNotificationCount;

    public bool HasUnreadNotifications => UnreadNotificationCount > 0;
    public string UnreadNotificationDisplay => UnreadNotificationCount > 99 ? "99+" : UnreadNotificationCount.ToString();

    partial void OnUnreadNotificationCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnreadNotifications));
        OnPropertyChanged(nameof(UnreadNotificationDisplay));
    }

    public string Version { get; }

    private HomeViewModel? _homeViewModel;
    private SettingsViewModel? _settingsViewModel;
    private NotificationsViewModel? _notificationsViewModel;
    private DispatcherTimer? _notificationPollTimer;

    public MainViewModel(
        SettingsManager settingsManager,
        LocalServiceClient localService,
        SiteApiService siteApiService,
        FtpStatusService ftpStatusService)
    {
        _settingsManager = settingsManager;
        _localService = localService;
        _siteApiService = siteApiService;
        _ftpStatusService = ftpStatusService;

        var settings = settingsManager.Settings;
        SiteNumber = settings.SiteNumber.ToString();
        SiteName = settings.SiteName;
        WindowTitle = settings.AppName;
        Version = $"v{settings.Version}";
    }

    public async Task InitializeAsync()
    {
        // Check if background service is available (informational — HomeViewModel will poll)
        await _localService.CheckHealthAsync();

        // Start FTP status polling
        _ftpStatusService.StartPolling();

        // Start notification badge polling (every 10 seconds)
        _ = PollNotificationCountAsync();
        _notificationPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _notificationPollTimer.Tick += async (_, _) => await PollNotificationCountAsync();
        _notificationPollTimer.Start();

        // Navigate to home
        NavigateHome();
    }

    private async Task PollNotificationCountAsync()
    {
        var result = await _localService.GetNotificationsAsync();
        if (result != null)
            UnreadNotificationCount = result.UnreadCount;
    }

    [RelayCommand]
    private void NavigateHome()
    {
        _homeViewModel ??= new HomeViewModel(_localService, _siteApiService, _ftpStatusService, _settingsManager);
        _homeViewModel.Start();
        CurrentPage = _homeViewModel;
    }

    [RelayCommand]
    public void NavigateSettings(int? tabIndex = null)
    {
        _settingsViewModel ??= new SettingsViewModel(_settingsManager, _localService, _siteApiService, this);
        if (tabIndex.HasValue)
            _settingsViewModel.SelectedTabIndex = tabIndex.Value;
        CurrentPage = _settingsViewModel;
    }

    [RelayCommand]
    private void NavigateNotifications()
    {
        _notificationsViewModel ??= new NotificationsViewModel(_localService, this, _settingsManager.Settings.ApiUrl);
        CurrentPage = _notificationsViewModel;
        _ = _notificationsViewModel.LoadAsync();
    }

    public void RefreshNotificationCount()
    {
        _ = PollNotificationCountAsync();
    }

    public void UpdateSiteInfo()
    {
        var settings = _settingsManager.Settings;
        SiteNumber = settings.SiteNumber.ToString();
        SiteName = settings.SiteName;
    }

    public void Shutdown()
    {
        _notificationPollTimer?.Stop();
        _notificationPollTimer = null;
        _homeViewModel?.Stop();
        _ftpStatusService.StopPolling();
        _localService.StopPolling();
        _localService.Dispose();
        _siteApiService.Dispose();
        _ftpStatusService.Dispose();
    }
}
