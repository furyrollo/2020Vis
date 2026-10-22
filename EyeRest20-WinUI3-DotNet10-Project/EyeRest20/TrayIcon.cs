using System;
using System.IO;
using System.Runtime.InteropServices;

namespace EyeRest20;

/// <summary>
/// Native Windows system-tray (notification area) icon.
/// WinUI 3 has no built-in tray support, so this uses the Win32 Shell_NotifyIcon API
/// with a small hidden helper window that receives the tray mouse messages.
/// The helper window is created on the UI thread, so all callbacks run on the UI thread.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    // ---- Public events -------------------------------------------------------------
    public event Action? OpenRequested;
    public event Action? ToggleTimerRequested;
    public event Action? ExitRequested;

    /// <summary>Returns the label for the Start/Pause/Resume tray menu item.</summary>
    public Func<string>? ToggleTimerLabelProvider { get; set; }

    // ---- Win32 constants -----------------------------------------------------------
    private const uint WM_NULL = 0x0000;
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 1;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;

    private const uint NIM_ADD = 0x0;
    private const uint NIM_MODIFY = 0x1;
    private const uint NIM_DELETE = 0x2;

    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;
    private static readonly IntPtr IDI_APPLICATION = new(32512);

    private const int CMD_OPEN = 1;
    private const int CMD_TOGGLE = 2;
    private const int CMD_EXIT = 3;

    private const string WindowClassName = "EyeRest20_TrayHelperWindow";

    // ---- State ---------------------------------------------------------------------
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Kept in a field so the GC never collects the delegate while native code holds it.
    private readonly WndProcDelegate _wndProc;
    private readonly IntPtr _hInstance;
    private readonly uint _taskbarCreatedMsg;
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _ownsIcon;
    private bool _added;
    private string _tooltip = "EyeRest 20-20-20";
    private bool _disposed;

    public TrayIcon(string iconPath)
    {
        _wndProc = WndProc;
        _hInstance = GetModuleHandle(null);
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            lpszClassName = WindowClassName
        };
        RegisterClassEx(ref wc); // Ignoring "class already exists" is fine

        // A normal (never shown) top-level window, NOT a message-only window,
        // so we also receive the "TaskbarCreated" broadcast when Explorer restarts.
        _hwnd = CreateWindowEx(0, WindowClassName, "EyeRest20 Tray", 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);

        LoadTrayIcon(iconPath);
        AddIcon();
    }

    private void LoadTrayIcon(string iconPath)
    {
        try
        {
            if (File.Exists(iconPath))
            {
                _hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON,
                    GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);
                _ownsIcon = _hIcon != IntPtr.Zero;
            }
        }
        catch { /* fall through to default icon */ }

        if (_hIcon == IntPtr.Zero)
        {
            _hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);
            _ownsIcon = false;
        }
    }

    private NOTIFYICONDATA CreateData(uint flags)
    {
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = _tooltip,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private void AddIcon()
    {
        if (_hwnd == IntPtr.Zero) return;
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    /// <summary>Updates the hover tooltip (max 127 characters).</summary>
    public void SetTooltip(string text)
    {
        if (_disposed) return;
        if (text.Length > 127) text = text[..127];
        if (text == _tooltip) return;
        _tooltip = text;
        if (!_added) return;
        var data = CreateData(NIF_TIP);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_TRAYICON)
            {
                uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouseMsg == WM_LBUTTONUP)
                {
                    OpenRequested?.Invoke();
                }
                else if (mouseMsg == WM_RBUTTONUP || mouseMsg == WM_CONTEXTMENU)
                {
                    ShowContextMenu();
                }
                return IntPtr.Zero;
            }

            if (msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
            {
                // Explorer restarted - the tray was recreated, so re-add our icon.
                AddIcon();
                return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EyeRest] Tray WndProc error: {ex.Message}");
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            string toggleLabel = ToggleTimerLabelProvider?.Invoke() ?? "Start / Pause";
            AppendMenu(menu, MF_STRING, (UIntPtr)CMD_OPEN, "Open EyeRest");
            AppendMenu(menu, MF_STRING, (UIntPtr)CMD_TOGGLE, toggleLabel);
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (UIntPtr)CMD_EXIT, "Exit EyeRest");
            SetMenuDefaultItem(menu, CMD_OPEN, 0);

            GetCursorPos(out POINT pt);

            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (cmd)
            {
                case CMD_OPEN: OpenRequested?.Invoke(); break;
                case CMD_TOGGLE: ToggleTimerRequested?.Invoke(); break;
                case CMD_EXIT: ExitRequested?.Invoke(); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added)
        {
            var data = CreateData(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        if (_ownsIcon && _hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
        }
        _hIcon = IntPtr.Zero;

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        UnregisterClass(WindowClassName, _hInstance);
    }

    // ---- Win32 interop -------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool SetMenuDefaultItem(IntPtr hMenu, int uItem, int fByPos);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved,
        IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
