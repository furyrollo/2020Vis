using System;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace EyeRest20;

/// <summary>
/// Manages native Windows Toast notifications using Windows App SDK AppNotificationManager.
/// </summary>
public static class NotificationManager
{
    /// <summary>
    /// Sends a native Windows notification alerting the user that 20 minutes elapsed and it is time for an eye break.
    /// Includes action buttons to start break immediately or snooze.
    /// </summary>
    public static void SendWorkCompletedNotification(int breakDurationSeconds)
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText("Time to rest your eyes! (20-20-20 Rule)")
                .AddText($"Look at something 20 feet (6m) away for {breakDurationSeconds} seconds.")
                .AddButton(new AppNotificationButton("Start Break Now")
                    .AddArgument("action", "start_break"))
                .AddButton(new AppNotificationButton("Snooze 2m")
                    .AddArgument("action", "snooze"))
                .SetScenario(AppNotificationScenario.Alarm) // High visibility alarm style
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EyeRest] Toast notification error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a native Windows notification indicating the break has concluded and work timer resumed.
    /// </summary>
    public static void SendBreakCompletedNotification()
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText("Break Complete! Eyes Refreshed")
                .AddText("Work timer has resumed for 20 minutes. Keep up the great eye health habits!")
                .AddButton(new AppNotificationButton("Open EyeRest")
                    .AddArgument("action", "open_app"))
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EyeRest] Toast notification error: {ex.Message}");
        }
    }
}