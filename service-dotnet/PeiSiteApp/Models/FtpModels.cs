using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PeiSiteApp.Models;

public partial class FtpDirectoryNode : ObservableObject
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isLoading;

    public bool HasLoadedChildren { get; set; }

    public ObservableCollection<FtpDirectoryNode> Children { get; set; } = new();
}
