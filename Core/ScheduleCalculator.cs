namespace SvitloTray.Core;

/// <summary>
/// Schedule logic ported from energy-ua.info (assets/js/schedule-live.js)
/// and extended: today and tomorrow are joined into one timeline so that
/// the "next power on/off" can be computed across midnight.
/// </summary>
public static class ScheduleCalculator
{
    public const int MinutesPerDay = 1440;

    /// <summary>
    /// Port of getSegments(): splits the day at every interval boundary;
    /// for each piece red wins over yellow and yellow wins over green;
    /// adjacent pieces with the same status are merged.
    /// </summary>
    public static List<DaySegment> GetSegments(IReadOnlyList<RawInterval> source)
    {
        var result = new List<DaySegment>();
        if (source.Count == 0) return result;

        var points = new SortedSet<int> { 0, MinutesPerDay };
        foreach (var p in source)
        {
            points.Add(Math.Clamp(p.From, 0, MinutesPerDay));
            points.Add(Math.Clamp(p.To, 0, MinutesPerDay));
        }

        var sorted = points.ToArray();
        for (int i = 0; i < sorted.Length - 1; i++)
        {
            int start = sorted[i], end = sorted[i + 1];
            if (end <= start) continue;

            var status = PowerStatus.On;
            foreach (var p in source)
            {
                if (p.From < end && p.To > start)
                {
                    if (p.Status == PowerStatus.Off) { status = PowerStatus.Off; break; }
                    if (p.Status == PowerStatus.Maybe) status = PowerStatus.Maybe;
                }
            }

            if (result.Count > 0 && result[^1].Status == status && result[^1].To == start)
                result[^1] = result[^1] with { To = end };
            else
                result.Add(new DaySegment(start, end, status));
        }
        return result;
    }

    /// <summary>
    /// Segments for a specific day. The site shows an empty list for today as
    /// "no data"; the app treats it as "no outages" for the whole day.
    /// </summary>
    public static List<DaySegment> GetDaySegments(IReadOnlyList<RawInterval> source, bool emptyMeansNoOutages)
    {
        var segments = GetSegments(source);
        if (segments.Count == 0 && emptyMeansNoOutages)
            segments.Add(new DaySegment(0, MinutesPerDay, PowerStatus.On));
        return segments;
    }

    /// <summary>Today + tomorrow (if published) on one Kyiv-time timeline.</summary>
    public static List<TimeSegment> BuildTimeline(ScheduleSnapshot snapshot, TimeZoneInfo tz)
    {
        var raw = new List<TimeSegment>();
        AddDay(raw, snapshot.Today, GetDaySegments(snapshot.TodayIntervals, emptyMeansNoOutages: true), tz);
        if (snapshot.TomorrowIntervals.Count > 0)
            AddDay(raw, snapshot.Today.AddDays(1), GetDaySegments(snapshot.TomorrowIntervals, emptyMeansNoOutages: false), tz);

        // Merge equal states across midnight (23:00–24:00 + 00:00–02:00).
        var merged = new List<TimeSegment>();
        foreach (var seg in raw)
        {
            if (merged.Count > 0 && merged[^1].Status == seg.Status && merged[^1].End == seg.Start)
                merged[^1] = merged[^1] with { End = seg.End };
            else
                merged.Add(seg);
        }
        return merged;
    }

    static void AddDay(List<TimeSegment> into, DateOnly date, List<DaySegment> segments, TimeZoneInfo tz)
    {
        foreach (var s in segments)
            into.Add(new TimeSegment(At(date, s.From, tz), At(date, s.To, tz), s.Status));
    }

    /// <summary>Date + minutes since midnight in Kyiv time → absolute instant.</summary>
    public static DateTimeOffset At(DateOnly date, int minutes, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    /// <summary>Current state and the nearest changes.</summary>
    public static Forecast GetForecast(IReadOnlyList<TimeSegment> timeline, DateTimeOffset now, int maxUpcoming = 4)
    {
        if (timeline.Count == 0) return new Forecast();

        var knownUntil = timeline[^1].End;
        int idx = -1;
        for (int i = 0; i < timeline.Count; i++)
        {
            if (timeline[i].Start <= now && now < timeline[i].End) { idx = i; break; }
        }
        if (idx < 0) return new Forecast { KnownUntil = knownUntil };

        // After merging, adjacent segments always differ in status,
        // so the start of each next segment is a state change.
        var upcoming = new List<UpcomingChange>();
        for (int i = idx + 1; i < timeline.Count && upcoming.Count < maxUpcoming; i++)
            upcoming.Add(new UpcomingChange(timeline[i].Start, timeline[i].Status));

        return new Forecast
        {
            Current = timeline[idx].Status,
            Upcoming = upcoming,
            KnownUntil = knownUntil,
        };
    }

    /// <summary>Schedule comparison for the "schedule changed" notification.</summary>
    public static bool SameSchedule(IReadOnlyList<RawInterval> a, IReadOnlyList<RawInterval> b)
    {
        var sa = GetSegments(a);
        var sb = GetSegments(b);
        return sa.SequenceEqual(sb);
    }
}
