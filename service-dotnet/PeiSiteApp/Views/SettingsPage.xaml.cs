using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PeiSiteApp.Models;
using PeiSiteApp.ViewModels;

namespace PeiSiteApp.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is SettingsViewModel vm)
        {
            vm.LogEntries.CollectionChanged += (_, _) =>
                Dispatcher.BeginInvoke(
                    new Action(() => LogScrollViewer.ScrollToEnd()),
                    DispatcherPriority.Background);
        }
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
