namespace SvitloTray.Core;

/// <summary>Supported interface languages.</summary>
public enum AppLanguage
{
    Ukrainian,
    English,
}

/// <summary>Current interface language and a notification when it changes.</summary>
public static class Loc
{
    static volatile AppLanguage _language = AppLanguage.Ukrainian;

    public static IReadOnlyList<AppLanguage> All { get; } = new[] { AppLanguage.Ukrainian, AppLanguage.English };

    public static AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>Raised on the thread that changed the language.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Language name in that language itself (shown in the language picker).</summary>
    public static string NativeName(AppLanguage language) => language switch
    {
        AppLanguage.English => "English",
        _ => "Українська",
    };

    /// <summary>Picks the string for the current language.</summary>
    public static string T(string uk, string en) => _language == AppLanguage.English ? en : uk;
}

/// <summary>All user-visible strings of the app, in Ukrainian and English.</summary>
public static class S
{
    static string T(string uk, string en) => Loc.T(uk, en);

    // ---------- App / window ----------
    public static string AppName => T("Світло", "Svitlo");
    public static string WindowTitle => T("Світло — графік відключень", "Svitlo — power outage schedule");

    // ---------- Durations and times ----------
    public static string LessThanMinute => T("менше хвилини", "less than a minute");
    public static string Minutes(int m) => T($"{m} хв", $"{m} min");
    public static string Hours(int h) => T($"{h} год", $"{h} h");
    public static string HoursMinutes(int h, int m) => T($"{h} год {m} хв", $"{h} h {m} min");
    public static string HoursSuffix => T("г", "h");
    public static string Tomorrow => T("завтра", "tomorrow");
    public static string AtTime(string hm) => T($"о {hm}", $"at {hm}");
    public static string AtDayTime(string day, string hm) => T($"{day} о {hm}", $"{day} at {hm}");
    public static string In(string duration) => T($"через {duration}", $"in {duration}");

    // ---------- Power status ----------
    public static string StatusOnTitle => T("Світло є", "Power is on");
    public static string StatusOffTitle => T("Світла немає", "Power is off");
    public static string StatusMaybeTitle => T("Можливе відключення", "Possible outage");
    public static string StatusUnknownTitle => T("Немає даних", "No data");

    public static string StatusOnShort => T("є світло", "power on");
    public static string StatusOffShort => T("немає світла", "power off");
    public static string StatusMaybeShort => T("можливе відключення", "possible outage");

    public static string VerbOff => T("Відключать", "Outage");
    public static string VerbOn => T("Увімкнуть", "Power back");
    public static string VerbMaybe => T("Можливе відключення", "Possible outage");

    public static string PossibleSuffix => T(" (можливо)", " (possible)");
    public static string NoOutages => T("відключень немає", "no outages");

    // ---------- Sources ----------
    public static string QueueName(string q) => T($"Черга {q}", $"Queue {q}");
    public static string NameWithQueue(string name, string q) => T($"{name} · черга {q}", $"{name} · queue {q}");

    // ---------- Errors ----------
    public static string ErrQueueFormat => T("Чергу потрібно вказати у вигляді 2.1", "Enter the queue as 2.1");
    public static string ErrLinkFormat => T(
        "Потрібне посилання виду https://chernigiv.energy-ua.info/grafik/… або …/cherga/2-1",
        "Expected a link like https://chernigiv.energy-ua.info/grafik/… or …/cherga/2-1");
    public static string ErrAddressEmpty => T("Вкажіть населений пункт, вулицю та будинок.", "Specify the settlement, street and house.");
    public static string ErrNotFound => T(
        "Адресу не знайдено на energy-ua.info (404). Перевірте населений пункт, вулицю та будинок.",
        "Address not found on energy-ua.info (404). Check the settlement, street and house.");
    public static string ErrNoData(int code) => T(
        $"energy-ua.info відповів {code} без даних (можливо, перевірка Cloudflare).",
        $"energy-ua.info returned {code} with no data (possibly a Cloudflare check).");
    public static string ErrLoadFailed(string message) => T($"Не вдалося завантажити дані: {message}", $"Failed to load data: {message}");
    public static string ErrNoSchedulePayload => T(
        "На сторінці немає даних графіка (schedulePayload) — можливо, сайт змінився.",
        "The page has no schedule data (schedulePayload) — the site may have changed.");
    public static string ErrNoSelectPayload => T("На сторінці немає списку (selectPagePayload).", "The page has no list (selectPagePayload).");
    public static string ErrNoCityPayload => T(
        "На сторінці немає списку населених пунктів (homeCityPayload).",
        "The page has no list of settlements (homeCityPayload).");

    // ---------- Notifications ----------
    public static string NotifyOffSoon => T("Скоро відключать світло", "Power outage soon");
    public static string NotifyOnSoon => T("Скоро увімкнуть світло", "Power coming back soon");
    public static string NotifyMaybeSoon => T("Можливе відключення", "Possible outage");
    public static string Until(string status, string clock) => T($"{status} до {clock}", $"{status} until {clock}");
    public static string TodayChanged => T("Графік на сьогодні змінився", "Today's schedule has changed");
    public static string TomorrowPublished => T("Опубліковано графік на завтра", "Tomorrow's schedule is published");
    public static string TomorrowChanged => T("Графік на завтра змінився", "Tomorrow's schedule has changed");
    public static string Outages(string periods) => T($"Відключення: {periods}", $"Outages: {periods}");

    // ---------- Tray ----------
    public static string TrayLoading => T("Завантажую графік…", "Loading schedule…");
    public static string TrayInitialTooltip => T("Світло: завантажую графік…", "Svitlo: loading schedule…");
    public static string TrayError(string error) => T($"Помилка: {error}", $"Error: {error}");
    public static string TrayNoData => T("Немає даних графіка", "No schedule data");
    public static string NoChangesUntil(string clock) => T($"Без змін до {clock}", $"No changes until {clock}");
    public static string NoChangesExpected => T("Змін не очікується", "No changes expected");
    public static string TrayUpdateFailed => T("⚠ не вдалося оновити", "⚠ update failed");
    public static string MenuOpen => T("Відкрити", "Open");
    public static string MenuShowWidget => T("Показати віджет", "Show desktop widget");
    public static string MenuHideWidget => T("Приховати віджет", "Hide desktop widget");
    public static string MenuRefresh => T("Оновити зараз", "Refresh now");
    public static string MenuOpenSite => T("Відкрити на energy-ua.info", "Open on energy-ua.info");
    public static string MenuExit => T("Вихід", "Exit");

    // ---------- Main window ----------
    public static string Emergency => T(" · аварійні відключення", " · emergency outages");
    public static string ScheduleNotLoaded => T("Графік ще не завантажено", "Schedule not loaded yet");
    public static string NoScheduleNow => T("Немає графіка на поточний час", "No schedule for the current time");
    public static string NextChange(string verb, string at, string left) => T($"{verb} {at} — через {left}", $"{verb} {at} — in {left}");
    public static string Then(string verb, string at) => T($"Потім: {verb} {at}", $"Then: {verb} {at}");
    public static string TomorrowNotPublished => T("Графік на завтра ще не опубліковано", "Tomorrow's schedule is not published yet");
    public static string NotPublished => T("Графік ще не опубліковано", "Schedule not published yet");
    public static string TodayTitle => T("Сьогодні", "Today");
    public static string TomorrowTitle => T("Завтра", "Tomorrow");
    public static string YesterdayTitle => T("Вчора", "Yesterday");
    public static string Refreshing => T("Оновлюю…", "Refreshing…");
    public static string UpdatedAt(string hm) => T($"Оновлено о {hm}", $"Updated at {hm}");
    public static string RefreshButton => T("Оновити", "Refresh");
    public static string SettingsButton(bool open) => T("Адреса та налаштування", "Address and settings") + (open ? " ▴" : " ▾");
    public static string WidgetTitle => T("Світло", "Svitlo");

    // ---------- Settings ----------
    public static string DataSource => T("Джерело даних", "Data source");
    public static string SourcePickerTitle => T("Джерело", "Source");
    public static string ModeAddress => T("За адресою (населений пункт, вулиця, будинок)", "By address (settlement, street, house)");
    public static string ModeQueue => T("За номером черги", "By queue number");
    public static string ModeLink => T("За посиланням energy-ua.info", "By energy-ua.info link");
    public static string City => T("Населений пункт", "Settlement");
    public static string CityPlaceholder => T("Почніть вводити, наприклад: Прилуки", "Start typing, e.g. Прилуки");
    public static string Street => T("Вулиця", "Street");
    public static string StreetPlaceholder => T("Почніть вводити назву вулиці", "Start typing the street name");
    public static string House => T("Будинок", "House");
    public static string HousePlaceholder => T("Номер будинку", "House number");
    public static string QueuePickerTitle => T("Черга (черга.підгрупа)", "Queue (queue.subgroup)");
    public static string NotificationsAndStartup => T("Сповіщення та запуск", "Notifications and startup");
    public static string NotifyPickerTitle => T("Попереджати перед зміною", "Warn before a change");
    public static string NotifyNever => T("Не сповіщати", "Don't notify");
    public static string NotifyBefore(int minutes) => T($"За {minutes} хвилин", $"{minutes} minutes before");
    public static string NotifyOnChange => T(
        "Повідомляти, коли графік змінився або з'явився на завтра",
        "Notify when the schedule changes or tomorrow's schedule appears");
    public static string AutoStart => T("Запускати разом з Windows", "Start with Windows");
    public static string StartMinimized => T("Запускати згорнутим у трей", "Start minimized to tray");
    public static string ShowDesktopWidget => T("Показувати віджет на робочому столі", "Show desktop widget");
    public static string LanguageSection => T("Мова", "Language");
    public static string LanguagePickerTitle => T("Мова інтерфейсу", "Interface language");
    public static string Save => T("Зберегти", "Save");
    public static string Cancel => T("Скасувати", "Cancel");

    public static string LoadingCities => T("Завантажую список населених пунктів…", "Loading the list of settlements…");
    public static string LoadCitiesFailed(string error) => T($"Не вдалося завантажити список: {error}", $"Failed to load the list: {error}");
    public static string LoadingStreets => T("Завантажую вулиці…", "Loading streets…");
    public static string StreetsNotFound => T(
        "Вулиці не знайдено — виберіть населений пункт зі списку вище.",
        "No streets found — pick a settlement from the list above.");
    public static string LoadStreetsFailed(string error) => T($"Не вдалося завантажити вулиці: {error}", $"Failed to load streets: {error}");
    public static string LoadingHouses => T("Завантажую будинки…", "Loading houses…");
    public static string HousesNotFound => T(
        "Будинки не знайдено — виберіть вулицю зі списку вище.",
        "No houses found — pick a street from the list above.");
    public static string LoadHousesFailed(string error) => T($"Не вдалося завантажити будинки: {error}", $"Failed to load houses: {error}");

    // ---------- Search list ----------
    public static string NotSelected => T("Не вибрано", "Not selected");
    public static string SelectedValue(string value) => T($"Вибрано: {value}", $"Selected: {value}");
    public static string SelectAboveFirst => T("Спочатку виберіть значення вище", "Select a value above first");
}
