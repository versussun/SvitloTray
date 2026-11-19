using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SvitloTray.WinUI;

/// <summary>Context menu item. Text = null means a separator.</summary>
internal sealed record TrayMenuItem(string? Text, Action? Click, bool Enabled = true, bool IsDefault = false)
{
    public static TrayMenuItem Separator => new(null, null);
}

/// <summary>
/// Minimal tray icon on plain Win32 (Shell_NotifyIcon) without WinForms/WPF.
/// Creates a hidden window for messages from the icon. Use from the UI thread.
/// </summary>
internal sealed class NativeTrayIcon : IDisposable
{
    const uint TrayId = 1;
    const uint WM_NULL = 0x0000;
    const uint WM_TRAYICON = 0x0400 + 0x42; // WM_USER + n
    const int WM_LBUTTONUP = 0x0202;
    const int WM_RBUTTONUP = 0x0205;
    const int NIN_BALLOONUSERCLICK = 0x0400 + 5;

    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10;
    const uint NIIF_INFO = 0x01;

    const uint MF_STRING = 0x0000, MF_GRAYED = 0x0001, MF_SEPARATOR = 0x0800;
    const uint TPM_RIGHTBUTTON = 0x0002, TPM_NONOTIFY = 0x0080, TPM_RETURNCMD = 0x0100;

    readonly WndProcDelegate _wndProc; // keep a reference so the GC does not collect the delegate
    readonly string _className;
    readonly IntPtr _hInstance;
    readonly IntPtr _hwnd;
    uint _taskbarCreatedMsg;
    IntPtr _hIcon;
    string _tip = "";
    bool _added;
    bool _disposed;

    public Func<IReadOnlyList<TrayMenuItem>>? MenuFactory { get; set; }
    public event Action? LeftClick;
    public event Action? BalloonClick;

    public NativeTrayIcon()
    {
        _wndProc = WndProc;
        _className = "SvitloTrayWnd_" + Guid.NewGuid().ToString("N");
        _hInstance = GetModuleHandleW(null);

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            lpszClassName = _className,
        };
        if (RegisterClassExW(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        // A regular (not message-only) hidden window: only such windows receive
        // the broadcast TaskbarCreated message after Explorer restarts.
        _hwnd = CreateWindowExW(0, _className, "SvitloTray", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");
    }

    /// <summary>Replace the icon (this class takes ownership of the HICON) and the tooltip.</summary>
    public void Update(IntPtr hIcon, string tooltip)
    {
        if (_disposed) { DestroyIcon(hIcon); return; }

        var old = _hIcon;
        _hIcon = hIcon;
        _tip = Clip(tooltip, 127);

        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        if (!_added || !Shell_NotifyIconW(NIM_MODIFY, ref data))
            _added = Shell_NotifyIconW(NIM_ADD, ref data);

        if (old != IntPtr.Zero && old != hIcon) DestroyIcon(old);
    }

    public void UpdateTooltip(string tooltip)
    {
        var tip = Clip(tooltip, 127);
        if (_disposed || !_added || tip == _tip) return;
        _tip = tip;
        var data = NewData(NIF_TIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    /// <summary>Pop-up notification (a toast from the tray on Windows 10/11).</summary>
    public void ShowBalloon(string title, string text)
    {
        if (_disposed || !_added) return;
        var data = NewData(NIF_INFO);
        data.szInfoTitle = Clip(title, 63);
        data.szInfo = Clip(text, 255);
        data.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    NOTIFYICONDATAW NewData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = TrayId,
        uFlags = flags,
        uCallbackMessage = WM_TRAYICON,
        hIcon = _hIcon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
    };

    IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_TRAYICON)
            {
                switch ((int)(lParam.ToInt64() & 0xFFFF))
                {
                    case WM_LBUTTONUP: LeftClick?.Invoke(); break;
                    case WM_RBUTTONUP: ShowMenu(); break;
                    case NIN_BALLOONUSERCLICK: BalloonClick?.Invoke(); break;
                }
                return IntPtr.Zero;
            }

            if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
            {
                // Explorer restarted — the icon has to be added again.
                _added = false;
                if (_hIcon != IntPtr.Zero)
                {
                    var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
                    _added = Shell_NotifyIconW(NIM_ADD, ref data);
                }
                return IntPtr.Zero;
            }
        }
        catch (Exception)
        {
            // An exception must not escape into native code — that would crash the process.
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    void ShowMenu()
    {
        var items = MenuFactory?.Invoke();
        if (items is null || items.Count == 0) return;

        var hMenu = CreatePopupMenu();
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var id = (uint)(i + 1);
                if (item.Text is null)
                {
                    AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }
                AppendMenuW(hMenu, MF_STRING | (item.Enabled ? 0u : MF_GRAYED), (UIntPtr)id, item.Text);
                if (item.IsDefault) SetMenuDefaultItem(hMenu, id, 0);
            }

            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd); // otherwise the menu does not close when clicking outside it
            var cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (cmd > 0 && cmd <= items.Count) items[(int)cmd - 1].Click?.Invoke();
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
        DestroyWindow(_hwnd);
        UnregisterClassW(_className, _hInstance);
    }

    // ---------------- Win32 ----------------

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEXW
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
    struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool UnregisterClassW(string className, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessageW(string message);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenuW(IntPtr hMenu, uint flags, UIntPtr idNewItem, string? text);

    [DllImport("user32.dll")]
    static extern bool SetMenuDefaultItem(IntPtr hMenu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    static extern uint TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpmParams);

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);
}
