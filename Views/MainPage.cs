using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using SvitloTray.Core;
using SvitloTray.Services;

namespace SvitloTray.Views;

/// <summary>Main window: state, countdown, schedule for today/tomorrow, address and settings.</summary>
public sealed class MainPage : ContentPage
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly Color Red = Color.FromArgb("#D63031");
    static readonly Color Neutral = Color.FromArgb("#6E7681");

    static readonly List<string> Queues =
        Enumerable.Range(1, 6).SelectMany(q => new[] { $"{q}.1", $"{q}.2" }).ToList();
    static readonly int[] NotifyValues = { 0, 5, 10, 15, 30, 60 };
    static readonly List<string> LanguageNames = Loc.All.Select(Loc.NativeName).ToList();

    static List<string> ModeNames() => new() { S.ModeAddress, S.ModeQueue, S.ModeLink };

    static List<string> NotifyNames() =>
        NotifyValues.Select(v => v == 0 ? S.NotifyNever : S.NotifyBefore(v)).ToList();

    readonly ScheduleService _svc;
    readonly EnergyUaClient _client;
    readonly IPlatformIntegration _platform;

    // State card
    readonly Border _card;
    readonly Label _statusLabel, _nextLabel, _afterLabel, _addressLabel;

    // Today / tomorrow
    readonly Label _todayTitle, _todayList, _tomorrowTitle, _tomorrowList;
    readonly GraphicsView _todayView, _tomorrowView;
    readonly DayTimelineDrawable _todayDrawable = new(), _tomorrowDrawable = new();
    readonly Label _updatedLabel, _errorLabel;
    readonly Button _refreshButton, _settingsButton;

    // Settings
    readonly VerticalStackLayout _settingsPanel, _addressPanel;
    readonly Picker _modePicker, _queuePicker, _notifyPicker, _languagePicker;
    readonly SearchList _cityList, _streetList, _houseList;
    readonly Entry _linkEntry;
    readonly CheckBox _changeNotifyBox, _autoStartBox, _minimizedBox;
    readonly Label _changeNotifyLabel, _autoStartLabel, _minimizedLabel;
    readonly Label _sourceSection, _notifySection, _languageSection;
    readonly Label _settingsError;
    readonly Button _saveButton, _cancelButton;
    readonly ScrollView _scroll;
    readonly VerticalStackLayout _pageStack;

    AppSettings _draft;
    bool _citiesLoaded;
    bool _applyingTexts;
    int _loadVersion;

    public MainPage(ScheduleService svc, EnergyUaClient client, IPlatformIntegration platform)
    {
        _svc = svc;
        _client = client;
        _platform = platform;
        _draft = svc.Settings.Clone();

        // ---- card ----
        _statusLabel = new Label { FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
        _nextLabel = new Label { FontSize = 17, TextColor = Colors.White };
        _afterLabel = new Label { FontSize = 14, TextColor = Colors.White, Opacity = 0.9 };
        _addressLabel = new Label { FontSize = 13, TextColor = Colors.White, Opacity = 0.85, Margin = new Thickness(0, 6, 0, 0) };
        _card = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
            Padding = new Thickness(18, 14),
            BackgroundColor = Neutral,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children = { _statusLabel, _nextLabel, _afterLabel, _addressLabel },
            },
        };

        // ---- schedules ----
        _todayTitle = SectionTitle();
        _todayView = new GraphicsView { Drawable = _todayDrawable, HeightRequest = 46 };
        _todayList = new Label { FontSize = 14 };
        _tomorrowTitle = SectionTitle();
        _tomorrowView = new GraphicsView { Drawable = _tomorrowDrawable, HeightRequest = 46 };
        _tomorrowList = new Label { FontSize = 14 };

        _refreshButton = new Button();
        _refreshButton.Clicked += async (_, _) => await _svc.RefreshAsync();
        _updatedLabel = new Label { FontSize = 12, TextColor = Colors.Gray, VerticalOptions = LayoutOptions.Center };
        _errorLabel = new Label { FontSize = 13, TextColor = Red, IsVisible = false };

        // ---- settings ----
        _settingsButton = new Button();
        _settingsButton.Clicked += (_, _) => ToggleSettings();

        _modePicker = new Picker();
        _modePicker.SelectedIndexChanged += (_, _) => { if (!_applyingTexts) OnModeChanged(); };

        _cityList = new SearchList();
        _streetList = new SearchList();
        _houseList = new SearchList();
        _cityList.ItemSelected += city => _ = OnCitySelectedAsync(city);
        _streetList.ItemSelected += street => _ = OnStreetSelectedAsync(street);
        _houseList.ItemSelected += house => _draft.House = house;
        _addressPanel = new VerticalStackLayout { Spacing = 12, Children = { _cityList, _streetList, _houseList } };

        _queuePicker = new Picker { ItemsSource = Queues };
        _linkEntry = new Entry { Placeholder = "https://chernigiv.energy-ua.info/grafik/…" };
        _notifyPicker = new Picker();
        _languagePicker = new Picker { ItemsSource = LanguageNames };
        _changeNotifyBox = new CheckBox();
        _autoStartBox = new CheckBox();
        _minimizedBox = new CheckBox();
        _changeNotifyLabel = CheckLabel(_changeNotifyBox);
        _autoStartLabel = CheckLabel(_autoStartBox);
        _minimizedLabel = CheckLabel(_minimizedBox);
        _sourceSection = SectionTitle();
        _notifySection = SectionTitle();
        _languageSection = SectionTitle();
        _settingsError = new Label { FontSize = 13, TextColor = Red, IsVisible = false };

        _saveButton = new Button();
        _saveButton.Clicked += (_, _) => Save();
        _cancelButton = new Button();
        _cancelButton.Clicked += (_, _) => ToggleSettings();

        _settingsPanel = new VerticalStackLayout
        {
            Spacing = 10,
            IsVisible = false,
            Children =
            {
                _sourceSection,
                _modePicker,
                _addressPanel,
                _queuePicker,
                _linkEntry,
                _notifySection,
                _notifyPicker,
                CheckRow(_changeNotifyBox, _changeNotifyLabel),
                CheckRow(_autoStartBox, _autoStartLabel),
                CheckRow(_minimizedBox, _minimizedLabel),
                _languageSection,
                _languagePicker,
                _settingsError,
                new HorizontalStackLayout { Spacing = 10, Children = { _saveButton, _cancelButton } },
            },
        };

        // Invisible browser — fallback path in case Cloudflare blocks HttpClient.
        var browser = new WebView { WidthRequest = 1, HeightRequest = 1, Opacity = 0.01, InputTransparent = true };
        _client.BrowserFallback = new WebViewFetcher(browser);

        _pageStack = new VerticalStackLayout
        {
            Padding = new Thickness(16),
            Spacing = 10,
            Children =
            {
                _card,
                _todayTitle, _todayView, _todayList,
                _tomorrowTitle, _tomorrowView, _tomorrowList,
                new HorizontalStackLayout { Spacing = 12, Children = { _refreshButton, _updatedLabel } },
                _errorLabel,
                _settingsButton,
                _settingsPanel,
                browser,
            },
        };
        _scroll = new ScrollView { Content = _pageStack };
        Content = _scroll;

        // While the settings are open, grow the window so the content fits without scrolling.
        // SizeChanged fires after layout, also when the panel's content changes (source mode, lists loaded).
        _pageStack.SizeChanged += (_, _) => FitWindowToSettings();
        _scroll.SizeChanged += (_, _) => FitWindowToSettings();

        ApplyTexts();
        _svc.Changed += () => MainThread.BeginInvokeOnMainThread(Render);
        Loc.LanguageChanged += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplyTexts();
            Render();
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Render();
    }

    // ================= Static texts =================

    /// <summary>Sets every fixed caption in the current language (on start and after a language switch).</summary>
    void ApplyTexts()
    {
        _applyingTexts = true;
        try
        {
            Title = S.AppName;
            _refreshButton.Text = S.RefreshButton;
            _settingsButton.Text = S.SettingsButton(_settingsPanel.IsVisible);

            _sourceSection.Text = S.DataSource;
            _notifySection.Text = S.NotificationsAndStartup;
            _languageSection.Text = S.LanguageSection;

            _modePicker.Title = S.SourcePickerTitle;
            int modeIndex = _modePicker.SelectedIndex;
            _modePicker.ItemsSource = ModeNames();
            _modePicker.SelectedIndex = modeIndex;

            _notifyPicker.Title = S.NotifyPickerTitle;
            int notifyIndex = _notifyPicker.SelectedIndex;
            _notifyPicker.ItemsSource = NotifyNames();
            _notifyPicker.SelectedIndex = notifyIndex;

            _queuePicker.Title = S.QueuePickerTitle;
            _languagePicker.Title = S.LanguagePickerTitle;

            _cityList.SetTexts(S.City, S.CityPlaceholder);
            _streetList.SetTexts(S.Street, S.StreetPlaceholder);
            _houseList.SetTexts(S.House, S.HousePlaceholder);

            _changeNotifyLabel.Text = S.NotifyOnChange;
            _autoStartLabel.Text = S.AutoStart;
            _minimizedLabel.Text = S.StartMinimized;

            _saveButton.Text = S.Save;
            _cancelButton.Text = S.Cancel;
        }
        finally
        {
            _applyingTexts = false;
        }
    }

    // ================= Rendering =================

    void Render()
    {
        var now = TimeZones.NowKyiv();
        var today = TimeZones.TodayKyiv();
        var f = _svc.Forecast;
        var snap = _svc.Snapshot;
        bool noTomorrow = snap is not null && snap.TomorrowIntervals.Count == 0;

        _statusLabel.Text = Fmt.StatusTitle(f.Current) + (snap?.IsEmergency == true ? S.Emergency : "");
        _card.BackgroundColor = f.Current is { } current ? DayTimelineDrawable.ColorFor(current) : Neutral;

        if (f.Current is null)
        {
            _nextLabel.Text = _svc.IsRefreshing ? S.TrayLoading
                : snap is null ? S.ScheduleNotLoaded
                : S.NoScheduleNow;
            _afterLabel.Text = "";
        }
        else if (f.Next is { } next)
        {
            _nextLabel.Text = S.NextChange(Fmt.ChangeVerb(next.To), Fmt.At(next.At, now), Fmt.Duration(next.At - now));
            _afterLabel.Text = f.Upcoming.Count > 1
                ? S.Then(Fmt.ChangeVerb(f.Upcoming[1].To).ToLowerInvariant(), Fmt.At(f.Upcoming[1].At, now))
                : noTomorrow ? S.TomorrowNotPublished : "";
        }
        else
        {
            _nextLabel.Text = f.KnownUntil is { } until ? S.NoChangesUntil(Fmt.Clock(until, now)) : "";
            _afterLabel.Text = noTomorrow ? S.TomorrowNotPublished : "";
        }
        _afterLabel.IsVisible = !string.IsNullOrEmpty(_afterLabel.Text);
        _addressLabel.Text = _svc.DisplayName;

        if (snap is null)
        {
            _todayTitle.Text = S.TodayTitle;
            _todayDrawable.Segments = Array.Empty<DaySegment>();
            _todayDrawable.NowMinute = null;
            _todayList.Text = "";
            _tomorrowTitle.Text = S.TomorrowTitle;
            _tomorrowDrawable.Segments = Array.Empty<DaySegment>();
            _tomorrowDrawable.NowMinute = null;
            _tomorrowList.Text = "";
        }
        else
        {
            int nowMinute = (int)now.TimeOfDay.TotalMinutes;
            var tomorrowDate = snap.Today.AddDays(1);

            _todayTitle.Text = DayTitle(snap.Today, today);
            _todayDrawable.Segments = ScheduleCalculator.GetDaySegments(snap.TodayIntervals, emptyMeansNoOutages: true);
            _todayDrawable.NowMinute = snap.Today == today ? nowMinute : null;
            _todayList.Text = PeriodsText(_todayDrawable.Segments);

            _tomorrowTitle.Text = DayTitle(tomorrowDate, today);
            _tomorrowDrawable.NowMinute = tomorrowDate == today ? nowMinute : null;
            if (snap.TomorrowIntervals.Count > 0)
            {
                _tomorrowDrawable.Segments = ScheduleCalculator.GetDaySegments(snap.TomorrowIntervals, emptyMeansNoOutages: false);
                _tomorrowList.Text = PeriodsText(_tomorrowDrawable.Segments);
            }
            else
            {
                _tomorrowDrawable.Segments = Array.Empty<DaySegment>();
                _tomorrowList.Text = S.NotPublished;
            }
        }
        _todayView.Invalidate();
        _tomorrowView.Invalidate();

        _updatedLabel.Text = _svc.IsRefreshing ? S.Refreshing
            : snap is null ? ""
            : S.UpdatedAt(snap.FetchedAt.ToLocalTime().ToString("HH:mm", Inv));
        _errorLabel.Text = _svc.LastError ?? "";
        _errorLabel.IsVisible = _svc.LastError is not null;
        _refreshButton.IsEnabled = !_svc.IsRefreshing;
    }

    static string DayTitle(DateOnly date, DateOnly today)
    {
        var d = date.ToString("dd.MM", Inv);
        if (date == today) return $"{S.TodayTitle}, {d}";
        if (date == today.AddDays(1)) return $"{S.TomorrowTitle}, {d}";
        if (date == today.AddDays(-1)) return $"{S.YesterdayTitle}, {d}";
        return d;
    }

    static string PeriodsText(IReadOnlyList<DaySegment> segments) =>
        string.Join("\n", segments.Select(s =>
            $"{Fmt.Minutes(s.From)} – {Fmt.Minutes(s.To)}    {Fmt.StatusShort(s.Status)}"));

    static Label SectionTitle() => new()
    {
        FontSize = 16,
        FontAttributes = FontAttributes.Bold,
        Margin = new Thickness(0, 6, 0, 0),
    };

    static Label CheckLabel(CheckBox box)
    {
        var label = new Label { VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.WordWrap };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => box.IsChecked = !box.IsChecked;
        label.GestureRecognizers.Add(tap);
        return label;
    }

    static View CheckRow(CheckBox box, Label label)
    {
        var grid = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        grid.Add(box, 0, 0);
        grid.Add(label, 1, 0);
        return grid;
    }

    // ================= Settings =================

    void ToggleSettings()
    {
        _settingsPanel.IsVisible = !_settingsPanel.IsVisible;
        _settingsButton.Text = S.SettingsButton(_settingsPanel.IsVisible);
        if (_settingsPanel.IsVisible) LoadSettingsIntoForm();
        else _platform.RestoreWindowSize();
    }

    /// <summary>Grows the window by the amount the content overflows the visible area (only while settings are open).</summary>
    void FitWindowToSettings()
    {
        if (!_settingsPanel.IsVisible || _scroll.Height <= 0 || _pageStack.Height <= 0) return;
        double overflow = _pageStack.Height - _scroll.Height;
        if (overflow > 1) _platform.GrowWindowBy(overflow);
    }

    void LoadSettingsIntoForm()
    {
        _draft = _svc.Settings.Clone();
        _settingsError.IsVisible = false;

        _queuePicker.SelectedItem = Queues.Contains(_draft.Queue) ? _draft.Queue : null;
        _linkEntry.Text = _draft.Link;
        int notifyIndex = Array.IndexOf(NotifyValues, _draft.NotifyMinutesBefore);
        _notifyPicker.SelectedIndex = notifyIndex >= 0 ? notifyIndex : 3;
        _changeNotifyBox.IsChecked = _draft.NotifyOnScheduleChange;
        _autoStartBox.IsChecked = _draft.AutoStart;
        _minimizedBox.IsChecked = _draft.StartMinimized;
        _languagePicker.SelectedIndex = Math.Max(0, Loc.All.ToList().IndexOf(_draft.Language));

        _cityList.Preselect(_draft.City);
        _streetList.Preselect(_draft.Street);
        _houseList.Preselect(_draft.House);

        if (_modePicker.SelectedIndex == (int)_draft.Mode) OnModeChanged();
        else _modePicker.SelectedIndex = (int)_draft.Mode; // triggers OnModeChanged
    }

    SourceMode SelectedMode =>
        _modePicker.SelectedIndex < 0 ? SourceMode.Address : (SourceMode)_modePicker.SelectedIndex;

    void OnModeChanged()
    {
        var mode = SelectedMode;
        _addressPanel.IsVisible = mode == SourceMode.Address;
        _queuePicker.IsVisible = mode == SourceMode.Queue;
        _linkEntry.IsVisible = mode == SourceMode.Link;
        if (mode == SourceMode.Address && !_citiesLoaded) _ = LoadCitiesAsync();
    }

    async Task LoadCitiesAsync()
    {
        _citiesLoaded = true;
        _cityList.SetStatus(() => S.LoadingCities);
        try
        {
            var cities = await _client.GetCitiesAsync(_draft.BaseUrl);
            _cityList.SetItems(cities);
            if (!string.IsNullOrWhiteSpace(_draft.City))
                await LoadStreetsAsync(_draft.City, keepSelection: true);
        }
        catch (Exception ex)
        {
            _citiesLoaded = false;
            var message = ex.Message;
            _cityList.SetStatus(() => S.LoadCitiesFailed(message));
        }
    }

    async Task OnCitySelectedAsync(string city)
    {
        _draft.City = city;
        _draft.Street = "";
        _draft.House = "";
        await LoadStreetsAsync(city, keepSelection: false);
    }

    async Task OnStreetSelectedAsync(string street)
    {
        _draft.Street = street;
        _draft.House = "";
        await LoadHousesAsync(_draft.City, street, keepSelection: false);
    }

    async Task LoadStreetsAsync(string city, bool keepSelection)
    {
        int version = ++_loadVersion;
        if (!keepSelection)
        {
            _streetList.Clear();
            _houseList.Clear();
        }
        _streetList.SetStatus(() => S.LoadingStreets);
        try
        {
            var streets = await _client.GetStreetsAsync(_draft.BaseUrl, city);
            if (version != _loadVersion) return;
            _streetList.SetItems(streets);
            if (keepSelection && !string.IsNullOrWhiteSpace(_draft.Street))
                await LoadHousesAsync(city, _draft.Street, keepSelection: true);
        }
        catch (EnergyUaNotFoundException)
        {
            if (version == _loadVersion)
                _streetList.SetStatus(() => S.StreetsNotFound);
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (version == _loadVersion)
                _streetList.SetStatus(() => S.LoadStreetsFailed(message));
        }
    }

    async Task LoadHousesAsync(string city, string street, bool keepSelection)
    {
        int version = ++_loadVersion;
        if (!keepSelection) _houseList.Clear();
        _houseList.SetStatus(() => S.LoadingHouses);
        try
        {
            var houses = await _client.GetHousesAsync(_draft.BaseUrl, city, street);
            if (version != _loadVersion) return;
            _houseList.SetItems(SortHouses(houses));
        }
        catch (EnergyUaNotFoundException)
        {
            if (version == _loadVersion)
                _houseList.SetStatus(() => S.HousesNotFound);
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (version == _loadVersion)
                _houseList.SetStatus(() => S.LoadHousesFailed(message));
        }
    }

    static IReadOnlyList<string> SortHouses(IReadOnlyList<string> houses) =>
        houses.Distinct()
              .OrderBy(LeadingNumber)
              .ThenBy(h => h, StringComparer.OrdinalIgnoreCase)
              .ToList();

    static int LeadingNumber(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return i > 0 && int.TryParse(s.AsSpan(0, Math.Min(i, 9)), out var n) ? n : int.MaxValue;
    }

    void Save()
    {
        var s = _draft.Clone();
        s.Mode = SelectedMode;
        if (_queuePicker.SelectedItem is string queue) s.Queue = queue;
        s.Link = _linkEntry.Text?.Trim() ?? "";
        s.NotifyMinutesBefore = _notifyPicker.SelectedIndex >= 0 ? NotifyValues[_notifyPicker.SelectedIndex] : 15;
        s.NotifyOnScheduleChange = _changeNotifyBox.IsChecked;
        s.AutoStart = _autoStartBox.IsChecked;
        s.StartMinimized = _minimizedBox.IsChecked;
        if (_languagePicker.SelectedIndex >= 0) s.Language = Loc.All[_languagePicker.SelectedIndex];

        try
        {
            s.ResolveSource(); // checks that address/queue/link are filled in correctly
        }
        catch (EnergyUaException ex)
        {
            _settingsError.Text = ex.Message;
            _settingsError.IsVisible = true;
            return;
        }

        _platform.SetAutoStart(s.AutoStart);
        _svc.ApplySettings(s);
        ToggleSettings();
    }
}
