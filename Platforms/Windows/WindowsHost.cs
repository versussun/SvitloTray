using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using SvitloTray.Core;
using SvitloTray.Services;
using SvitloTray.Views;
using WinRT.Interop;

namespace SvitloTray.WinUI;

/// <summary>
/// Main window behavior on Windows: the close button hides the window to the tray,
/// launching with --tray (autostart) starts minimized,
/// launching the .exe again just shows the already open window.
/// </summary>
public static class WindowsHost
{
    const int DWMWA_BORDER_COLOR = 34;
    const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    const int GWL_STYLE = -16;
    const long WS_BORDER = 0x00800000L;
    const long WS_DLGFRAME = 0x00400000L;
    const long WS_CAPTION = 0x00C00000L;
    const long WS_THICKFRAME = 0x00040000L;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_FRAMECHANGED = 0x0020;

    static Microsoft.UI.Xaml.Window? _window;
    static AppWindow? _appWindow;
    static Microsoft.UI.Xaml.Window? _widgetWindow;
    static AppWindow? _widgetAppWindow;
    static Func<Microsoft.Maui.Controls.Window>? _widgetWindowFactory;
    static bool _widgetRequested;
    static bool _widgetCreating;
    static volatile bool _exiting;
    static bool _listenerStarted;

    static bool LaunchedFromAutostart =>
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

    public static void OnWindowCreated(Microsoft.UI.Xaml.Window window, bool startMinimized)
    {
        if (_window is null)
        {
            _window = window;
            _appWindow = window.AppWindow;

            _appWindow.Closing += (sender, args) =>
            {
                if (_exiting) return;
                args.Cancel = true;
                sender.Hide();
            };

            if (startMinimized || LaunchedFromAutostart)
            {
                window.DispatcherQueue.TryEnqueue(() => _appWindow?.Hide());
            }

            if (!_listenerStarted)
            {
                _listenerStarted = true;
                SingleInstance.Listen(ShowMainWindow);
            }

            if (_widgetRequested)
                window.DispatcherQueue.TryEnqueue(EnsureWidgetWindow);
            return;
        }

        if (_widgetWindow is not null) return;
        _widgetWindow = window;
        _widgetAppWindow = window.AppWindow;
        _widgetCreating = false;
        ConfigureWidgetWindow(window);
        _widgetAppWindow.Closing += (sender, args) =>
        {
            if (_exiting) return;
            args.Cancel = true;
            sender.Hide();
        };
        if (_widgetRequested) ShowWidget();
        else _widgetAppWindow.Hide();
    }

    public static void ConfigureWidget(Func<Microsoft.Maui.Controls.Window> windowFactory) =>
        _widgetWindowFactory = windowFactory;

    public static void SetWidgetVisible(bool visible)
    {
        _widgetRequested = visible;
        var mainWindow = _window;
        if (mainWindow is null) return;

        mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (!visible)
            {
                _widgetAppWindow?.Hide();
                return;
            }

            if (_widgetAppWindow is not null) ShowWidget();
            else EnsureWidgetWindow();
        });
    }

    static void EnsureWidgetWindow()
    {
        if (!_widgetRequested || _widgetWindow is not null || _widgetCreating) return;
        var factory = _widgetWindowFactory
            ?? throw new InvalidOperationException("The desktop widget window factory has not been configured.");
        var app = Microsoft.Maui.Controls.Application.Current
            ?? throw new InvalidOperationException("The MAUI application is not available.");

        _widgetCreating = true;
        try { app.OpenWindow(factory()); }
        catch
        {
            _widgetCreating = false;
            throw;
        }
    }

    static void ConfigureWidgetWindow(Microsoft.UI.Xaml.Window window)
    {
        var appWindow = window.AppWindow;
        appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        var hwnd = WindowNative.GetWindowHandle(window);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        int widgetWidth = (int)Math.Ceiling(370 * scale);
        int widgetHeight = (int)Math.Ceiling(290 * scale);
        appWindow.Resize(new Windows.Graphics.SizeInt32(widgetWidth, widgetHeight));
        SetWidgetRegion(window, appWindow.Size.Width, appWindow.Size.Height, (int)Math.Ceiling(22 * scale));
        EnableWidgetDragging(window);
        var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        appWindow.Move(new Windows.Graphics.PointInt32(
            work.X + work.Width - widgetWidth - 24,
            work.Y + work.Height - widgetHeight - 24));
    }

    static void SetWidgetRegion(Microsoft.UI.Xaml.Window window, int width, int height, int cornerRadius)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        RemoveWidgetFrame(hwnd);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            int borderColor = DWMWA_COLOR_NONE;
            int result = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            if (result < 0) Marshal.ThrowExceptionForHR(result);
        }

        int diameter = cornerRadius * 2;
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (SetWindowRgn(hwnd, region, redraw: true) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            DeleteObject(region);
            throw new Win32Exception(error);
        }
    }

    static void RemoveWidgetFrame(IntPtr hwnd)
    {
        long style = GetWindowLongPtrW(hwnd, GWL_STYLE).ToInt64();
        long frameStyles = WS_BORDER | WS_DLGFRAME | WS_CAPTION | WS_THICKFRAME;
        if (SetWindowLongPtrW(hwnd, GWL_STYLE, new IntPtr(style & ~frameStyles)) == IntPtr.Zero &&
            Marshal.GetLastWin32Error() != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (!SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    static void EnableWidgetDragging(Microsoft.UI.Xaml.Window window)
    {
        if (window.Content is not Microsoft.UI.Xaml.UIElement root)
            throw new InvalidOperationException("The desktop widget has no native content element.");

        bool dragging = false;
        POINT startCursor = default;
        var startPosition = default(Windows.Graphics.PointInt32);

        root.PointerPressed += (_, args) =>
        {
            var point = args.GetCurrentPoint(root).Position;
            if (point.X > 52 || point.Y > 46) return;
            if (!args.GetCurrentPoint(root).Properties.IsLeftButtonPressed) return;
            if (!GetCursorPos(out startCursor))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!root.CapturePointer(args.Pointer)) return;

            startPosition = window.AppWindow.Position;
            dragging = true;
            args.Handled = true;
        };

        root.PointerMoved += (_, args) =>
        {
            if (!dragging) return;
            if (!args.GetCurrentPoint(root).Properties.IsLeftButtonPressed)
            {
                dragging = false;
                root.ReleasePointerCapture(args.Pointer);
                return;
            }
            if (!GetCursorPos(out var cursor))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            window.AppWindow.Move(new Windows.Graphics.PointInt32(
                startPosition.X + cursor.X - startCursor.X,
                startPosition.Y + cursor.Y - startCursor.Y));
            args.Handled = true;
        };

        root.PointerReleased += (_, args) =>
        {
            if (!dragging) return;
            dragging = false;
            root.ReleasePointerCapture(args.Pointer);
            args.Handled = true;
        };
        root.PointerCaptureLost += (_, _) => dragging = false;
    }

    static void ShowWidget()
    {
        _widgetAppWindow?.Show();
        _widgetWindow?.Activate();
    }

    public static void ShowMainWindow()
    {
        var window = _window;
        var appWindow = _appWindow;
        if (window is null || appWindow is null) return;

        window.DispatcherQueue.TryEnqueue(() =>
        {
            appWindow.Show();
            if (appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p)
                p.Restore();
            window.Activate();
        });
    }

    public static void Exit()
    {
        _exiting = true;
        Microsoft.Maui.Controls.Application.Current?.Quit();
    }

    // Window position and height before GrowBy, so RestoreSize can bring them back.
    static int? _savedHeight, _savedY;

    /// <summary>
    /// Makes the window taller by <paramref name="dips"/> (device-independent pixels),
    /// limited to the monitor's work area; moves the window up if its bottom would go off screen.
    /// Call on the UI thread.
    /// </summary>
    public static void GrowBy(double dips)
    {
        var window = _window;
        var appWindow = _appWindow;
        if (window is null || appWindow is null || dips <= 0) return;
        if (appWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored }) return;

        double scale = window.Content?.XamlRoot?.RasterizationScale ?? 1.0;
        var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var pos = appWindow.Position;
        var size = appWindow.Size;

        int height = Math.Min(size.Height + (int)Math.Ceiling(dips * scale), work.Height);
        if (height <= size.Height) return;

        int y = pos.Y;
        int workBottom = work.Y + work.Height;
        if (y + height > workBottom) y = Math.Max(work.Y, workBottom - height);

        _savedHeight ??= size.Height;
        _savedY ??= pos.Y;
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(pos.X, y, size.Width, height));
    }

    /// <summary>Returns the window to the height and position it had before the first GrowBy.</summary>
    public static void RestoreSize()
    {
        var appWindow = _appWindow;
        if (appWindow is null || _savedHeight is not int height || _savedY is not int y) return;
        _savedHeight = _savedY = null;
        if (appWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored }) return;

        var pos = appWindow.Position;
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(pos.X, y, appWindow.Size.Width, height));
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("gdi32.dll", SetLastError = true)]
    static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    static extern IntPtr SetWindowLongPtrW(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }
}

public sealed class WindowsPlatform : IPlatformIntegration
{
    public WindowsPlatform(WidgetPage widgetPage)
    {
        WindowsHost.ConfigureWidget(() => new Microsoft.Maui.Controls.Window(widgetPage)
        {
            Title = S.WidgetTitle,
            Width = 370,
            Height = 290,
            MinimumWidth = 370,
            MinimumHeight = 290,
        });
    }

    public void ShowMainWindow() => WindowsHost.ShowMainWindow();
    public void SetWidgetVisible(bool visible) => WindowsHost.SetWidgetVisible(visible);
    public void ExitApp() => WindowsHost.Exit();
    public void SetAutoStart(bool enabled) => AutoStart.Set(enabled);
    public void GrowWindowBy(double dips) => WindowsHost.GrowBy(dips);
    public void RestoreWindowSize() => WindowsHost.RestoreSize();
}

/// <summary>One app instance per user.</summary>
internal static class SingleInstance
{
    const string MutexName = @"Local\SvitloTray.SingleInstance";
    const string EventName = @"Local\SvitloTray.Activate";
    static Mutex? _mutex;

    /// <returns>true if this is the first instance.</returns>
    public static bool TryClaim()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        return createdNew;
    }

    /// <summary>Ask the already running instance to show its window.</summary>
    public static void SignalFirstInstance()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(EventName);
            ev.Set();
        }
        catch (Exception) { }
    }

    public static void Listen(Action onActivate)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var thread = new Thread(() =>
        {
            while (ev.WaitOne())
            {
                try { onActivate(); } catch (Exception) { }
            }
        })
        {
            IsBackground = true,
            Name = "SvitloTray.SingleInstance",
        };
        thread.Start();
    }
}

/// <summary>Autostart via the HKCU Run registry key (no administrator rights needed).</summary>
internal static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "SvitloTray";

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled && Environment.ProcessPath is { } exe)
                key.SetValue(ValueName, $"\"{exe}\" --tray");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception) { }
    }
}
