using System.Windows;
using System.Windows.Controls;
using PeiSiteApp.Models;
using PeiSiteApp.ViewModels;

namespace PeiSiteApp.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is FtpDirectoryNode node)
        {
            if (DataContext is SettingsViewModel vm && !node.HasLoadedChildren)
            {
                vm.ExpandBrowseNodeCommand.Execute(node);
            }
        }
    }

    private void BrowseTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is SettingsViewModel vm && e.NewValue is FtpDirectoryNode node)
        {
            vm.SelectedBrowseNode = node;
        }
    }
}
