using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    // FTP tab
    [ObservableProperty]
    private bool _ftpEnabled;

    [ObservableProperty]
    private string _ftpHost = "";

    [ObservableProperty]
    private string _ftpPath = "/";

    [ObservableProperty]
    private int _ftpScheduleMinutes = 15;

    [ObservableProperty]
    private string _ftpTestMessage = "";

    [ObservableProperty]
    private bool? _ftpTestSuccess;

    [ObservableProperty]
    private bool _isFtpTesting;

    [ObservableProperty]
    private bool _isFtpSaving;

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
    }

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

    [RelayCommand]
    private async Task TestFtpConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(FtpHost))
        {
            FtpTestMessage = "Please enter an FTP host address";
            FtpTestSuccess = false;
            return;
        }

        IsFtpTesting = true;
        FtpTestMessage = "";
        FtpTestSuccess = null;

        var result = await _localService.FtpTestConnectionAsync(FtpHost, FtpPath);
        FtpTestMessage = result.Message;
        FtpTestSuccess = result.Success;
        IsFtpTesting = false;
    }

    [RelayCommand]
    private async Task SaveFtpSettingsAsync()
    {
        IsFtpSaving = true;

        var success = await _localService.FtpUpdateConfigAsync(new
        {
            ftpEnabled = FtpEnabled,
            ftpHost = FtpHost,
            ftpPath = FtpPath,
            ftpPollInterval = FtpScheduleMinutes * 60
        });

        if (success)
        {
            FtpTestMessage = "FTP settings saved successfully";
            FtpTestSuccess = true;
        }
        else
        {
            FtpTestMessage = "Failed to save FTP settings";
            FtpTestSuccess = false;
        }

        IsFtpSaving = false;
    }

    partial void OnConfirmationPhraseChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
    partial void OnNewSiteNumberInputChanged(string value) => OnPropertyChanged(nameof(IsFormValid));
    partial void OnTenantConfirmationPhraseChanged(string value) => OnPropertyChanged(nameof(IsTenantFormValid));
    partial void OnNewTenantIdInputChanged(string value) => OnPropertyChanged(nameof(IsTenantFormValid));
}
