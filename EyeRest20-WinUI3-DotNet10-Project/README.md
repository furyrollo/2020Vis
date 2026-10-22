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
