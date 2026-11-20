using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using SvitloTray.Core;

namespace SvitloTray.Views;

/// <summary>Compact status window intended to sit on the desktop.</summary>
public sealed class WidgetPage : ContentPage
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly Color Neutral = Color.FromArgb("#6E7681");

    readonly ScheduleService _svc;
    readonly Label _dragHandle, _nameLabel, _statusLabel, _nextLabel, _updatedLabel;
    readonly Label _todayTitle, _tomorrowTitle;
    readonly Border _statusCard;
    readonly GraphicsView _todayView, _tomorrowView;
    readonly DayTimelineDrawable _todayDrawable = new(), _tomorrowDrawable = new();

    public WidgetPage(ScheduleService svc)
    {
        _svc = svc;

        _dragHandle = new Label
        {
            Text = "⋮⋮",
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#AAB6C5"),
            VerticalOptions = LayoutOptions.Center,
        };
        _nameLabel = new Label
        {
            FontSize = 12,
            TextColor = Color.FromArgb("#CBD2DA"),
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center,
        };
        _statusLabel = new Label
        {
            FontSize = 23,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
        };
        _nextLabel = new Label
        {
            FontSize = 15,
            TextColor = Colors.White,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        _updatedLabel = new Label { FontSize = 11, TextColor = Color.FromArgb("#CBD2DA") };
        _todayTitle = TimelineTitle();
        _tomorrowTitle = TimelineTitle();
        _todayView = new GraphicsView { Drawable = _todayDrawable, HeightRequest = 46 };
        _tomorrowView = new GraphicsView { Drawable = _tomorrowDrawable, HeightRequest = 46 };
        _statusCard = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(14, 12),
            Background = Neutral,
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children = { _statusLabel, _nextLabel },
            },
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 8,
            Children = { _dragHandle, _nameLabel },
        };
        Grid.SetColumn(_nameLabel, 1);

        var content = new Grid
        {
            Padding = 14,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            RowSpacing = 4,
            Children = { header, _statusCard, _todayTitle, _todayView, _tomorrowTitle, _tomorrowView, _updatedLabel },
        };
        Grid.SetRow(_statusCard, 1);
        Grid.SetRow(_todayTitle, 2);
        Grid.SetRow(_todayView, 3);
        Grid.SetRow(_tomorrowTitle, 4);
        Grid.SetRow(_tomorrowView, 5);
        Grid.SetRow(_updatedLabel, 6);

        Background = CreateBackground();
        Content = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(22) },
            Background = CreateBackground(),
            Content = content,
        };

        Title = S.WidgetTitle;
        _svc.Changed += OnScheduleChanged;
        Loc.LanguageChanged += OnLanguageChanged;
        Render();
    }

    void OnScheduleChanged() => MainThread.BeginInvokeOnMainThread(Render);
    void OnLanguageChanged() => MainThread.BeginInvokeOnMainThread(Render);

    static Label TimelineTitle() => new()
    {
        FontSize = 11,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#E1E8F0"),
    };

    static LinearGradientBrush CreateBackground() => new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop(Color.FromArgb("#17243A"), 0),
            new GradientStop(Color.FromArgb("#263F5B"), 0.55f),
            new GradientStop(Color.FromArgb("#31536A"), 1),
        },
    };

    void Render()
    {
        var now = TimeZones.NowKyiv();
        var forecast = _svc.Forecast;
        var snapshot = _svc.Snapshot;

        _nameLabel.Text = _svc.DisplayName;
        _statusLabel.Text = Fmt.StatusTitle(forecast.Current) + (snapshot?.IsEmergency == true ? S.Emergency : "");
        _statusCard.Background = forecast.Current is { } current
            ? DayTimelineDrawable.ColorFor(current)
            : Neutral;

        _nextLabel.Text = forecast.Next is { } next
            ? S.NextChange(Fmt.ChangeVerb(next.To), Fmt.At(next.At, now), Fmt.Duration(next.At - now))
            : forecast.KnownUntil is { } until
                ? S.NoChangesUntil(Fmt.Clock(until, now))
                : _svc.IsRefreshing ? S.TrayLoading : S.ScheduleNotLoaded;

        _updatedLabel.Text = snapshot is null
            ? _svc.LastError ?? ""
            : S.UpdatedAt(snapshot.FetchedAt.ToLocalTime().ToString("HH:mm", Inv));

        if (snapshot is null)
        {
            _todayTitle.Text = S.TodayTitle;
            _tomorrowTitle.Text = S.TomorrowTitle;
            _todayDrawable.Segments = Array.Empty<DaySegment>();
            _tomorrowDrawable.Segments = Array.Empty<DaySegment>();
            _todayDrawable.NowMinute = null;
            _tomorrowDrawable.NowMinute = null;
        }
        else
        {
            var today = TimeZones.TodayKyiv();
            var tomorrow = snapshot.Today.AddDays(1);
            int nowMinute = (int)now.TimeOfDay.TotalMinutes;

            _todayTitle.Text = DayTitle(snapshot.Today, today);
            _todayDrawable.Segments = ScheduleCalculator.GetDaySegments(snapshot.TodayIntervals, emptyMeansNoOutages: true);
            _todayDrawable.NowMinute = snapshot.Today == today ? nowMinute : null;

            _tomorrowTitle.Text = snapshot.TomorrowIntervals.Count > 0
                ? DayTitle(tomorrow, today)
                : $"{DayTitle(tomorrow, today)} · {S.NotPublished}";
            _tomorrowDrawable.Segments = snapshot.TomorrowIntervals.Count > 0
                ? ScheduleCalculator.GetDaySegments(snapshot.TomorrowIntervals, emptyMeansNoOutages: false)
                : Array.Empty<DaySegment>();
            _tomorrowDrawable.NowMinute = tomorrow == today ? nowMinute : null;
        }

        _todayView.Invalidate();
        _tomorrowView.Invalidate();
    }

    static string DayTitle(DateOnly date, DateOnly today)
    {
        var label = date == today ? S.TodayTitle
            : date == today.AddDays(1) ? S.TomorrowTitle
            : date == today.AddDays(-1) ? S.YesterdayTitle
            : "";
        return $"{label} · {date.ToString("dd.MM", Inv)}";
    }
}
