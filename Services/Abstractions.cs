using SvitloTray.Core;

namespace SvitloTray.Services;

/// <summary>What to show in the tray: color by state, 1–3 characters on the icon, and a tooltip.</summary>
public sealed record TrayState(PowerStatus? Status, string IconText, string Tooltip);

/// <summary>Notification area icon.</summary>
public interface ITrayService : IDisposable
{
    void Initialize();
    void Update(TrayState state);
    void ShowNotification(string title, string text);

    event Action? OpenRequested;
    event Action? RefreshRequested;
    event Action? OpenSiteRequested;
    event Action? ExitRequested;
}

/// <summary>Platform-specific window and system actions.</summary>
public interface IPlatformIntegration
{
    void ShowMainWindow();
    void ExitApp();
    void SetAutoStart(bool enabled);

    /// <summary>Make the main window taller by the given amount (device-independent pixels), within the screen.</summary>
    void GrowWindowBy(double dips);

    /// <summary>Undo GrowWindowBy: return the window to its previous height and position.</summary>
    void RestoreWindowSize();
}

/// <summary>Stubs for platforms without a tray.</summary>
public sealed class NullTrayService : ITrayService
{
    public void Initialize() { }
    public void Update(TrayState state) { }
    public void ShowNotification(string title, string text) { }
    public void Dispose() { }

#pragma warning disable CS0067 // event is never used
    public event Action? OpenRequested;
    public event Action? RefreshRequested;
    public event Action? OpenSiteRequested;
    public event Action? ExitRequested;
#pragma warning restore CS0067
}

public sealed class NullPlatformIntegration : IPlatformIntegration
{
    public void ShowMainWindow() { }
    public void ExitApp() => Application.Current?.Quit();
    public void SetAutoStart(bool enabled) { }
    public void GrowWindowBy(double dips) { }
    public void RestoreWindowSize() { }
}

public static class TrayStateBuilder
{
    public static TrayState Build(ScheduleService svc, DateTimeOffset now)
    {
        var f = svc.Forecast;
        var name = svc.DisplayName;

        if (f.Current is not { } current)
        {
            var reason = svc.IsRefreshing ? S.TrayLoading
                : svc.LastError is { } err ? S.TrayError(err)
                : S.TrayNoData;
            return new TrayState(null, svc.IsRefreshing ? "…" : "?", Fmt.Clip($"{reason}\n{name}", 127));
        }

        string line2, iconText;
        if (f.Next is { } next)
        {
            var left = next.At - now;
            line2 = $"{Fmt.ChangeVerb(next.To)} {Fmt.At(next.At, now)} ({S.In(Fmt.Duration(left))})";
            iconText = Fmt.IconText(left);
        }
        else
        {
            line2 = f.KnownUntil is { } until
                ? S.NoChangesUntil(Fmt.Clock(until, now))
                : S.NoChangesExpected;
            iconText = "—";
        }

        var tip = $"{Fmt.StatusTitle(current)}\n{line2}\n{name}";
        if (svc.LastError is not null) tip += "\n" + S.TrayUpdateFailed;
        return new TrayState(current, iconText, Fmt.Clip(tip, 127));
    }
}
