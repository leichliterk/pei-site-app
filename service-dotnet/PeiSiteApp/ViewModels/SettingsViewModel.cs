using System.Collections.ObjectModel;
using System.ServiceProcess;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeiSiteApp.Models;
using PeiSiteApp.Services;

namespace PeiSiteApp.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private readonly LocalServiceClient _localService;
    private readonly SiteApiService _siteApiService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private int _selectedTabIndex;

    // Site tab
    [ObservableProperty]
    private string _siteName = "";

    [ObservableProperty]
    private string _tenantId = "";

    [ObservableProperty]
    private int _siteNumber;

    [ObservableProperty]
    private string _siteNameMessage = "";

    [ObservableProperty]
    private bool _siteNameSuccess;

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _apiKeyMessage = "";

    [ObservableProperty]
    private bool _apiKeySuccess;

    // Tenant dialog
    [ObservableProperty]
    private bool _showTenantDialog;

    [ObservableProperty]
    private string _newTenantIdInput = "";

    [ObservableProperty]
    private string _tenantConfirmationPhrase = "";

    [ObservableProperty]
    private string _tenantErrorMessage = "";

    // Site number dialog
    [ObservableProperty]
    private bool _showSiteNumberDialog;

    [ObservableProperty]
    private string _newSiteNumberInput = "";

    [ObservableProperty]
    private string _confirmationPhrase = "";

    [ObservableProperty]
    private string _errorMessage = "";

    // FTP tab — server list
    [ObservableProperty]
    private bool _ftpEnabled;

    public ObservableCollection<FtpServerStatusResponse> FtpServers { get; } = new();

    // FTP Add/Edit dialog
    [ObservableProperty]
    private bool _showFtpServerDialog;

    [ObservableProperty]
    private string _ftpDialogTitle = "Add FTP Server";

    [ObservableProperty]
    private string _editingServerId = "";

    [ObservableProperty]
    private string _dialogServerName = "";

    [ObservableProperty]
    private string _dialogFtpHost = "";

    [ObservableProperty]
    private string _dialogFtpPath = "/";

    [ObservableProperty]
    private int _dialogFtpIntervalSeconds = 900;

    [ObservableProperty]
    private string _dialogUsername = "";

    [ObservableProperty]
    private string _dialogPassword = "";

    [ObservableProperty]
    private bool _dialogForceFullUpload;

    [ObservableProperty]
    private string _dialogTestMessage = "";

    [ObservableProperty]
    private bool? _dialogTestSuccess;

    [ObservableProperty]
    private bool _isDialogTesting;

    [ObservableProperty]
    private bool _isDialogSaving;

    // FTP Browse dialog
    [ObservableProperty]
    private bool _showBrowseDialog;

    [ObservableProperty]
    private bool _isBrowseLoading;

    [ObservableProperty]
    private string _browseErrorMessage = "";

    public ObservableCollection<FtpDirectoryNode> BrowseRoots { get; } = new();

    [ObservableProperty]
    private FtpDirectoryNode? _selectedBrowseNode;

    // FTP Delete dialog
    [ObservableProperty]
    private bool _showDeleteDialog;

    [ObservableProperty]
    private string _deletingServerId = "";

    [ObservableProperty]
    private string _deletingServerHost = "";

    // Logging tab
    [ObservableProperty]
    private string _selectedLogLevel = "info";

    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    private string? _lastLogTimestamp;
    private DispatcherTimer? _logPollTimer;

    // Advanced tab — service control
    private const string ServiceName = "PeiSiteService";

    [ObservableProperty]
    private string _serviceStatusDisplay = "Unknown";

    [ObservableProperty]
    private bool _isServiceRunning;

    [ObservableProperty]
    private bool _isServiceBusy;

    [ObservableProperty]
    private string _serviceMessage = "";

    private DispatcherTimer? _serviceStatusTimer;

    public static IReadOnlyList<string> LogLevels { get; } =
        new[] { "debug", "info", "warning", "error", "critical" };

    public bool IsFtpDialogSaveEnabled =>
        !string.IsNullOrWhiteSpace(DialogServerName) && !string.IsNullOrWhiteSpace(DialogFtpHost);

    public bool IsFormValid
    {
        get
        {
            if (!int.TryParse(NewSiteNumberInput, out _)) return false;
            return ConfirmationPhrase.Equals("change site number", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsTenantFormValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NewTenantIdInput)) return false;
            return TenantConfirmationPhrase.Equals("change tenant id", StringComparison.OrdinalIgnoreCase);
        }
    }

    public SettingsViewModel(
        SettingsManager settingsManager,
        LocalServiceClient localService,
        SiteApiService siteApiService,
        MainViewModel mainViewModel)
    {
        _settingsManager = settingsManager;
        _localService = localService;
        _siteApiService = siteApiService;
        _mainViewModel = mainViewModel;

        var settings = settingsManager.Settings;
        SiteName = settings.SiteName;
        TenantId = settings.TenantId.ToString();
        SiteNumber = settings.SiteNumber;
        SelectedLogLevel = settings.LogLevel;

        _ = LoadFtpServersAsync();
    }

    // --- Site tab commands ---

    [RelayCommand]
    private async Task SaveSiteNameAsync()
    {
        if (string.IsNullOrWhiteSpace(TenantId) || SiteNumber == 0)
        {
            SiteNameMessage = "Missing tenant ID or site number";
            SiteNameSuccess = false;
            return;
        }

        var success = await _siteApiService.UpdateSiteNameAsync(
            int.Parse(TenantId), SiteNumber, SiteName);

        if (success)
        {
            _settingsManager.SaveSiteName(SiteName);
            _mainViewModel.UpdateSiteInfo();
            SiteNameMessage = "Site name updated successfully";
            SiteNameSuccess = true;
        }
        else
        {
            SiteNameMessage = "Failed to update site name on server";
            SiteNameSuccess = false;
        }
    }

    [RelayCommand]
    private async Task SaveApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ApiKeyMessage = "API key cannot be empty";
            ApiKeySuccess = false;
            return;
        }

        try
        {
            await _localService.UpdateConfigAsync(new { apiKey = ApiKey });
            ApiKey = "";
            ApiKeyMessage = "API key updated successfully";
            ApiKeySuccess = true;
        }
        catch (Exception)
        {
            ApiKeyMessage = "Failed to update API key";
            ApiKeySuccess = false;
        }
    }

    [RelayCommand]
    private void EditTenantId()
    {
        ShowTenantDialog = true;
        NewTenantIdInput = TenantId;
        TenantConfirmationPhrase = "";
        TenantErrorMessage = "";
    }

    [RelayCommand]
    private void CloseTenantDialog()
    {
        ShowTenantDialog = false;
        NewTenantIdInput = "";
        TenantConfirmationPhrase = "";
        TenantErrorMessage = "";
    }

    [RelayCommand]
    private async Task ConfirmTenantIdChangeAsync()
    {
        TenantErrorMessage = "";

        if (string.IsNullOrWhiteSpace(NewTenantIdInput))
        {
            TenantErrorMessage = "Please enter a valid tenant ID";
            return;
        }

        if (!TenantConfirmationPhrase.Equals("change tenant id", StringComparison.OrdinalIgnoreCase))
        {
            TenantErrorMessage = "Please type \"change tenant id\" to confirm";
            return;
        }

        if (!int.TryParse(NewTenantIdInput, out var newTenantId))
        {
            TenantErrorMessage = "Please enter a valid number";
            return;
        }

        TenantId = NewTenantIdInput;
        _settingsManager.UpdateTenantId(newTenantId);
        _mainViewModel.UpdateSiteInfo();

        await _localService.UpdateConfigAsync(new { tenantId = newTenantId });

        CloseTenantDialog();
    }

    [RelayCommand]
    private void EditSiteNumber()
    {
        ShowSiteNumberDialog = true;
        NewSiteNumberInput = SiteNumber.ToString();
        ConfirmationPhrase = "";
        ErrorMessage = "";
    }

    [RelayCommand]
    private void CloseDialog()
    {
        ShowSiteNumberDialog = false;
        NewSiteNumberInput = "";
        ConfirmationPhrase = "";
        ErrorMessage = "";
    }

    [RelayCommand]
    private async Task ConfirmSiteNumberChangeAsync()
    {
        ErrorMessage = "";

        if (!int.TryParse(NewSiteNumberInput, out var parsedNumber))
        {
            ErrorMessage = "Please enter a valid number";
            return;
        }

        if (!ConfirmationPhrase.Equals("change site number", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Please type \"change site number\" to confirm";
            return;
        }

        var oldSiteNumber = SiteNumber;
        SiteNumber = parsedNumber;
        _settingsManager.UpdateSiteNumber(parsedNumber);
        _mainViewModel.UpdateSiteInfo();

        await _localService.UpdateConfigAsync(new { siteId = parsedNumber });

        if (!string.IsNullOrWhiteSpace(TenantId) && int.TryParse(TenantId, out var tenantId))
        {
            await _siteApiService.UpdateSiteIdAsync(tenantId, oldSiteNumber, parsedNumber);
        }

        CloseDialog();
    }

    // --- FTP tab commands ---

    private async Task LoadFtpServersAsync()
    {
        var status = await _localService.FtpGetStatusAsync();
        if (status == null) return;

        FtpEnabled = status.FtpEnabled;
        FtpServers.Clear();
        foreach (var s in status.Servers)
            FtpServers.Add(s);
    }

    [RelayCommand]
    private async Task ToggleFtpEnabledAsync()
    {
        await _localService.FtpSetEnabledAsync(FtpEnabled);
    }

    [RelayCommand]
    private void OpenAddServerDialog()
    {
        FtpDialogTitle = "Add FTP Server";
        EditingServerId = "";
        DialogServerName = "";
        DialogFtpHost = "";
        DialogFtpPath = "/";
        DialogFtpIntervalSeconds = 900;
        DialogUsername = "";
        DialogPassword = "";
        DialogForceFullUpload = false;
        DialogTestMessage = "";
        DialogTestSuccess = null;
        ShowFtpServerDialog = true;
    }

    [RelayCommand]
    private void OpenEditServerDialog(FtpServerStatusResponse server)
    {
        FtpDialogTitle = "Edit FTP Server";
        EditingServerId = server.Id;
        DialogServerName = server.Name;
        DialogFtpHost = server.Host;
        DialogFtpPath = server.Path;
        DialogFtpIntervalSeconds = server.PollInterval;
        if (DialogFtpIntervalSeconds < 1) DialogFtpIntervalSeconds = 1;
        DialogUsername = server.Username;
        DialogPassword = ""; // passwords are never returned from the service; leave blank to keep existing
        DialogForceFullUpload = server.ForceFullUploadOnNextPoll;
        DialogTestMessage = "";
        DialogTestSuccess = null;
        ShowFtpServerDialog = true;
    }

    [RelayCommand]
    private void CloseFtpServerDialog()
    {
        ShowFtpServerDialog = false;
    }

    [RelayCommand]
    private async Task DialogTestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(DialogFtpHost))
        {
            DialogTestMessage = "Please enter an FTP host address";
            DialogTestSuccess = false;
            return;
        }

        IsDialogTesting = true;
        DialogTestMessage = "Testing connection...";
        DialogTestSuccess = null;

        var result = await _localService.FtpTestConnectionAsync(DialogFtpHost, DialogFtpPath, DialogUsername, DialogPassword);
        DialogTestMessage = result.Message;
        DialogTestSuccess = result.Success;
        IsDialogTesting = false;
    }

    [RelayCommand]
    private async Task SaveFtpServerAsync()
    {
        if (string.IsNullOrWhiteSpace(DialogFtpHost))
        {
            DialogTestMessage = "Please enter an FTP host address";
            DialogTestSuccess = false;
            return;
        }

        IsDialogSaving = true;
        var intervalSeconds = DialogFtpIntervalSeconds;

        if (string.IsNullOrEmpty(EditingServerId))
        {
            var result = await _localService.FtpAddServerAsync(DialogServerName, DialogFtpHost, DialogFtpPath, intervalSeconds, DialogUsername, DialogPassword);
            if (result != null)
            {
                ShowFtpServerDialog = false;
                await LoadFtpServersAsync();
            }
            else
            {
                DialogTestMessage = "Failed to add FTP server";
                DialogTestSuccess = false;
            }
        }
        else
        {
            var success = await _localService.FtpUpdateServerAsync(EditingServerId, DialogServerName, DialogFtpHost, DialogFtpPath, intervalSeconds, DialogUsername, DialogPassword, DialogForceFullUpload);
            if (success)
            {
                ShowFtpServerDialog = false;
                await LoadFtpServersAsync();
            }
            else
            {
                DialogTestMessage = "Failed to update FTP server";
                DialogTestSuccess = false;
            }
        }

        IsDialogSaving = false;
    }

    // Browse dialog

    [RelayCommand]
    private async Task OpenBrowseDialogAsync()
    {
        if (string.IsNullOrWhiteSpace(DialogFtpHost))
        {
            DialogTestMessage = "Please enter an FTP host address first";
            DialogTestSuccess = false;
            return;
        }

        ShowBrowseDialog = true;
        BrowseErrorMessage = "";
        SelectedBrowseNode = null;
        BrowseRoots.Clear();

        await LoadBrowseChildrenAsync("/");
    }

    [RelayCommand]
    private void CloseBrowseDialog()
    {
        ShowBrowseDialog = false;
    }

    [RelayCommand]
    private void SelectBrowsePath()
    {
        if (SelectedBrowseNode != null)
        {
            DialogFtpPath = SelectedBrowseNode.FullPath;
        }
        ShowBrowseDialog = false;
    }

    [RelayCommand]
    private async Task ExpandBrowseNodeAsync(FtpDirectoryNode node)
    {
        if (node.HasLoadedChildren) return;

        node.IsLoading = true;
        var result = await _localService.FtpBrowseAsync(DialogFtpHost, node.FullPath, DialogUsername, DialogPassword);
        node.IsLoading = false;

        if (result.Success)
        {
            node.Children.Clear();
            foreach (var dir in result.Directories)
            {
                node.Children.Add(new FtpDirectoryNode
                {
                    Name = dir.Name,
                    FullPath = dir.FullPath,
                    Children = { new FtpDirectoryNode { Name = "Loading..." } }
                });
            }
            node.HasLoadedChildren = true;
        }
    }

    private async Task LoadBrowseChildrenAsync(string path)
    {
        IsBrowseLoading = true;
        BrowseErrorMessage = "";

        var result = await _localService.FtpBrowseAsync(DialogFtpHost, path, DialogUsername, DialogPassword);
        IsBrowseLoading = false;

        if (result.Success)
        {
            BrowseRoots.Clear();
            foreach (var dir in result.Directories)
            {
                BrowseRoots.Add(new FtpDirectoryNode
                {
                    Name = dir.Name,
                    FullPath = dir.FullPath,
                    Children = { new FtpDirectoryNode { Name = "Loading..." } }
                });
            }

            if (result.Directories.Count == 0)
                BrowseErrorMessage = "No subdirectories found at this path";
        }
        else
        {
            BrowseErrorMessage = result.Message;
        }
    }

    // Delete dialog

    [RelayCommand]
    private void OpenDeleteDialog(FtpServerStatusResponse server)
    {
        DeletingServerId = server.Id;
        DeletingServerHost = server.Host;
        ShowDeleteDialog = true;
    }

    [RelayCommand]
    private void CloseDeleteDialog()
    {
        ShowDeleteDialog = false;
        DeletingServerId = "";
        DeletingServerHost = "";
    }

    [RelayCommand]
    private async Task ConfirmDeleteServerAsync()
    {
        var success = await _localService.FtpDeleteServerAsync(DeletingServerId);
        ShowDeleteDialog = false;
        if (success)
            await LoadFtpServersAsync();
    }

    // --- Logging tab commands ---

    [RelayCommand]
    private async Task SaveLogLevelAsync()
    {
        _settingsManager.SaveLogLevel(SelectedLogLevel);
        await _localService.SetLogLevelAsync(SelectedLogLevel);
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
    }

    private void StartLogPolling()
    {
        if (_logPollTimer != null) return;
        _ = PollLogsAsync();
        _logPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _logPollTimer.Tick += async (_, _) => await PollLogsAsync();
        _logPollTimer.Start();
    }

    private void StopLogPolling()
    {
        _logPollTimer?.Stop();
        _logPollTimer = null;
    }

    private async Task PollLogsAsync()
    {
        var entries = await _localService.GetLogsAsync(_lastLogTimestamp);
        if (entries == null || entries.Count == 0) return;

        foreach (var entry in entries)
            LogEntries.Add(entry);

        // Cap at 500 entries to avoid unbounded growth
        while (LogEntries.Count > 500)
            LogEntries.RemoveAt(0);

        _lastLogTimestamp = entries[^1].Timestamp;
    }

    // --- Advanced tab commands ---

    [RelayCommand(CanExecute = nameof(CanControlService))]
    private async Task StartServiceAsync()
    {
        IsServiceBusy = true;
        ServiceMessage = "";
        try
        {
            await Task.Run(() =>
            {
                using var sc = new ServiceController(ServiceName);
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            });
            await RefreshServiceStatusAsync();
        }
        catch (Exception ex)
        {
            ServiceMessage = $"Failed to start: {ex.Message}";
        }
        finally { IsServiceBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanControlService))]
    private async Task StopServiceAsync()
    {
        IsServiceBusy = true;
        ServiceMessage = "";
        try
        {
            await Task.Run(() =>
            {
                using var sc = new ServiceController(ServiceName);
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            });
            await RefreshServiceStatusAsync();
        }
        catch (Exception ex)
        {
            ServiceMessage = $"Failed to stop: {ex.Message}";
        }
        finally { IsServiceBusy = false; }
    }

    private bool CanControlService() => !IsServiceBusy;

    partial void OnIsServiceBusyChanged(bool value)
    {
        StartServiceCommand.NotifyCanExecuteChanged();
        StopServiceCommand.NotifyCanExecuteChanged();
    }

    private void StartServiceStatusPolling()
    {
        if (_serviceStatusTimer != null) return;
        _ = RefreshServiceStatusAsync();
        _serviceStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _serviceStatusTimer.Tick += async (_, _) => await RefreshServiceStatusAsync();
        _serviceStatusTimer.Start();
    }

    private void StopServiceStatusPolling()
    {
        _serviceStatusTimer?.Stop();
        _serviceStatusTimer = null;
    }

    private async Task RefreshServiceStatusAsync()
    {
        try
        {
            var status = await Task.Run(() =>
            {
                using var sc = new ServiceController(ServiceName);
                return sc.Status;
            });
            IsServiceRunning = status == ServiceControllerStatus.Running;
            ServiceStatusDisplay = status switch
            {
                ServiceControllerStatus.Running => "Running",
                ServiceControllerStatus.Stopped => "Stopped",
                ServiceControllerStatus.StartPending => "Starting...",
                ServiceControllerStatus.StopPending => "Stopping...",
                ServiceControllerStatus.Paused => "Paused",
                _ => status.ToString()
            };
        }
        catch
        {
            IsServiceRunning = false;
            ServiceStatusDisplay = "Unknown";
        }
    }

    // Tab indices: Site=0, Logging=1, FTP=2, Advanced=3
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 1) StartLogPolling();
        else StopLogPolling();

        if (value == 3) StartServiceStatusPolling();
        else StopServiceStatusPolling();
    }

    partial void OnConfirmationPhraseChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
    partial void OnNewSiteNumberInputChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
    partial void OnTenantConfirmationPhraseChanged(string value) => OnPropertyChanged(nameof(IsTenantFormValid));
    partial void OnNewTenantIdInputChanged(string value) => OnPropertyChanged(nameof(IsTenantFormValid));
    partial void OnDialogServerNameChanged(string value) => OnPropertyChanged(nameof(IsFtpDialogSaveEnabled));
    partial void OnDialogFtpHostChanged(string value) => OnPropertyChanged(nameof(IsFtpDialogSaveEnabled));
}
