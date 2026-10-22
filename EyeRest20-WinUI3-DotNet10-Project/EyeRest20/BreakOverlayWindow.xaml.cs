using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace EyeRest20;

public sealed partial class BreakOverlayWindow : Window
{
    private readonly Action _onDismiss;
    private readonly DispatcherTimer _breathingTimer;
    private int _breathingPhase = 0; // 0=Inhale, 1=Hold, 2=Exhale

    public BreakOverlayWindow(int initialSeconds, Action onDismiss)
    {
        this.InitializeComponent();
        _onDismiss = onDismiss;

        // Make window fullscreen borderless
        var appWindow = this.AppWindow;
        if (appWindow != null)
        {
            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }

        UpdateRemainingSeconds(initialSeconds);

        // Breathing cycle text timer (changes every 4 seconds)
        _breathingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _breathingTimer.Tick += (s, e) =>
        {
            _breathingPhase = (_breathingPhase + 1) % 3;
            BreathingGuideText.Text = _breathingPhase switch
            {
                0 => "Inhale gently...",
                1 => "Hold breath softly...",
                _ => "Exhale and relax eyes..."
            };
        };
        _breathingTimer.Start();

        // Always stop the breathing timer when the window closes (including when the main
        // window closes it automatically at the end of the break), so it never ticks on a closed window.
        this.Closed += (s, e) => _breathingTimer.Stop();

        // Listen for Esc key to dismiss early
        if (this.Content is UIElement rootElement)
        {
            rootElement.KeyDown += RootElement_KeyDown;
        }
    }

    public void UpdateRemainingSeconds(int seconds)
    {
        OverlayCountdownText.Text = $"{seconds}s remaining";
    }

    private void RootElement_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            _breathingTimer.Stop();
            _onDismiss?.Invoke();
            this.Close();
        }
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        _breathingTimer.Stop();
        _onDismiss?.Invoke();
        this.Close();
    }
}