using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace EyeRest20;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// Registers AppNotificationManager for native Windows toast notifications.
/// </summary>
public partial class App : Application
{
    public static MainWindow? MainWindowInstance { get; private set; }

    // Win32 APIs for bringing the window to the foreground when clicking a notification
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    public App()
    {
        this.InitializeComponent();

        // 1. Register for Windows App SDK AppNotifications
        try
        {
            AppNotificationManager notificationManager = AppNotificationManager.Default;
            notificationManager.NotificationInvoked += OnNotificationInvoked;
            notificationManager.Register();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EyeRest] Notification registration notice: {ex.Message}");
        }
    }

    /// <summary>
    /// Invoked when the application is launched normally or activated.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();

        // Check if app was launched directly by clicking a Windows notification while closed
        var appArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
        if (appArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.AppNotification &&
            appArgs.Data is AppNotificationActivatedEventArgs notificationArgs)
        {
            HandleNotificationActivation(notificationArgs);
        }
    }

    /// <summary>
    /// Triggered whenever the user clicks a native Windows notification toast or button while app is running.
    /// </summary>
    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // Must run on UI Thread
        MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            RestoreMainWindow();
            HandleNotificationActivation(args);
        });
    }

    private void HandleNotificationActivation(AppNotificationActivatedEventArgs args)
    {
        string action = args.Arguments.TryGetValue("action", out string? act) ? act : "";

        if (action == "start_break")
        {
            MainWindowInstance?.TriggerBreakDirectly();
        }
        else if (action == "snooze")
        {
            MainWindowInstance?.SnoozeWorkTimer(TimeSpan.FromMinutes(2));
        }
    }

    public static void RestoreMainWindow()
    {
        if (MainWindowInstance == null) return;

        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindowInstance);
        ShowWindow(hWnd, SW_RESTORE);
        SetForegroundWindow(hWnd);
        MainWindowInstance.Activate();
    }
}