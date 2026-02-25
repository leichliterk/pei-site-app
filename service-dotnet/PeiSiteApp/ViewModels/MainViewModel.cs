using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeiSiteApp.Services;

namespace PeiSiteApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private readonly LocalServiceClient _localService;
    private readonly WebSocketService _webSocketService;
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

    public string Version { get; }

    private HomeViewModel? _homeViewModel;
    private SettingsViewModel? _settingsViewModel;

    public MainViewModel(
        SettingsManager settingsManager,
        LocalServiceClient localService,
        WebSocketService webSocketService,
        SiteApiService siteApiService,
        FtpStatusService ftpStatusService)
    {
        _settingsManager = settingsManager;
        _localService = localService;
        _webSocketService = webSocketService;
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
        // Check if background service is available
        var serviceAvailable = await _localService.CheckHealthAsync();

        if (!serviceAvailable)
        {
            // Fall back to direct WebSocket
            var settings = _settingsManager.Settings;
            _webSocketService.Connect(settings.ApiUrl, settings.ApiKey, settings.SiteNumber, settings.TenantId);
        }

        // Start FTP status polling
        _ftpStatusService.StartPolling();

        // Navigate to home
        NavigateHome();
    }

    [RelayCommand]
    private void NavigateHome()
    {
        _homeViewModel ??= new HomeViewModel(_localService, _webSocketService, _siteApiService, _ftpStatusService, _settingsManager);
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

    public void UpdateSiteInfo()
    {
        var settings = _settingsManager.Settings;
        SiteNumber = settings.SiteNumber.ToString();
        SiteName = settings.SiteName;
    }

    public void Shutdown()
    {
        _homeViewModel?.Stop();
        _ftpStatusService.StopPolling();
        _localService.StopPolling();
        _webSocketService.Disconnect();
        _localService.Dispose();
        _webSocketService.Dispose();
        _siteApiService.Dispose();
        _ftpStatusService.Dispose();
    }
}
