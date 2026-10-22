using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EyeRest20;

public enum TimerState
{
    Idle,
    Working,
    Breaking,
    Paused
}

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private TimerState _state = TimerState.Idle;
    private TimerState _previousState = TimerState.Working;

    private int _remainingSeconds;
    private int _workDurationSeconds = 20 * 60; // 20 minutes default
    private int _breakDurationSeconds = 20;      // 20 seconds default
    private int _completedRests = 0;
    private int _currentCycle = 1;

    private BreakOverlayWindow? _overlayWindow;

    public MainWindow()
    {
        this.InitializeComponent();

        // Configure WinUI 3 Window Title and Size
        Title = "EyeRest 20-20-20";
        var appWindow = this.AppWindow;
        appWindow.Resize(new Windows.Graphics.SizeInt32(680, 840));

        // Setup sound dropdown items
        PopulateSoundComboBoxes();

        // Setup high-precision DispatcherTimer (1 second tick)
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += Timer_Tick;

        _remainingSeconds = _workDurationSeconds;
        UpdateUI();
    }

    private void PopulateSoundComboBoxes()
    {
        var sounds = SoundPlayer.AvailableSounds;
        foreach (var sound in sounds)
        {
            BreakSoundComboBox.Items.Add(sound);
            ResumeSoundComboBox.Items.Add(sound);
        }
        BreakSoundComboBox.SelectedIndex = 0; // Tibetan Bowl
        ResumeSoundComboBox.SelectedIndex = 1; // Zen Bell
    }

    private void Timer_Tick(object? sender, object e)
    {
        if (_state != TimerState.Working && _state != TimerState.Breaking) return;

        _remainingSeconds--;

        if (_remainingSeconds <= 0)
        {
            if (_state == TimerState.Working)
            {
                OnWorkCompleted();
            }
            else if (_state == TimerState.Breaking)
            {
                OnBreakCompleted();
            }
        }

        UpdateUI();
    }

    private void OnWorkCompleted()
    {
        // 1. Play user-selected Break alert sound
        string selectedSound = BreakSoundComboBox.SelectedItem?.ToString() ?? "Tibetan Bowl";
        SoundPlayer.PlaySound(selectedSound);

        // 2. Dispatch native Windows toast notification
        if (NativeNotificationsToggle.IsOn)
        {
            NotificationManager.SendWorkCompletedNotification(_breakDurationSeconds);
        }

        // 3. Launch fullscreen screensaver curtain overlay if enabled
        if (FullscreenOverlayToggle.IsOn)
        {
            ShowBreakOverlay();
        }

        // 4. Transition to Breaking state
        _state = TimerState.Breaking;
        _remainingSeconds = _breakDurationSeconds;
    }

    private void OnBreakCompleted()
    {
        _completedRests++;

        // 1. Close overlay if open
        CloseBreakOverlay();

        // 2. Play user-selected Resume alert sound
        string selectedSound = ResumeSoundComboBox.SelectedItem?.ToString() ?? "Zen Bell";
        SoundPlayer.PlaySound(selectedSound);

        // 3. Dispatch native Windows toast notification
        if (NativeNotificationsToggle.IsOn)
        {
            NotificationManager.SendBreakCompletedNotification();
        }

        // 4. Auto-loop check
        if (AutoLoopToggle.IsOn)
        {
            _currentCycle++;
            _state = TimerState.Working;
            _remainingSeconds = _workDurationSeconds;
        }
        else
        {
            _state = TimerState.Idle;
            _remainingSeconds = _workDurationSeconds;
            _timer.Stop();
        }
    }

    private void ShowBreakOverlay()
    {
        CloseBreakOverlay();
        _overlayWindow = new BreakOverlayWindow(_breakDurationSeconds, () =>
        {
            // If user dismissed overlay early
            OnBreakCompleted();
            UpdateUI();
        });
        _overlayWindow.Activate();
    }

    private void CloseBreakOverlay()
    {
        if (_overlayWindow != null)
        {
            _overlayWindow.Close();
            _overlayWindow = null;
        }
    }

    private void UpdateUI()
    {
        int minutes = _remainingSeconds / 60;
        int seconds = _remainingSeconds % 60;
        TimerDisplay.Text = $"{minutes:D2}:{seconds:D2}";

        // Update progress ring
        int total = _state == TimerState.Breaking ? _breakDurationSeconds : _workDurationSeconds;
        if (total > 0)
        {
            int elapsed = total - _remainingSeconds;
            TimerProgressRing.Value = Math.Clamp((double)elapsed / total * 100, 0, 100);
        }

        StatsSummaryText.Text = $"Cycle #{_currentCycle} • {_completedRests} eye rest(s) completed today";

        switch (_state)
        {
            case TimerState.Idle:
                StartPauseButton.Content = "Start Work Cycle";
                TimerPhaseLabel.Text = "Ready to Focus";
                StatusBadgeText.Text = "Idle";
                StatusBadgeBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 241, 245, 249));
                StatusBadgeText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 71, 85, 105));
                break;
            case TimerState.Working:
                StartPauseButton.Content = "Pause";
                TimerPhaseLabel.Text = "Focusing Eyes";
                StatusBadgeText.Text = "Work Cycle Active";
                StatusBadgeBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 224, 242, 254));
                StatusBadgeText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 3, 105, 161));
                break;
            case TimerState.Breaking:
                StartPauseButton.Content = "Pause Break";
                TimerPhaseLabel.Text = "Resting Eyes (Look 20ft away)";
                StatusBadgeText.Text = "Rest Break in Progress";
                StatusBadgeBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 209, 250, 229));
                StatusBadgeText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 4, 120, 87));
                break;
            case TimerState.Paused:
                StartPauseButton.Content = "Resume";
                TimerPhaseLabel.Text = "Timer Paused";
                StatusBadgeText.Text = "Paused";
                StatusBadgeBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 254, 243, 199));
                StatusBadgeText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 180, 83, 9));
                break;
        }

        // Keep overlay in sync if visible
        _overlayWindow?.UpdateRemainingSeconds(_remainingSeconds);
    }

    private void StartPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state == TimerState.Idle)
        {
            _state = TimerState.Working;
            _remainingSeconds = _workDurationSeconds;
            _timer.Start();
        }
        else if (_state == TimerState.Working || _state == TimerState.Breaking)
        {
            _previousState = _state;
            _state = TimerState.Paused;
            _timer.Stop();
        }
        else if (_state == TimerState.Paused)
        {
            _state = _previousState;
            _timer.Start();
        }
        UpdateUI();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _state = TimerState.Idle;
        _remainingSeconds = _workDurationSeconds;
        CloseBreakOverlay();
        UpdateUI();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state == TimerState.Breaking)
        {
            OnBreakCompleted();
        }
        else
        {
            _state = TimerState.Working;
            OnWorkCompleted();
        }
        UpdateUI();
    }

    private void Duration_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (FastTestModeToggle?.IsOn == true) return;

        if (sender == WorkDurationNumberBox && !double.IsNaN(args.NewValue))
        {
            _workDurationSeconds = (int)args.NewValue * 60;
            if (_state == TimerState.Idle)
            {
                _remainingSeconds = _workDurationSeconds;
                UpdateUI();
            }
        }
        else if (sender == BreakDurationNumberBox && !double.IsNaN(args.NewValue))
        {
            _breakDurationSeconds = (int)args.NewValue;
        }
    }

    private void FastTestModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (FastTestModeToggle.IsOn)
        {
            _workDurationSeconds = 5;  // 5 seconds
            _breakDurationSeconds = 5; // 5 seconds
        }
        else
        {
            _workDurationSeconds = (int)(WorkDurationNumberBox?.Value ?? 20) * 60;
            _breakDurationSeconds = (int)(BreakDurationNumberBox?.Value ?? 20);
        }

        if (_state == TimerState.Idle)
        {
            _remainingSeconds = _workDurationSeconds;
            UpdateUI();
        }
    }

    private void BreakSoundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private void ResumeSoundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void TestBreakSound_Click(object sender, RoutedEventArgs e)
    {
        string selected = BreakSoundComboBox.SelectedItem?.ToString() ?? "Tibetan Bowl";
        SoundPlayer.PlaySound(selected);
    }

    private void TestResumeSound_Click(object sender, RoutedEventArgs e)
    {
        string selected = ResumeSoundComboBox.SelectedItem?.ToString() ?? "Zen Bell";
        SoundPlayer.PlaySound(selected);
    }

    private void PreviewOverlay_Click(object sender, RoutedEventArgs e)
    {
        ShowBreakOverlay();
    }

    public void TriggerBreakDirectly()
    {
        OnWorkCompleted();
        UpdateUI();
    }

    public void SnoozeWorkTimer(TimeSpan duration)
    {
        _remainingSeconds += (int)duration.TotalSeconds;
        UpdateUI();
    }
}