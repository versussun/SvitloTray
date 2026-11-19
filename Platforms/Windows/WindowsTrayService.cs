using SvitloTray.Core;
using SvitloTray.Services;

namespace SvitloTray.WinUI;

public sealed class WindowsTrayService : ITrayService
{
    NativeTrayIcon? _icon;
    string _lastIconKey = "";

    public event Action? OpenRequested;
    public event Action? RefreshRequested;
    public event Action? OpenSiteRequested;
    public event Action? ExitRequested;

    public void Initialize()
    {
        if (_icon is not null) return;

        _icon = new NativeTrayIcon();
        _icon.LeftClick += () => OpenRequested?.Invoke();
        _icon.BalloonClick += () => OpenRequested?.Invoke();
        // Built on every right click, so the menu always uses the current language.
        _icon.MenuFactory = () => new List<TrayMenuItem>
        {
            new(S.MenuOpen, () => OpenRequested?.Invoke(), IsDefault: true),
            new(S.MenuRefresh, () => RefreshRequested?.Invoke()),
            new(S.MenuOpenSite, () => OpenSiteRequested?.Invoke()),
            TrayMenuItem.Separator,
            new(S.MenuExit, () => ExitRequested?.Invoke()),
        };

        Update(new TrayState(null, "…", S.TrayInitialTooltip));
    }

    public void Update(TrayState state)
    {
        if (_icon is null) return;

        // Redraw the image only when the number or the color changes.
        var key = $"{state.Status}|{state.IconText}";
        if (key == _lastIconKey)
        {
            _icon.UpdateTooltip(state.Tooltip);
            return;
        }
        _lastIconKey = key;
        _icon.Update(TrayIconRenderer.Render(state.IconText, state.Status), state.Tooltip);
    }

    public void ShowNotification(string title, string text) => _icon?.ShowBalloon(title, text);

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
