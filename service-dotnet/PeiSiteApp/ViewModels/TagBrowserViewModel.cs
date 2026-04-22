using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeiSiteApp.Models;
using PeiSiteApp.Services;

namespace PeiSiteApp.ViewModels;

public partial class TagBrowserViewModel : ObservableObject
{
    private readonly LocalServiceClient _localService;

    public ObservableCollection<PlcDiscoveredTag> Tags { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _filterText = "";

    private IReadOnlyList<PlcDiscoveredTag> _allTags = Array.Empty<PlcDiscoveredTag>();

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    public TagBrowserViewModel(LocalServiceClient localService)
    {
        _localService = localService;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = "";

        var result = await _localService.PlcGetTagsAsync();
        if (result == null)
        {
            StatusMessage = "Service unavailable";
            IsLoading = false;
            return;
        }

        _allTags = result.Tags;
        ApplyFilter();

        if (_allTags.Count == 0)
            StatusMessage = "No tags discovered. Click Refresh to browse.";

        IsLoading = false;
    }

    // Called when plc:tags WebSocket event arrives
    public void ApplyTagList(IReadOnlyList<PlcDiscoveredTag> tags)
    {
        _allTags = tags;
        ApplyFilter();
        if (_allTags.Count == 0)
            StatusMessage = "No tags discovered.";
        else
            StatusMessage = "";
    }

    private void ApplyFilter()
    {
        var filter = FilterText.Trim().ToLowerInvariant();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allTags
            : _allTags.Where(t =>
                t.Name.ToLowerInvariant().Contains(filter) ||
                (t.Program?.ToLowerInvariant().Contains(filter) ?? false) ||
                t.DataType.ToLowerInvariant().Contains(filter)).ToList();

        Tags.Clear();
        foreach (var t in filtered)
            Tags.Add(t);
    }

    [RelayCommand]
    private async Task Refresh()
    {
        IsLoading = true;
        await _localService.PlcRefreshTagsAsync();
        // Wait a moment for the service to browse, then reload
        await Task.Delay(2000);
        await LoadAsync();
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = "";
    }
}
