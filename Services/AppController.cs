using Microsoft.Maui.ApplicationModel;
using SvitloTray.Core;

namespace SvitloTray.Services;

/// <summary>Connects the schedule service, the tray icon and the window.</summary>
public sealed class AppController
{
    readonly ScheduleService _svc;
    readonly ITrayService _tray;
    readonly IPlatformIntegration _platform;
    bool _started;

    public AppController(ScheduleService svc, ITrayService tray, IPlatformIntegration platform)
    {
        _svc = svc;
        _tray = tray;
        _platform = platform;
    }

    /// <summary>Call once on the UI thread at startup.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        _tray.Initialize();
        _tray.OpenRequested += () => _platform.ShowMainWindow();
        _tray.RefreshRequested += () => _ = _svc.RefreshAsync();
        _tray.OpenSiteRequested += OpenSite;
        _tray.ExitRequested += Exit;

        _svc.Changed += () => MainThread.BeginInvokeOnMainThread(UpdateTray);
        _svc.Notify += (title, text) => MainThread.BeginInvokeOnMainThread(() => _tray.ShowNotification(title, text));
        Loc.LanguageChanged += () => MainThread.BeginInvokeOnMainThread(UpdateTray);
        _svc.Start();
    }

    void UpdateTray() => _tray.Update(TrayStateBuilder.Build(_svc, TimeZones.NowKyiv()));

    async void OpenSite()
    {
        var url = _svc.CurrentSourceUrl;
        if (url is null) return;
        try { await Launcher.Default.OpenAsync(url); }
        catch (Exception) { }
    }

    void Exit()
    {
        _svc.Dispose();
        _tray.Dispose();
        _platform.ExitApp();
    }
}
