using Microsoft.UI.Windowing;
using SvitloTray.Services;

namespace SvitloTray.WinUI;

/// <summary>
/// Main window behavior on Windows: the close button hides the window to the tray,
/// launching with --tray (autostart) starts minimized,
/// launching the .exe again just shows the already open window.
/// </summary>
public static class WindowsHost
{
    static Microsoft.UI.Xaml.Window? _window;
    static AppWindow? _appWindow;
    static volatile bool _exiting;
    static bool _listenerStarted;

    static bool LaunchedFromAutostart =>
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

    public static void OnWindowCreated(Microsoft.UI.Xaml.Window window, bool startMinimized)
    {
        if (_window is not null) return;
        _window = window;
        _appWindow = window.AppWindow;

        _appWindow.Closing += (sender, args) =>
        {
            if (_exiting) return;
            args.Cancel = true;   // do not close the app, hide it to the tray instead
            sender.Hide();
        };

        if (startMinimized || LaunchedFromAutostart)
        {
            // Runs after MAUI activates the window.
            window.DispatcherQueue.TryEnqueue(() => _appWindow?.Hide());
        }

        if (!_listenerStarted)
        {
            _listenerStarted = true;
            SingleInstance.Listen(ShowMainWindow);
        }
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
}

public sealed class WindowsPlatform : IPlatformIntegration
{
    public void ShowMainWindow() => WindowsHost.ShowMainWindow();
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
