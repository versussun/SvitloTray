using Microsoft.Maui.ApplicationModel;
using SvitloTray.Core;
using SvitloTray.Services;
using SvitloTray.Views;

namespace SvitloTray;

public sealed class App : Application
{
    readonly MainPage _page;
    readonly AppController _controller;

    public App(MainPage page, AppController controller)
    {
        _page = page;
        _controller = controller;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_page)
        {
            Title = S.WindowTitle,
            Width = 480,
            Height = 860,
            MinimumWidth = 380,
            MinimumHeight = 500,
        };
        Loc.LanguageChanged += () => MainThread.BeginInvokeOnMainThread(() => window.Title = S.WindowTitle);

        // Start the tray and background updates once, on the UI thread.
        _controller.Start();
        return window;
    }
}
