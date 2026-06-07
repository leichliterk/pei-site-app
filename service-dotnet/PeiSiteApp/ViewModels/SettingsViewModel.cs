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
    private string _siteNumber = "";

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
    private DispatcherTimer? _ftpStatusTimer;

    // PLC tab
    [ObservableProperty]
    private bool _plcEnabled;

    [ObservableProperty]
    private string _plcConnectionType = "ControlLogix";

    [ObservableProperty]
    private string _plcIpAddress = "192.168.1.10";

    [ObservableProperty]
    private int _plcSlot;

    [ObservableProperty]
    private int _modbusTcpPort = 502;

    [ObservableProperty]
    private int _modbusTcpUnitId = 1;

    [ObservableProperty]
    private int _plcPollingIntervalMs = 500;

    [ObservableProperty]
    private string _plcSaveMessage = "";

    [ObservableProperty]
    private bool _plcSaveSuccess;

    [ObservableProperty]
    private bool _plcIsSaving;

    private CancellationTokenSource? _plcSaveMessageCts;

    // PLC Add/Edit Tag dialog
    [ObservableProperty]
    private bool _showAddPlcTagDialog;

    [ObservableProperty]
    private string _plcTagDialogTitle = "Add PLC Tag";

    [ObservableProperty]
    private string _newPlcTagName = "";

    [ObservableProperty]
    private string _newPlcTagDataType = "REAL";

    [ObservableProperty]
    private string _newPlcTagDisplayName = "";

    [ObservableProperty]
    private string _newPlcTagUnit = "";

    [ObservableProperty]
    private string _newPlcTagMultiplier = "1";

    [ObservableProperty]
    private string _plcTagDialogError = "";

    /// <summary>Show the Multiplier field only for Modbus BCD_INT_16 tags.</summary>
    public bool ShowMultiplierField => IsModbusTcp && NewPlcTagDataType == "BCD_INT_16";

    private PlcTagDefinitionModel? _editingPlcTag;

    public ObservableCollection<PlcTagDefinitionModel> PlcTags { get; } = new();

    public static IReadOnlyList<string> ControlLogixDataTypes { get; } =
        new[] { "REAL", "BOOL", "DINT", "STRING" };

    public static IReadOnlyList<string> ModbusDataTypes { get; } =
        new[] { "FLOAT32", "UINT16", "INT16", "UINT32", "INT32", "BOOL", "BCD_INT_16" };

    /// <summary>Options list for the PLC Type ComboBox.</summary>
    public static IReadOnlyList<PlcConnectionTypeOption> PlcConnectionTypeOptions { get; } = new[]
    {
        new PlcConnectionTypeOption("ControlLogix", "Allen-Bradley CompactLogix / ControlLogix"),
        new PlcConnectionTypeOption("ModbusTcp",    "Modbus TCP  (Host Engineering, etc.)")
    };

    public bool IsControlLogix => PlcConnectionType == "ControlLogix";
    public bool IsModbusTcp    => PlcConnectionType == "ModbusTcp";

    /// <summary>Data type choices appropriate to the selected PLC protocol.</summary>
    public IReadOnlyList<string> CurrentPlcDataTypes =>
        IsModbusTcp ? ModbusDataTypes : ControlLogixDataTypes;

    /// <summary>Label shown above the tag-name field in the Add/Edit dialog.</summary>
    public string PlcTagNameLabel => IsModbusTcp
        ? "Register Address  (e.g. 40001 = holding reg 0,  30001 = input reg 0)"
        : "Tag Name  (PLC address, e.g. FLR_1.MMBTU.RATE.VLU.SCL)";

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
            if (string.IsNullOrWhiteSpace(NewSiteNumberInput)) return false;
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
        if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(SiteNumber))
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
        NewSiteNumberInput = SiteNumber;
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

        if (string.IsNullOrWhiteSpace(NewSiteNumberInput))
        {
            ErrorMessage = "Please enter a site ID";
            return;
        }

        if (!ConfirmationPhrase.Equals("change site number", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Please type \"change site number\" to confirm";
            return;
        }

        var oldSiteNumber = SiteNumber;
        SiteNumber = NewSiteNumberInput;
        _settingsManager.UpdateSiteNumber(NewSiteNumberInput);
        _mainViewModel.UpdateSiteInfo();

        await _localService.UpdateConfigAsync(new { siteId = NewSiteNumberInput });

        if (!string.IsNullOrWhiteSpace(TenantId) && int.TryParse(TenantId, out var tenantId))
        {
            await _siteApiService.UpdateSiteIdAsync(tenantId, oldSiteNumber, NewSiteNumberInput);
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

    private void StartFtpStatusPolling()
    {
        if (_ftpStatusTimer != null) return;
        _ftpStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _ftpStatusTimer.Tick += async (_, _) => await LoadFtpServersAsync();
        _ftpStatusTimer.Start();
    }

    private void StopFtpStatusPolling()
    {
        _ftpStatusTimer?.Stop();
        _ftpStatusTimer = null;
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

    // --- PLC tab commands ---

    private async Task LoadPlcSettingsAsync()
    {
        var settings = await _localService.PlcGetSettingsAsync();
        if (settings == null) return;
        PlcEnabled = settings.Enabled;
        PlcConnectionType = string.IsNullOrEmpty(settings.ConnectionType)
            ? "ControlLogix" : settings.ConnectionType;
        PlcIpAddress = settings.IpAddress;
        PlcSlot = settings.Slot;
        ModbusTcpPort = settings.ModbusPort > 0 ? settings.ModbusPort : 502;
        ModbusTcpUnitId = settings.ModbusUnitId;
        PlcPollingIntervalMs = settings.PollingIntervalMs;
        PlcTags.Clear();
        foreach (var t in settings.Tags)
            PlcTags.Add(t);
    }

    [RelayCommand]
    private async Task SavePlcSettingsAsync()
    {
        PlcIsSaving = true;
        PlcSaveMessage = "";
        var settings = new PlcSettingsModel
        {
            Enabled = PlcEnabled,
            ConnectionType = PlcConnectionType,
            IpAddress = PlcIpAddress,
            Slot = PlcSlot,
            ModbusPort = ModbusTcpPort,
            ModbusUnitId = ModbusTcpUnitId,
            PollingIntervalMs = PlcPollingIntervalMs,
            Tags = PlcTags.ToList()
        };
        var success = await _localService.PlcUpdateSettingsAsync(settings);
        PlcSaveMessage = success ? "Settings saved." : "Failed to save settings.";
        PlcSaveSuccess = success;
        PlcIsSaving = false;

        // Cancel any previous auto-clear, then schedule a new one
        _plcSaveMessageCts?.Cancel();
        _plcSaveMessageCts?.Dispose();
        _plcSaveMessageCts = new CancellationTokenSource();
        var delay = success ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(10);
        var cts = _plcSaveMessageCts;
        _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                System.Windows.Application.Current.Dispatcher.Invoke(() => PlcSaveMessage = "");
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private void OpenAddPlcTagDialog()
    {
        _editingPlcTag = null;
        PlcTagDialogTitle = "Add PLC Tag";
        NewPlcTagName = "";
        NewPlcTagDataType = IsModbusTcp ? "FLOAT32" : "REAL";
        NewPlcTagDisplayName = "";
        NewPlcTagUnit = "";
        NewPlcTagMultiplier = "1";
        PlcTagDialogError = "";
        ShowAddPlcTagDialog = true;
    }

    [RelayCommand]
    private void OpenEditPlcTagDialog(PlcTagDefinitionModel tag)
    {
        _editingPlcTag = tag;
        PlcTagDialogTitle = "Edit PLC Tag";
        NewPlcTagName = tag.Name;
        NewPlcTagDataType = tag.DataType;
        NewPlcTagDisplayName = tag.DisplayName ?? "";
        NewPlcTagUnit = tag.Unit ?? "";
        NewPlcTagMultiplier = tag.Multiplier.ToString("G");
        PlcTagDialogError = "";
        ShowAddPlcTagDialog = true;
    }

    [RelayCommand]
    private void CloseAddPlcTagDialog()
    {
        ShowAddPlcTagDialog = false;
        _editingPlcTag = null;
    }

    [RelayCommand]
    private void ConfirmAddPlcTag()
    {
        if (string.IsNullOrWhiteSpace(NewPlcTagName))
        {
            PlcTagDialogError = "Tag name is required.";
            return;
        }

        double multiplier = 1.0;
        if (ShowMultiplierField && !string.IsNullOrWhiteSpace(NewPlcTagMultiplier))
        {
            if (!double.TryParse(NewPlcTagMultiplier, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out multiplier) || multiplier == 0)
            {
                PlcTagDialogError = "Multiplier must be a non-zero number (e.g. 0.1, 0.01).";
                return;
            }
        }

        if (_editingPlcTag != null)
        {
            // Update in-place
            _editingPlcTag.Name = NewPlcTagName.Trim();
            _editingPlcTag.DataType = NewPlcTagDataType;
            _editingPlcTag.DisplayName = string.IsNullOrWhiteSpace(NewPlcTagDisplayName) ? null : NewPlcTagDisplayName.Trim();
            _editingPlcTag.Unit = string.IsNullOrWhiteSpace(NewPlcTagUnit) ? null : NewPlcTagUnit.Trim();
            _editingPlcTag.Multiplier = multiplier;
            // Force the ItemsControl to refresh by replacing the item
            int idx = PlcTags.IndexOf(_editingPlcTag);
            if (idx >= 0) { PlcTags.RemoveAt(idx); PlcTags.Insert(idx, _editingPlcTag); }
            _editingPlcTag = null;
        }
        else
        {
            PlcTags.Add(new PlcTagDefinitionModel
            {
                Name = NewPlcTagName.Trim(),
                DataType = NewPlcTagDataType,
                DisplayName = string.IsNullOrWhiteSpace(NewPlcTagDisplayName) ? null : NewPlcTagDisplayName.Trim(),
                Unit = string.IsNullOrWhiteSpace(NewPlcTagUnit) ? null : NewPlcTagUnit.Trim(),
                Multiplier = multiplier
            });
        }

        ShowAddPlcTagDialog = false;
    }

    [RelayCommand]
    private void RemovePlcTag(PlcTagDefinitionModel tag)
    {
        PlcTags.Remove(tag);
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

    // Tab indices: Site=0, Logging=1, FTP=2, Advanced=3, PLC=4
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 1) StartLogPolling();
        else StopLogPolling();

        if (value == 2) StartFtpStatusPolling();
        else StopFtpStatusPolling();

        if (value == 3) StartServiceStatusPolling();
        else StopServiceStatusPolling();

        if (value == 4) _ = LoadPlcSettingsAsync();
    }

    partial void OnPlcConnectionTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsControlLogix));
        OnPropertyChanged(nameof(IsModbusTcp));
        OnPropertyChanged(nameof(CurrentPlcDataTypes));
        OnPropertyChanged(nameof(PlcTagNameLabel));
        // Reset dialog data type to a valid default when protocol changes
        NewPlcTagDataType = IsModbusTcp ? "FLOAT32" : "REAL";
        OnPropertyChanged(nameof(ShowMultiplierField));
    }

    partial void OnNewPlcTagDataTypeChanged(string value)
    {
        OnPropertyChanged(nameof(ShowMultiplierField));
        if (!ShowMultiplierField) NewPlcTagMultiplier = "1";
    }

    partial void OnConfirmationPhraseChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
    partial void OnNewSiteNumberInputChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
    partial void OnTenantConfirmationPhraseChanged(string value) => OnPropertyChanged(nameof(IsTenantFormValid));
    partial void OnNewTenantIdInputChanged(string value) => OnPropertyChanged(nameof(IsTenantFormValid));
    partial void OnDialogServerNameChanged(string value) => OnPropertyChanged(nameof(IsFtpDialogSaveEnabled));
    partial void OnDialogFtpHostChanged(string value) => OnPropertyChanged(nameof(IsFtpDialogSaveEnabled));
}

/// <summary>Item model for the PLC Type ComboBox.</summary>
public record PlcConnectionTypeOption(string Value, string Display);
