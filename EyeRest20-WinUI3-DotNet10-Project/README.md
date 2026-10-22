# EyeRest 20-20-20 — Native Windows Desktop Application

A lightweight native Windows desktop application built with **C#**, **.NET 10**, **WinUI 3**, and **Windows App SDK** implementing the ophthalmology-proven 20-20-20 rule.

---

## 🚀 Quick Start for Visual Studio 2026

1. **Open Solution**:
   Double-click `EyeRest20.sln` to open in Visual Studio 2026.
2. **Select Target**:
   In the top toolbar, select **Debug** and **x64** (or ARM64 if on a Surface Pro / Snapdragon device).
   Make sure the startup project is **EyeRest20 (Package)**.
3. **Run**:
   Press **F5** (or click the green Play arrow). Visual Studio will build the MSIX package and launch the application.

---

## 🔔 Testing Native Notifications

- Turn on the **Fast Demo Mode** switch in the app UI (sets 5s Work / 5s Break).
- Minimize the app or switch to another window.
- Within 5 seconds, you will hear the chime and see a native Windows Toast Notification appear in your Windows Action Center with **"Start Break Now"** and **"Snooze 2m"** buttons.
- Clicking the notification brings the EyeRest window back to the foreground automatically!

---

## 🆕 What's New (v1.1)

### 🔕 Silent Windows toast notifications
Toast notifications no longer play the Windows notification sound. Only the tone you selected in the app plays, so you don't get two sounds at once.

### 📥 Minimize to system tray
- **Minimize to System Tray** (on by default): minimizing the window hides it to the tray (the area next to the clock). The timer keeps running.
- **Send to Tray** button: hides the window to the tray right away.
- **Close Button Keeps Running in Tray** (off by default): the window's X button hides EyeRest to the tray instead of closing it.
- **Tray icon:** left-click to reopen the window. Right-click for a menu with *Open*, *Start/Pause/Resume*, and *Exit EyeRest*. Hover over the icon to see the time left.

### 🎵 Custom tones
You can use your own audio files (`.wav`, `.mp3`, `.m4a`, `.wma`, `.aac`, `.flac`). They appear in both sound lists as **"Custom: name"**.
1. **In the app (no rebuild needed):** under **Custom Tones**, click **Add Sound File...**. Or click **Open Custom Sounds Folder**, copy files into that folder, then click **Refresh List**.
2. **Bundled with the app:** put the files in `EyeRest20\CustomSounds\` before building in Visual Studio.

The tones you pick are saved and come back the next time you open the app.

### 🔊 More reliable alerts
- The timer now checks the actual clock time instead of counting ticks. If Windows slows down the app while it's minimized or in the tray, or the PC wakes from sleep, the alert still fires as soon as the time is up.
- The app tells Windows not to slow down its timer in the background (it opts out of power throttling / "Efficiency mode").
- Each alert gets its own audio player and uses the Windows **Alerts** audio category, so it plays over videos and music. It is not registered with the media controls, so other media apps can't pause it.
- A backup check confirms the sound actually started. If it didn't, the app plays the file through the classic Windows sound API, and as a last resort the Windows system chime.
