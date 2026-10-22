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

    // ---- Power throttling opt-out --------------------------------------------------
    // When the window is minimized / hidden in the tray, Windows 11 may put the process into
    // "Efficiency mode" (EcoQoS) and coarsen its timers, which can delay alerts. Opting out
    // keeps the background timer precise. (Cost is negligible: the app wakes twice a second.)
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE processInformation, uint processInformationSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    private const int ProcessPowerThrottling = 4;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;

    public App()
    {
        this.InitializeComponent();

        DisablePowerThrottling();

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

    private static void DisablePowerThrottling()
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
                StateMask = 0 // 0 = throttling OFF for the controlled features
            };
            if (!SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state,
                    (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
            {
                // Older Windows builds may not support IGNORE_TIMER_RESOLUTION - retry with execution speed only.
                state.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state,
                    (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EyeRest] Power throttling opt-out not available: {ex.Message}");
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

    /// <summary>Shows the main window again (also when it is hidden in the system tray).</summary>
    public static void RestoreMainWindow()
    {
        MainWindowInstance?.ShowFromTray();
    }

    public static void BringToForeground(Window window)
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        ShowWindow(hWnd, SW_RESTORE);
        SetForegroundWindow(hWnd);
        window.Activate();
    }
}
