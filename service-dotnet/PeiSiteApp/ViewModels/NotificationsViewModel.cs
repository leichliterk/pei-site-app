using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeiSiteApp.Models;
using PeiSiteApp.Services;

namespace PeiSiteApp.ViewModels;

public partial class NotificationsViewModel : ObservableObject
{
    private readonly LocalServiceClient _localService;
    private readonly MainViewModel _mainViewModel;
    private readonly OtaService _otaService;

    public ObservableCollection<NotificationResponse> Notifications { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _emptyMessage = "No notifications";

    // Release notes dialog state
    [ObservableProperty] private bool _showReleaseNotesDialog;
    [ObservableProperty] private string _releaseNotesVersion = "";
    [ObservableProperty] private string _releaseNotesText = "";

    // OTA dialog state
    [ObservableProperty] private bool _showOtaDialog;
    [ObservableProperty] private NotificationResponse? _currentOtaNotification;
    [ObservableProperty] private string _otaStatus = "confirm"; // confirm | downloading | error | launching
    [ObservableProperty] private double _otaDownloadProgress;
    [ObservableProperty] private string _otaStatusMessage = "";

    public string OtaVersion => CurrentOtaNotification?.Data?.Version ?? "";
    public string OtaNotes => CurrentOtaNotification?.Data?.Notes ?? "";
    public string OtaSizeDisplay => FormatSize(CurrentOtaNotification?.Data?.Size);

    partial void OnCurrentOtaNotificationChanged(NotificationResponse? value)
    {
        OnPropertyChanged(nameof(OtaVersion));
        OnPropertyChanged(nameof(OtaNotes));
        OnPropertyChanged(nameof(OtaSizeDisplay));
    }

    public NotificationsViewModel(LocalServiceClient localService, MainViewModel mainViewModel, string apiUrl)
    {
        _localService = localService;
        _mainViewModel = mainViewModel;
        _otaService = new OtaService(apiUrl);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        var result = await _localService.GetNotificationsAsync();
        IsLoading = false;

        Notifications.Clear();
        if (result == null)
        {
            EmptyMessage = "Could not reach service";
            return;
        }

        foreach (var n in result.Notifications)
            Notifications.Add(n);

        EmptyMessage = "No notifications";
        _mainViewModel.RefreshNotificationCount();
    }

    [RelayCommand]
    private async Task MarkReadAsync(NotificationResponse notification)
    {
        if (notification.Read) return;
        await _localService.MarkNotificationReadAsync(notification.Id);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        await _localService.MarkAllNotificationsReadAsync();
        await LoadAsync();
    }

    // --- Release notes ---

    [RelayCommand]
    private void OpenReleaseNotesDialog(NotificationResponse notification)
    {
        ReleaseNotesVersion = notification.Data?.Version ?? "";
        ReleaseNotesText = notification.Data?.Notes ?? "";
        ShowReleaseNotesDialog = true;
    }

    [RelayCommand]
    private void CloseReleaseNotesDialog() => ShowReleaseNotesDialog = false;

    // --- OTA ---

    [RelayCommand]
    private void OpenOtaDialog(NotificationResponse notification)
    {
        CurrentOtaNotification = notification;
        OtaStatus = "confirm";
        OtaDownloadProgress = 0;
        OtaStatusMessage = "";
        ShowOtaDialog = true;
    }

    [RelayCommand]
    private async Task OtaDeclineAsync()
    {
        if (CurrentOtaNotification?.Data == null) return;
        await _localService.OtaRespondAsync(CurrentOtaNotification.Data.ReleaseId ?? "", false);
        ShowOtaDialog = false;
        await MarkReadAsync(CurrentOtaNotification);
    }

    [RelayCommand]
    private async Task OtaAcceptAsync()
    {
        var notification = CurrentOtaNotification;
        if (notification?.Data == null) return;
        var data = notification.Data;

        OtaStatus = "downloading";
        OtaDownloadProgress = 0;
        OtaStatusMessage = "Contacting server...";

        // 1. Emit ota:response accepted=true and wait for ack
        var responded = await _localService.OtaRespondAsync(data.ReleaseId ?? "", true);
        if (!responded)
        {
            OtaStatus = "error";
            OtaStatusMessage = "Server did not acknowledge the request. Please try again.";
            return;
        }

        // 2. Stream download with progress
        OtaStatusMessage = "Downloading update...";
        var progress = new Progress<double>(p =>
        {
            OtaDownloadProgress = p;
            OtaStatusMessage = $"Downloading... {p:F0}%";
        });

        var result = await _otaService.DownloadAsync(
            data.ReleaseId ?? "",
            data.DownloadToken ?? "",
            data.Sha256,
            progress);

        if (!result.Success)
        {
            OtaStatus = "error";
            OtaStatusMessage = result.Error ?? "Download failed.";
            return;
        }

        // 3. Emit ota:installed before launching
        OtaStatus = "launching";
        OtaStatusMessage = "Launching installer...";
        await _localService.OtaInstalledAsync(data.ReleaseId ?? "", data.Version ?? "");

        // 4. Launch installer — it will stop the service and close the app
        Process.Start(new ProcessStartInfo(result.FilePath!) { UseShellExecute = true });
        ShowOtaDialog = false;
    }

    private static string FormatSize(long? bytes)
    {
        if (bytes == null) return "";
        double mb = bytes.Value / 1_048_576.0;
        return mb >= 1 ? $"{mb:F1} MB" : $"{bytes.Value / 1024.0:F0} KB";
    }
}
