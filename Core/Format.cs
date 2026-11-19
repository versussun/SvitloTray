using System.Globalization;

namespace SvitloTray.Core;

public static class TimeZones
{
    /// <summary>Schedules are published in Kyiv time.</summary>
    public static TimeZoneInfo Kyiv { get; } = Find();

    static TimeZoneInfo Find()
    {
        foreach (var id in new[] { "Europe/Kyiv", "Europe/Kiev", "FLE Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    public static DateTimeOffset NowKyiv() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Kyiv);

    public static DateOnly TodayKyiv() => DateOnly.FromDateTime(NowKyiv().DateTime);
}

/// <summary>Texts for the window, the tooltip and the tray icon.</summary>
public static class Fmt
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>"1 h 25 min", "45 min", "2 h".</summary>
    public static string Duration(TimeSpan span)
    {
        var totalMin = (int)Math.Ceiling(Math.Max(0, span.TotalMinutes));
        if (totalMin <= 0) return S.LessThanMinute;
        int h = totalMin / 60, m = totalMin % 60;
        if (h == 0) return S.Minutes(m);
        if (m == 0) return S.Hours(h);
        return S.HoursMinutes(h, m);
    }

    /// <summary>"at 15:00", "at 24:00", "tomorrow at 08:30".</summary>
    public static string At(DateTimeOffset t, DateTimeOffset now)
    {
        var (day, hm) = Split(t, now);
        return day is null ? S.AtTime(hm) : S.AtDayTime(day, hm);
    }

    /// <summary>"15:00", "24:00", "tomorrow 08:30".</summary>
    public static string Clock(DateTimeOffset t, DateTimeOffset now)
    {
        var (day, hm) = Split(t, now);
        return day is null ? hm : $"{day} {hm}";
    }

    static (string? Day, string HourMinute) Split(DateTimeOffset t, DateTimeOffset now)
    {
        var tl = TimeZoneInfo.ConvertTime(t, TimeZones.Kyiv);
        var nl = TimeZoneInfo.ConvertTime(now, TimeZones.Kyiv);
        int dayDiff = DateOnly.FromDateTime(tl.DateTime).DayNumber - DateOnly.FromDateTime(nl.DateTime).DayNumber;
        var hm = tl.ToString("HH:mm", Inv);

        // Midnight of the next day is shown as 24:00 of today, as on the site.
        if (dayDiff == 1 && tl.Hour == 0 && tl.Minute == 0) return (null, "24:00");
        return dayDiff switch
        {
            0 => (null, hm),
            1 => (S.Tomorrow, hm),
            _ => (tl.ToString("dd.MM", Inv), hm),
        };
    }

    public static string Minutes(int minutes) =>
        minutes >= ScheduleCalculator.MinutesPerDay ? "24:00" : $"{minutes / 60:00}:{minutes % 60:00}";

    public static string StatusTitle(PowerStatus? s) => s switch
    {
        PowerStatus.On => S.StatusOnTitle,
        PowerStatus.Off => S.StatusOffTitle,
        PowerStatus.Maybe => S.StatusMaybeTitle,
        _ => S.StatusUnknownTitle,
    };

    public static string StatusShort(PowerStatus s) => s switch
    {
        PowerStatus.On => S.StatusOnShort,
        PowerStatus.Off => S.StatusOffShort,
        _ => S.StatusMaybeShort,
    };

    /// <summary>Phrase for a change of state to <paramref name="to"/>.</summary>
    public static string ChangeVerb(PowerStatus to) => to switch
    {
        PowerStatus.Off => S.VerbOff,
        PowerStatus.On => S.VerbOn,
        _ => S.VerbMaybe,
    };

    /// <summary>
    /// 1–3 characters for a 16×16 icon: minutes up to 99, then hours ("2h").
    /// </summary>
    public static string IconText(TimeSpan? left)
    {
        if (left is null) return "—";
        int min = (int)Math.Ceiling(Math.Max(0, left.Value.TotalMinutes));
        if (min < 100) return min.ToString(Inv);
        int h = min / 60;
        return h < 100 ? h.ToString(Inv) + S.HoursSuffix : "99+";
    }

    /// <summary>"08:30–15:00, 19:00–22:00" or "no outages".</summary>
    public static string OffPeriods(IReadOnlyList<RawInterval> intervals)
    {
        var parts = ScheduleCalculator.GetSegments(intervals)
            .Where(s => s.Status != PowerStatus.On)
            .Select(s => $"{Minutes(s.From)}–{Minutes(s.To)}" + (s.Status == PowerStatus.Maybe ? S.PossibleSuffix : ""))
            .ToList();
        return parts.Count == 0 ? S.NoOutages : string.Join(", ", parts);
    }

    public static string Clip(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>Site badge text "2 черга (1 підгрупа)" → "2.1".</summary>
    public static string? ShortQueue(string? queue)
    {
        if (string.IsNullOrWhiteSpace(queue)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(queue, @"(\d+)\s*черга\s*\(\s*(\d+)\s*підгрупа\s*\)");
        return m.Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}" : queue.Trim();
    }
}
