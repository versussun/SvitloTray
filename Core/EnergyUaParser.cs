using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SvitloTray.Core;

/// <summary>
/// Parsing of energy-ua.info HTML. The site renders data on the server and puts it
/// into &lt;script type="application/json" id="..."&gt;:
///   schedulePayload   — schedule: {"today":[{"from":510,"to":900,"status":"red"}],"tomorrow":[...],"dates":{...}}
///   selectPagePayload — list of streets/houses: {"mode":"street|house","options":[...]}
///   homeCityPayload   — list of settlements: ["АВГУСТІВКА", ...]
/// </summary>
public static class EnergyUaParser
{
    const RegexOptions Opts = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // The patterns below match Ukrainian text that the site itself renders.
    static readonly Regex QueueBadgeRx = new(@"class\s*=\s*[""'][^""']*\bqueue-badge\b[^""']*[""'][^>]*>(?<q>[^<]+)<", Opts);
    static readonly Regex QueueTextRx = new(@"\d+\s*черга\s*\(\s*\d+\s*підгрупа\s*\)", Opts);
    static readonly Regex H1Rx = new(@"<h1[^>]*>(?<t>.*?)</h1>", Opts);
    static readonly Regex TagRx = new(@"<[^>]+>", Opts);
    static readonly Regex SpacesRx = new(@"\s+", Opts);
    static readonly Regex TitlePrefixRx = new(@"^Графік\s+відключен\w*\s+(електроенергії|світла)\s*", Opts);

    public static string? ExtractJsonScript(string html, string id)
    {
        var rx = new Regex(@"<script[^>]*\bid\s*=\s*[""']" + Regex.Escape(id) + @"[""'][^>]*>(?<json>.*?)</script>", Opts);
        var m = rx.Match(html);
        return m.Success ? m.Groups["json"].Value.Trim() : null;
    }

    public static ScheduleSnapshot ParseSchedulePage(string html, string sourceUrl)
    {
        var json = ExtractJsonScript(html, "schedulePayload")
                   ?? throw new EnergyUaException(S.ErrNoSchedulePayload);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new ScheduleSnapshot
        {
            Today = ReadTodayDate(root),
            TodayIntervals = ReadIntervals(root, "today"),
            TomorrowIntervals = ReadIntervals(root, "tomorrow"),
            TomorrowAvailable = ReadBool(root, "tomorrowAvailable"),
            IsEmergency = ReadBool(root, "isEmergency"),
            Queue = ExtractQueue(html),
            Title = ExtractTitle(html),
            SourceUrl = sourceUrl,
            FetchedAt = DateTimeOffset.Now,
        };
    }

    public static IReadOnlyList<string> ParseSelectOptions(string html)
    {
        var json = ExtractJsonScript(html, "selectPagePayload")
                   ?? throw new EnergyUaException(S.ErrNoSelectPayload);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("options", out var arr) ? ReadStrings(arr) : Array.Empty<string>();
    }

    public static IReadOnlyList<string> ParseCities(string html)
    {
        var json = ExtractJsonScript(html, "homeCityPayload")
                   ?? throw new EnergyUaException(S.ErrNoCityPayload);
        using var doc = JsonDocument.Parse(json);
        return ReadStrings(doc.RootElement);
    }

    public static string? ExtractQueue(string html)
    {
        var m = QueueBadgeRx.Match(html);
        if (m.Success) return WebUtility.HtmlDecode(m.Groups["q"].Value).Trim();
        m = QueueTextRx.Match(html);
        return m.Success ? m.Value : null;
    }

    public static string? ExtractTitle(string html)
    {
        var m = H1Rx.Match(html);
        if (!m.Success) return null;
        var text = WebUtility.HtmlDecode(TagRx.Replace(m.Groups["t"].Value, " "));
        text = SpacesRx.Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Strips the site's "Графік відключення електроенергії" prefix, leaving "&lt;settlement&gt;, &lt;street&gt; &lt;house&gt;".</summary>
    public static string? CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var t = TitlePrefixRx.Replace(title, "").Trim();
        return t.Length == 0 ? title : t;
    }

    static List<RawInterval> ReadIntervals(JsonElement root, string name)
    {
        var list = new List<RawInterval>();
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var status = item.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                ? st.GetString()
                : null;
            list.Add(new RawInterval(ReadInt(item, "from"), ReadInt(item, "to"), MapStatus(status)));
        }
        return list;
    }

    static PowerStatus MapStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "red" => PowerStatus.Off,
        "yellow" => PowerStatus.Maybe,
        _ => PowerStatus.On,
    };

    static int ReadInt(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return (int)Math.Round(v.GetDouble());
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return (int)Math.Round(d);
        return 0;
    }

    static bool ReadBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>dates.today = "19.11.2026"; if missing — today's date in Kyiv.</summary>
    static DateOnly ReadTodayDate(JsonElement root)
    {
        if (root.TryGetProperty("dates", out var dates) &&
            dates.ValueKind == JsonValueKind.Object &&
            dates.TryGetProperty("today", out var t) &&
            t.ValueKind == JsonValueKind.String &&
            DateOnly.TryParseExact(t.GetString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return TimeZones.TodayKyiv();
    }

    static IReadOnlyList<string> ReadStrings(JsonElement arr)
    {
        if (arr.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var x in arr.EnumerateArray())
        {
            if (x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 } s) list.Add(s);
        }
        return list;
    }
}
