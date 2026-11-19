# Svitlo — power outage schedule in the tray (.NET MAUI, Windows)

The notification area icon shows how much time is left until the power state changes:

- **green** — power is on, the number is minutes until the outage;
- **red** — power is off, the number is minutes until power comes back;
- **yellow** — "possible outage" according to the schedule;
- more than 99 minutes is shown in hours (`3h` in English, `3г` in Ukrainian); `—` means no changes until the end of the known schedule.

Hovering shows a tooltip, e.g. "Power is on / Outage at 15:00 (in 1 h 25 min) / &lt;settlement&gt;, &lt;street&gt; &lt;house&gt; · queue 2.1".
Left click opens the window, right click opens the menu (Refresh, Open on the site, Exit). The window's close button hides it to the tray.

The window shows the current state and a countdown, schedule bars for today and tomorrow, address selection, notifications N minutes before a change, a notice when the schedule changes or tomorrow's schedule appears, and autostart with Windows.

## Languages

The interface is available in **Ukrainian** (default) and **English**. Switch it in *Address and settings → Language*; the window, tray tooltip, tray menu and notifications change right after saving.
All strings live in `Core/Localization.cs` (class `S`, each entry has a Ukrainian and an English variant).

## Where the data comes from

Neither energy-ua.info nor Chernihivoblenergo has a public API. The regional utility only returns the queue schedule and the address-to-queue lookup after a Turnstile captcha.
So the app does what the energy-ua.info site does itself. The site renders pages on the server and embeds the data as JSON right in the HTML:

| Page | JSON in `<script id=…>` |
|---|---|
| `/` | `homeCityPayload` — settlements |
| `/select/{city}` | `selectPagePayload` — streets |
| `/select/{city}/{street}` | `selectPagePayload` — houses |
| `/grafik/{city}/{street}/{house}` | `schedulePayload` — schedule + queue in `.queue-badge` |
| `/cherga/2-1` | `schedulePayload` — schedule of queue 2.1 |

`schedulePayload` looks like this (time is in minutes since midnight, Kyiv time):

```json
{"today":[{"from":510,"to":900,"status":"red"}],"tomorrow":[],"tomorrowAvailable":false,
 "dates":{"today":"19.11.2026","tomorrow":"20.11.2026"},"isCurrentlyOff":true,"isEmergency":false}
```

`Core/ScheduleCalculator.GetSegments` is a direct port of `getSegments()` from the site's `assets/js/schedule-live.js`. The day is split at every boundary; `red` wins over `yellow`, `yellow` wins over `green`; adjacent pieces with the same status are merged.
On top of that, the app joins "today" and "tomorrow" into one timeline, so the next power on/off is computed across midnight too.

Pages are loaded with a plain `HttpClient`. If Cloudflare blocks it, the page is opened in an invisible WebView2 (the same Edge engine) and the JSON is taken from the DOM.

## Running

Requirements: Windows 10 1809+ / 11, .NET 10 SDK and the MAUI workload.

```powershell
dotnet workload install maui-windows
cd SvitloTray
dotnet run -f net10.0-windows10.0.19041.0
```

Or open `SvitloTray.csproj` in Visual Studio 2026 with the ".NET Multi-platform App UI development" workload and pick the **Windows Machine** profile.

If you have .NET 8 or 9, replace `net10.0` with `net8.0` / `net9.0` in `SvitloTray.csproj`. Visual Studio 2022 works for .NET 8.

A ready folder with the `.exe`:

```powershell
dotnet publish -f net10.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64
```

Settings and the last loaded schedule are stored in `%AppData%\SvitloTray`.
Autostart uses the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SvitloTray` key with the `--tray` argument: the app starts minimized.

## Structure

```
Core/                     logic without UI
  EnergyUaClient.cs       site URLs, HttpClient + browser fallback
  EnergyUaParser.cs       extracting JSON from HTML
  ScheduleCalculator.cs   getSegments() port, today+tomorrow timeline, forecast
  ScheduleService.cs      refresh every 15 min and right after a change, notifications
  Settings.cs             address/queue/link/language, stored in %AppData%
  Format.cs               texts: "in 1 h 25 min", "tomorrow at 08:30", icon digits
  Localization.cs         Ukrainian and English strings, current language
Services/                 glue between the tray, the window and the service
Views/                    MAUI window (C# without XAML), address picker, schedule bar
Platforms/Windows/
  NativeTrayIcon.cs       Win32 tray icon (Shell_NotifyIcon), menu, notifications
  TrayIconRenderer.cs     drawing the icon with digits (System.Drawing)
  WindowsHost.cs          close button → tray, single instance, autostart
```

## Other regions

In the "By energy-ua.info link" mode you can paste any `https://<region>.energy-ua.info/grafik/…` or `…/cherga/…` link, e.g. for Kyiv or Lviv. The "By address" mode searches the Chernihiv region by default.
