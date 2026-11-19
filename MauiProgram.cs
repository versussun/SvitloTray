using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.LifecycleEvents;
using SvitloTray.Core;
using SvitloTray.Services;
using SvitloTray.Views;

namespace SvitloTray;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton(SettingsStore.Instance);
        builder.Services.AddSingleton<EnergyUaClient>();
        builder.Services.AddSingleton<ScheduleService>();
        builder.Services.AddSingleton<AppController>();
        builder.Services.AddSingleton<MainPage>();

#if WINDOWS
        builder.Services.AddSingleton<ITrayService, SvitloTray.WinUI.WindowsTrayService>();
        builder.Services.AddSingleton<IPlatformIntegration, SvitloTray.WinUI.WindowsPlatform>();
        builder.ConfigureLifecycleEvents(events =>
            events.AddWindows(windows =>
                windows.OnWindowCreated(window =>
                    SvitloTray.WinUI.WindowsHost.OnWindowCreated(window, SettingsStore.Instance.Current.StartMinimized))));
#else
        builder.Services.AddSingleton<ITrayService, NullTrayService>();
        builder.Services.AddSingleton<IPlatformIntegration, NullPlatformIntegration>();
#endif

        return builder.Build();
    }
}
