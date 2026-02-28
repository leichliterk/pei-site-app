using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using PeiSiteApp.Services;
using PeiSiteApp.ViewModels;
using PeiSiteApp.Views;

namespace PeiSiteApp;

public partial class App : Application
{
    // Single-instance enforcement via named mutex + broadcast window message
    private static Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    public static readonly int WmShowApp =
        RegisterWindowMessage("PeiSiteApp_ShowWindow_9F3D2A1B");

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(false, "PeiSiteApp_SingleInstance_9F3D2A1B");
        bool isNewInstance;
        try { isNewInstance = _singleInstanceMutex.WaitOne(0); }
        catch (AbandonedMutexException) { isNewInstance = true; } // previous owner crashed; we can proceed
        if (!isNewInstance)
        {
            // Signal the running instance to restore itself, then exit
            PostMessage(HwndBroadcast, WmShowApp, IntPtr.Zero, IntPtr.Zero);
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }
        _ownsMutex = true;

        base.OnStartup(e);

        // Catch any startup crash so it's visible instead of silently disappearing
        DispatcherUnhandledException += (s, ex) =>
        {
            System.Windows.MessageBox.Show(
                $"Startup error:\n\n{ex.Exception.GetType().Name}: {ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                "PEI Site App – Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
            Shutdown(1);
        };

        // Initialize services
        var settingsManager = new SettingsManager();
        var localService = new LocalServiceClient();
        var siteApiService = new SiteApiService(settingsManager.Settings.ApiUrl);
        var ftpStatusService = new FtpStatusService(localService);

        // Create main view model
        _mainViewModel = new MainViewModel(settingsManager, localService, siteApiService, ftpStatusService);

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
        if (_ownsMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
