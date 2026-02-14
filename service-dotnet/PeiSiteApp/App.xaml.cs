using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using PeiSiteApp.Services;
using PeiSiteApp.ViewModels;
using PeiSiteApp.Views;

namespace PeiSiteApp;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize services
        var settingsManager = new SettingsManager();
        var localService = new LocalServiceClient();
        var webSocketService = new WebSocketService();
        var siteApiService = new SiteApiService(settingsManager.Settings.ApiUrl);
        var ftpStatusService = new FtpStatusService(localService);

        // Create main view model
        _mainViewModel = new MainViewModel(settingsManager, localService, webSocketService, siteApiService, ftpStatusService);

        // Create and show main window
        _mainWindow = new MainWindow
        {
            DataContext = _mainViewModel
        };
        _mainWindow.Show();

        // Set up system tray
        SetupTrayIcon();

        // Initialize (check service health, connect, etc.)
        await _mainViewModel.InitializeAsync();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Resources/icon.ico")),
            ToolTipText = "PEI Site App"
        };

        // Double-click to show window
        _trayIcon.TrayMouseDoubleClick += (s, e) => ShowMainWindow();

        // Context menu
        var contextMenu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem { Header = "Show App" };
        showItem.Click += (s, e) => ShowMainWindow();
        contextMenu.Items.Add(showItem);

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (s, e) =>
        {
            ShowMainWindow();
            _mainViewModel?.NavigateSettings();
        };
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (s, e) => QuitApplication();
        contextMenu.Items.Add(quitItem);

        _trayIcon.ContextMenu = contextMenu;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.ShowAndActivate();
        }
    }

    private void QuitApplication()
    {
        _mainViewModel?.Shutdown();
        _trayIcon?.Dispose();
        _mainWindow?.ForceClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
