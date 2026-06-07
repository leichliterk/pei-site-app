using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PeiSiteApp.ViewModels;

namespace PeiSiteApp.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;

    // MSGFLT_ALLOW = 1: permit the message through UIPI regardless of sender privilege
    [DllImport("user32.dll")] private static extern bool ChangeWindowMessageFilterEx(
        IntPtr hwnd, uint msg, uint action, IntPtr pChangeFilterStruct);

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;

        // Hook WndProc to receive the single-instance activation message
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);

        // Allow WmShowApp through UIPI so a non-elevated shortcut launch can
        // signal an elevated running instance to show its window (and vice versa).
        // Without this, PostMessage from a lower-privilege process is silently
        // dropped by Windows, so the shortcut appears to do nothing.
        ChangeWindowMessageFilterEx(handle, (uint)App.WmShowApp, 1, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == App.WmShowApp)
        {
            ShowAndActivate();
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
