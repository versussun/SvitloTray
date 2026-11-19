namespace SvitloTray.Core;

/// <summary>
/// Holds the current schedule, recomputes the countdown every 20 seconds,
/// reloads data from the site every 15 minutes (and right after each state change
/// or midnight), and raises notifications.
/// </summary>
public sealed class ScheduleService : IDisposable
{
    static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(20);
    static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    readonly EnergyUaClient _client;
    readonly SettingsStore _store;
    readonly SemaphoreSlim _refreshLock = new(1, 1);
    readonly HashSet<string> _notified = new();
    CancellationTokenSource? _loopCts;
    DateTimeOffset _nextRefreshAt = DateTimeOffset.MinValue;
    volatile bool _forceRefresh;
    int _failures;

    public ScheduleService(EnergyUaClient client, SettingsStore store)
    {
        _client = client;
        _store = store;
    }

    public AppSettings Settings => _store.Current;
    public ScheduleSnapshot? Snapshot { get; private set; }
    public IReadOnlyList<TimeSegment> Timeline { get; private set; } = Array.Empty<TimeSegment>();
    public Forecast Forecast { get; private set; } = new();
    public string? LastError { get; private set; }
    public bool IsRefreshing { get; private set; }

    /// <summary>State has changed (raised from a background thread).</summary>
    public event Action? Changed;

    /// <summary>A notification should be shown: title, text (raised from a background thread).</summary>
    public event Action<string, string>? Notify;

    public string DisplayName
    {
        get
        {
            var q = Fmt.ShortQueue(Snapshot?.Queue);
            bool queuePage = Settings.Mode == SourceMode.Queue ||
                             (Snapshot?.SourceUrl.Contains("/cherga/", StringComparison.OrdinalIgnoreCase) ?? false);
            if (queuePage) return q is null ? Settings.FallbackName : S.QueueName(q);

            var name = EnergyUaParser.CleanTitle(Snapshot?.Title) ?? Settings.FallbackName;
            return q is null ? name : S.NameWithQueue(name, q);
        }
    }

    public string? CurrentSourceUrl
    {
        get
        {
            try
            {
                var (baseUrl, path) = Settings.ResolveSource();
                return EnergyUaClient.Combine(baseUrl, path);
            }
            catch (EnergyUaException) { return null; }
        }
    }

    public void Start()
    {
        if (_loopCts is not null) return;

        // Last schedule from the cache — so something is shown right away, even offline.
        var cached = _store.LoadCachedSnapshot();
        if (cached is not null && cached.SourceUrl == CurrentSourceUrl) Snapshot = cached;
        Rebuild(TimeZones.NowKyiv());
        _nextRefreshAt = DateTimeOffset.MinValue;
        _loopCts = new CancellationTokenSource();
        _ = LoopAsync(_loopCts.Token);
    }

    async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TickInterval);
            do
            {
                var now = TimeZones.NowKyiv();
                if (now >= _nextRefreshAt) await RefreshAsync(ct).ConfigureAwait(false);
                else UpdateForecast(now);
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Load the schedule from the site right now.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await _refreshLock.WaitAsync(0, ct).ConfigureAwait(false))
        {
            // A load is already running — repeat right after it (e.g. the address was just changed).
            _forceRefresh = true;
            return;
        }
        try
        {
            IsRefreshing = true;
            RaiseChanged();

            var sourceKey = SourceKey(Settings);
            var (baseUrl, path) = Settings.ResolveSource();
            var fresh = await _client.GetScheduleAsync(baseUrl, path, ct).ConfigureAwait(false);

            // The user changed the address while loading — the result is no longer needed.
            if (sourceKey != SourceKey(Settings))
            {
                _forceRefresh = true;
                return;
            }

            DetectScheduleChanges(Snapshot, fresh);
            Snapshot = fresh;
            LastError = null;
            _failures = 0;
            _store.SaveCachedSnapshot(fresh);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _failures++;
        }
        finally
        {
            IsRefreshing = false;
            Rebuild(TimeZones.NowKyiv());
            if (_forceRefresh)
            {
                _forceRefresh = false;
                _nextRefreshAt = DateTimeOffset.MinValue; // the next tick (≤20 s) loads the new address
            }
            _refreshLock.Release();
        }
    }

    /// <summary>New address / settings: reset the data and load again.</summary>
    public void ApplySettings(AppSettings settings)
    {
        var sourceChanged = SourceKey(settings) != SourceKey(Settings);
        _store.Save(settings);
        if (sourceChanged)
        {
            Snapshot = null;
            _store.ClearCachedSnapshot();
            lock (_notified) _notified.Clear();
            Rebuild(TimeZones.NowKyiv());
        }

        _nextRefreshAt = DateTimeOffset.MinValue;
        _ = RefreshAsync();
    }

    static string SourceKey(AppSettings s) =>
        string.Join("|", s.Mode, s.BaseUrl, s.City, s.Street, s.House, s.Queue, s.Link);

    void Rebuild(DateTimeOffset now)
    {
        Timeline = Snapshot is null
            ? Array.Empty<TimeSegment>()
            : ScheduleCalculator.BuildTimeline(Snapshot, TimeZones.Kyiv);
        UpdateForecast(now);
        ScheduleNextRefresh(now);
    }

    void UpdateForecast(DateTimeOffset now)
    {
        Forecast = ScheduleCalculator.GetForecast(Timeline, now);
        CheckUpcomingNotification(now);
        RaiseChanged();
    }

    void ScheduleNextRefresh(DateTimeOffset now)
    {
        var next = LastError is null
            ? now + RefreshInterval
            : now + TimeSpan.FromMinutes(Math.Min(15, 1 << Math.Min(_failures, 4))); // 2, 4, 8, 15 min

        // Right after a state change — check whether the schedule was updated.
        if (Forecast.Next is { } change && change.At > now)
            next = Min(next, change.At + TimeSpan.FromMinutes(1));

        // After midnight the site's "today" moves forward.
        var midnight = new DateTimeOffset(now.Date.AddDays(1), now.Offset) + TimeSpan.FromMinutes(1);
        _nextRefreshAt = Min(next, midnight);
    }

    static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    void CheckUpcomingNotification(DateTimeOffset now)
    {
        int before = Settings.NotifyMinutesBefore;
        if (before <= 0 || Forecast.Next is not { } next) return;

        var left = next.At - now;
        if (left <= TimeSpan.Zero || left > TimeSpan.FromMinutes(before)) return;
        lock (_notified)
        {
            if (!_notified.Add($"{next.At:O}|{next.To}")) return;
        }

        var title = next.To switch
        {
            PowerStatus.Off => S.NotifyOffSoon,
            PowerStatus.On => S.NotifyOnSoon,
            _ => S.NotifyMaybeSoon,
        };
        var text = $"{Fmt.ChangeVerb(next.To)} {Fmt.At(next.At, now)} ({S.In(Fmt.Duration(left))})";
        if (Forecast.Upcoming.Count > 1)
            text += ", " + S.Until(Fmt.StatusShort(next.To), Fmt.Clock(Forecast.Upcoming[1].At, now));
        Notify?.Invoke(title, text);
    }

    void DetectScheduleChanges(ScheduleSnapshot? old, ScheduleSnapshot fresh)
    {
        if (!Settings.NotifyOnScheduleChange || old is null || old.SourceUrl != fresh.SourceUrl) return;

        if (old.Today == fresh.Today)
        {
            if (!ScheduleCalculator.SameSchedule(old.TodayIntervals, fresh.TodayIntervals))
                Notify?.Invoke(S.TodayChanged, S.Outages(Fmt.OffPeriods(fresh.TodayIntervals)));

            if (fresh.TomorrowIntervals.Count > 0)
            {
                if (old.TomorrowIntervals.Count == 0)
                    Notify?.Invoke(S.TomorrowPublished, S.Outages(Fmt.OffPeriods(fresh.TomorrowIntervals)));
                else if (!ScheduleCalculator.SameSchedule(old.TomorrowIntervals, fresh.TomorrowIntervals))
                    Notify?.Invoke(S.TomorrowChanged, S.Outages(Fmt.OffPeriods(fresh.TomorrowIntervals)));
            }
        }
        else if (old.Today.AddDays(1) == fresh.Today &&
                 old.TomorrowIntervals.Count > 0 &&
                 !ScheduleCalculator.SameSchedule(old.TomorrowIntervals, fresh.TodayIntervals))
        {
            Notify?.Invoke(S.TodayChanged, S.Outages(Fmt.OffPeriods(fresh.TodayIntervals)));
        }
    }

    void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception) { /* UI errors must not break the background loop */ }
    }

    public void Dispose()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
    }
}
