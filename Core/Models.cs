namespace SvitloTray.Core;

/// <summary>Power supply state. On the site: green / red / yellow.</summary>
public enum PowerStatus
{
    On,     // green  — power is on
    Off,    // red    — power is off
    Maybe,  // yellow — possible outage
}

/// <summary>Interval from the site's JSON: minutes since midnight (0..1440).</summary>
public sealed record RawInterval(int From, int To, PowerStatus Status);

/// <summary>Part of a day after normalization (like getSegments() in schedule-live.js).</summary>
public sealed record DaySegment(int From, int To, PowerStatus Status);

/// <summary>Segment on an absolute timeline (today and tomorrow joined).</summary>
public sealed record TimeSegment(DateTimeOffset Start, DateTimeOffset End, PowerStatus Status);

/// <summary>Upcoming state change.</summary>
public sealed record UpcomingChange(DateTimeOffset At, PowerStatus To);

/// <summary>Data of one energy-ua.info schedule page.</summary>
public sealed class ScheduleSnapshot
{
    public required DateOnly Today { get; init; }
    public required IReadOnlyList<RawInterval> TodayIntervals { get; init; }
    public required IReadOnlyList<RawInterval> TomorrowIntervals { get; init; }
    public bool TomorrowAvailable { get; init; }
    public bool IsEmergency { get; init; }

    /// <summary>Queue badge text as shown on the site, e.g. "2 черга (1 підгрупа)".</summary>
    public string? Queue { get; init; }

    /// <summary>The page's h1 heading.</summary>
    public string? Title { get; init; }

    public required string SourceUrl { get; init; }
    public DateTimeOffset FetchedAt { get; init; }
}

/// <summary>What is happening now and what comes next.</summary>
public sealed class Forecast
{
    /// <summary>null — now is outside the known schedule.</summary>
    public PowerStatus? Current { get; init; }

    public IReadOnlyList<UpcomingChange> Upcoming { get; init; } = Array.Empty<UpcomingChange>();

    /// <summary>Until when the schedule is known (24:00 today or tomorrow).</summary>
    public DateTimeOffset? KnownUntil { get; init; }

    public UpcomingChange? Next => Upcoming.Count > 0 ? Upcoming[0] : null;
}

public class EnergyUaException : Exception
{
    public EnergyUaException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class EnergyUaNotFoundException : EnergyUaException
{
    public EnergyUaNotFoundException(string url)
        : base(S.ErrNotFound) => Url = url;

    public string Url { get; }
}
