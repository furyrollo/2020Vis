using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;

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

    // Wall-clock deadline of the current phase (Environment.TickCount64, ms).
    // Counting down "ticks" can drift or stall when Windows throttles a hidden/minimized
    // app or the PC sleeps, which made some alerts late or missed. Comparing against a real
    // deadline guarantees the alert fires as soon as the time has actually elapsed.
    private long _phaseEndTick;

    private BreakOverlayWindow? _overlayWindow;

    // System tray
    private TrayIcon? _trayIcon;
    private bool _isExiting;
    private bool _isHiddenInTray;
    private bool _isRestoringFromTray;

    // Settings keys
    private const string BreakSoundSettingKey = "BreakSound";
    private const string ResumeSoundSettingKey = "ResumeSound";
    private const string MinimizeToTraySettingKey = "MinimizeToTray";
    private const string CloseToTraySettingKey = "CloseToTray";
    private bool _loadingSettings = true; // ignore control events fired during InitializeComponent

    public MainWindow()
    {
        this.InitializeComponent();

        // Configure WinUI 3 Window Title and Size
        Title = "EyeRest 20-20-20";
        var appWindow = this.AppWindow;
        appWindow.Resize(new Windows.Graphics.SizeInt32(680, 840));

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "EyeRest.ico");
        try { if (File.Exists(iconPath)) appWindow.SetIcon(iconPath); } catch { /* cosmetic only */ }

        // Setup sound dropdown items (built-in + custom tones) and restore saved choices
        SoundPlayer.RefreshCustomSounds();
        PopulateSoundComboBoxes(
            LoadSetting(BreakSoundSettingKey) ?? "Tibetan Bowl",
            LoadSetting(ResumeSoundSettingKey) ?? "Zen Bell");
        MinimizeToTrayToggle.IsOn = LoadSetting(MinimizeToTraySettingKey) != "False";
        CloseToTrayToggle.IsOn = LoadSetting(CloseToTraySettingKey) == "True";
        _loadingSettings = false;
        UpdateCustomSoundsInfo();

        // UI refresh timer. Actual timing is based on _phaseEndTick, so a delayed tick
        // never causes a missed alert - it just fires on the next tick.
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += Timer_Tick;

        // System tray icon
        InitializeTrayIcon(iconPath);
        appWindow.Changed += AppWindow_Changed;
        appWindow.Closing += AppWindow_Closing;
        this.Closed += MainWindow_Closed;

        _remainingSeconds = _workDurationSeconds;
        UpdateUI();
    }

    // =====================================================================================
    //  Sounds
    // =====================================================================================

    private void PopulateSoundComboBoxes(string breakSelection, string resumeSelection)
    {
        var sounds = SoundPlayer.AvailableSounds;

        BreakSoundComboBox.Items.Clear();
        ResumeSoundComboBox.Items.Clear();
        foreach (var sound in sounds)
        {
            BreakSoundComboBox.Items.Add(sound);
            ResumeSoundComboBox.Items.Add(sound);
        }

        BreakSoundComboBox.SelectedItem = sounds.Contains(breakSelection) ? breakSelection : "Tibetan Bowl";
        ResumeSoundComboBox.SelectedItem = sounds.Contains(resumeSelection) ? resumeSelection : "Zen Bell";
    }

    private string SelectedBreakSound => BreakSoundComboBox.SelectedItem?.ToString() ?? "Tibetan Bowl";
    private string SelectedResumeSound => ResumeSoundComboBox.SelectedItem?.ToString() ?? "Zen Bell";

    private void RefreshSoundLists()
    {
        string breakSel = SelectedBreakSound;
        string resumeSel = SelectedResumeSound;

        _loadingSettings = true;
        SoundPlayer.RefreshCustomSounds();
        PopulateSoundComboBoxes(breakSel, resumeSel);
        _loadingSettings = false;

        UpdateCustomSoundsInfo();
    }

    private void UpdateCustomSoundsInfo()
    {
        int count = SoundPlayer.AvailableSounds.Count(s => s.StartsWith(SoundPlayer.CustomPrefix, StringComparison.Ordinal));
        CustomSoundsInfoText.Text =
            "Use your own sound file (" + string.Join(", ", SoundPlayer.SupportedExtensions) + "). " +
            "Added tones appear in both lists above as \"Custom: name\". " +
            $"{count} custom tone(s) found.";
    }

    private void ShowCustomSoundsStatus(string message)
    {
        CustomSoundsStatusText.Text = message;
        CustomSoundsStatusText.Visibility = Visibility.Visible;
    }

    private async void AddCustomSound_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                ViewMode = PickerViewMode.List
            };
            foreach (var ext in SoundPlayer.SupportedExtensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            // Required for pickers in WinUI 3 desktop apps
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile? file = await picker.PickSingleFileAsync();
            if (file == null) return;

            string displayName = SoundPlayer.ImportCustomSound(file.Path);
            RefreshSoundLists();
            ShowCustomSoundsStatus($"Added \"{displayName}\". Select it in the Break Alert or Resume Work list above.");
        }
        catch (Exception ex)
        {
            ShowCustomSoundsStatus($"Could not add sound file: {ex.Message}");
        }
    }

    private void OpenCustomSoundsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = SoundPlayer.UserCustomSoundsFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            ShowCustomSoundsStatus($"Copy your sound files into this folder, then press \"Refresh List\":\n{folder}");
        }
        catch (Exception ex)
        {
            ShowCustomSoundsStatus($"Could not open folder: {ex.Message}");
        }
    }

    private void RefreshCustomSounds_Click(object sender, RoutedEventArgs e)
    {
        RefreshSoundLists();
        ShowCustomSoundsStatus("Sound list refreshed.");
    }

    private void BreakSoundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingSettings && BreakSoundComboBox.SelectedItem != null)
            SaveSetting(BreakSoundSettingKey, SelectedBreakSound);
    }

    private void ResumeSoundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingSettings && ResumeSoundComboBox.SelectedItem != null)
            SaveSetting(ResumeSoundSettingKey, SelectedResumeSound);
    }

    private void TestBreakSound_Click(object sender, RoutedEventArgs e)
    {
        SoundPlayer.PlaySound(SelectedBreakSound);
    }

    private void TestResumeSound_Click(object sender, RoutedEventArgs e)
    {
        SoundPlayer.PlaySound(SelectedResumeSound);
    }

    // =====================================================================================
    //  Timer
    // =====================================================================================

    private void StartPhase(int seconds)
    {
        _remainingSeconds = seconds;
        _phaseEndTick = Environment.TickCount64 + seconds * 1000L;
    }

    private void Timer_Tick(object? sender, object e)
    {
        if (_state != TimerState.Working && _state != TimerState.Breaking) return;

        long msLeft = _phaseEndTick - Environment.TickCount64;
        _remainingSeconds = (int)Math.Max(0, (msLeft + 999) / 1000);

        if (msLeft <= 0)
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
        // 1. Transition to Breaking state FIRST so a failure below can never stall the timer
        _state = TimerState.Breaking;
        StartPhase(_breakDurationSeconds);
        if (!_timer.IsEnabled) _timer.Start();

        // 2. Play user-selected Break alert sound (always - independent of toast/overlay)
        SoundPlayer.PlaySound(SelectedBreakSound);

        // 3. Dispatch native Windows toast notification (silent - no extra Windows tone)
        if (NativeNotificationsToggle.IsOn)
        {
            NotificationManager.SendWorkCompletedNotification(_breakDurationSeconds);
        }

        // 4. Launch fullscreen screensaver curtain overlay if enabled
        if (FullscreenOverlayToggle.IsOn)
        {
            try
            {
                ShowBreakOverlay();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EyeRest] Overlay error: {ex.Message}");
            }
        }
    }

    private void OnBreakCompleted()
    {
        if (_state != TimerState.Breaking && _state != TimerState.Paused) return;

        _completedRests++;

        // 1. Next state FIRST so a failure below can never stall the timer
        if (AutoLoopToggle.IsOn)
        {
            _currentCycle++;
            _state = TimerState.Working;
            StartPhase(_workDurationSeconds);
            if (!_timer.IsEnabled) _timer.Start();
        }
        else
        {
            _state = TimerState.Idle;
            _remainingSeconds = _workDurationSeconds;
            _timer.Stop();
        }

        // 2. Play user-selected Resume alert sound (always)
        SoundPlayer.PlaySound(SelectedResumeSound);

        // 3. Close overlay if open
        CloseBreakOverlay();

        // 4. Dispatch native Windows toast notification (silent - no extra Windows tone)
        if (NativeNotificationsToggle.IsOn)
        {
            NotificationManager.SendBreakCompletedNotification();
        }
    }

    private void ShowBreakOverlay()
    {
        CloseBreakOverlay();
        _overlayWindow = new BreakOverlayWindow(_breakDurationSeconds, () =>
        {
            // If user dismissed overlay early
            _overlayWindow = null;
            if (_state == TimerState.Breaking ||
                (_state == TimerState.Paused && _previousState == TimerState.Breaking))
            {
                OnBreakCompleted();
            }
            UpdateUI();
        });
        _overlayWindow.Activate();
    }

    private void CloseBreakOverlay()
    {
        var overlay = _overlayWindow;
        _overlayWindow = null;
        if (overlay != null)
        {
            try { overlay.Close(); } catch { /* already closed */ }
        }
    }

    private void UpdateUI()
    {
        int minutes = _remainingSeconds / 60;
        int seconds = _remainingSeconds % 60;
        TimerDisplay.Text = $"{minutes:D2}:{seconds:D2}";

        // Update progress ring
        int total = _state == TimerState.Breaking || (_state == TimerState.Paused && _previousState == TimerState.Breaking)
            ? _breakDurationSeconds
            : _workDurationSeconds;
        if (total > 0)
        {
            int elapsed = total - _remainingSeconds;
            TimerProgressRing.Value = Math.Clamp((double)elapsed / total * 100, 0, 100);
        }

        StatsSummaryText.Text = $"Cycle #{_currentCycle} • {_completedRests} eye rest(s) completed today";

        string trayStatus;
        switch (_state)
        {
            case TimerState.Idle:
                StartPauseButton.Content = "Start Work Cycle";
                TimerPhaseLabel.Text = "Ready to Focus";
                StatusBadgeText.Text = "Idle";
                StatusBadgeBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 241, 245, 249));
                StatusBadgeText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 71, 85, 105));
                trayStatus = "Idle";
                break;
            case TimerState.Working:
                StartPauseButton.Content = "Pause";
                TimerPhaseLabel.Text = "Focusing Eyes";
                StatusBadgeText.Text = "Work Cycle Active";
                StatusBadgeBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 224, 242, 254));
                StatusBadgeText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 3, 105, 161));
                trayStatus = $"Next eye break in {minutes:D2}:{seconds:D2}";
                break;
            case TimerState.Breaking:
                StartPauseButton.Content = "Pause Break";
                TimerPhaseLabel.Text = "Resting Eyes (Look 20ft away)";
                StatusBadgeText.Text = "Rest Break in Progress";
                StatusBadgeBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 209, 250, 229));
                StatusBadgeText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 4, 120, 87));
                trayStatus = $"Resting eyes - {_remainingSeconds}s left";
                break;
            default: // Paused
                StartPauseButton.Content = "Resume";
                TimerPhaseLabel.Text = "Timer Paused";
                StatusBadgeText.Text = "Paused";
                StatusBadgeBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 254, 243, 199));
                StatusBadgeText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 180, 83, 9));
                trayStatus = "Paused";
                break;
        }

        _trayIcon?.SetTooltip($"EyeRest 20-20-20\n{trayStatus}");

        // Keep overlay in sync if visible
        _overlayWindow?.UpdateRemainingSeconds(_remainingSeconds);
    }

    private void ToggleStartPause()
    {
        if (_state == TimerState.Idle)
        {
            _state = TimerState.Working;
            StartPhase(_workDurationSeconds);
            _timer.Start();
        }
        else if (_state == TimerState.Working || _state == TimerState.Breaking)
        {
            // Freeze the remaining time while paused
            long msLeft = _phaseEndTick - Environment.TickCount64;
            _remainingSeconds = (int)Math.Max(1, (msLeft + 999) / 1000);
            _previousState = _state;
            _state = TimerState.Paused;
            _timer.Stop();
        }
        else if (_state == TimerState.Paused)
        {
            _state = _previousState;
            StartPhase(_remainingSeconds);
            _timer.Start();
        }
        UpdateUI();
    }

    private void StartPauseButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleStartPause();
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
        int extra = (int)duration.TotalSeconds;
        _remainingSeconds += extra;
        _phaseEndTick += extra * 1000L;
        UpdateUI();
    }

    // =====================================================================================
    //  System tray
    // =====================================================================================

    private void InitializeTrayIcon(string iconPath)
    {
        try
        {
            _trayIcon = new TrayIcon(iconPath)
            {
                ToggleTimerLabelProvider = () => _state switch
                {
                    TimerState.Idle => "Start Work Cycle",
                    TimerState.Paused => "Resume Timer",
                    _ => "Pause Timer"
                }
            };
            _trayIcon.OpenRequested += ShowFromTray;
            _trayIcon.ToggleTimerRequested += ToggleStartPause;
            _trayIcon.ExitRequested += ExitApplication;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EyeRest] Tray icon unavailable: {ex.Message}");
            _trayIcon = null;
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_isExiting || _isHiddenInTray || _isRestoringFromTray || _trayIcon == null) return;
        if (!MinimizeToTrayToggle.IsOn) return;

        if (sender.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            // Defer out of the Changed callback to avoid re-entrancy
            _isHiddenInTray = true;
            DispatcherQueue.TryEnqueue(HideToTray);
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting || _trayIcon == null) return;

        if (CloseToTrayToggle.IsOn)
        {
            args.Cancel = true;
            HideToTray();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // Real close: clean up so no "ghost" tray icon or orphan overlay is left behind.
        _timer.Stop();
        CloseBreakOverlay();
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    /// <summary>Hides the window to the system tray. The timer keeps running.</summary>
    public void HideToTray()
    {
        if (_trayIcon == null)
        {
            // No tray available - fall back to a normal minimize
            (AppWindow.Presenter as OverlappedPresenter)?.Minimize();
            return;
        }

        _isHiddenInTray = true;
        AppWindow.Hide();
    }

    /// <summary>Restores the window from the tray (or from minimized) and brings it to front.</summary>
    public void ShowFromTray()
    {
        // While restoring, the window is briefly still "minimized" - don't send it straight back to the tray.
        _isRestoringFromTray = true;
        try
        {
            _isHiddenInTray = false;
            AppWindow.Show();
            if (AppWindow.Presenter is OverlappedPresenter presenter &&
                presenter.State == OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }
            App.BringToForeground(this);
        }
        finally
        {
            DispatcherQueue.TryEnqueue(() => _isRestoringFromTray = false);
        }
    }

    private void SendToTray_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void TraySetting_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || MinimizeToTrayToggle == null || CloseToTrayToggle == null) return;
        SaveSetting(MinimizeToTraySettingKey, MinimizeToTrayToggle.IsOn.ToString());
        SaveSetting(CloseToTraySettingKey, CloseToTrayToggle.IsOn.ToString());
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _timer.Stop();
        CloseBreakOverlay();
        _trayIcon?.Dispose();
        _trayIcon = null;
        Application.Current.Exit();
    }

    // =====================================================================================
    //  Settings persistence (remembers selected tones & tray options between launches)
    // =====================================================================================

    private static string? LoadSetting(string key)
    {
        try
        {
            return ApplicationData.Current.LocalSettings.Values[key] as string;
        }
        catch
        {
            return null; // Unpackaged / settings unavailable - use defaults
        }
    }

    private static void SaveSetting(string key, string value)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[key] = value;
        }
        catch
        {
            // Settings unavailable - non-fatal
        }
    }
}
