using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeiSiteApp.Models;
using PeiSiteApp.Services;

namespace PeiSiteApp.ViewModels;

public partial class NotificationsViewModel : ObservableObject
{
    private readonly LocalServiceClient _localService;
    private readonly MainViewModel _mainViewModel;

    public ObservableCollection<NotificationResponse> Notifications { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _emptyMessage = "No notifications";

    public NotificationsViewModel(LocalServiceClient localService, MainViewModel mainViewModel)
    {
        _localService = localService;
        _mainViewModel = mainViewModel;
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
        notification.Read = true;
        // Trigger a full reload so unread count and styling update
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        await _localService.MarkAllNotificationsReadAsync();
        await LoadAsync();
    }
}
