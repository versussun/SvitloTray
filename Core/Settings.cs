using System.Text.Json;
using System.Text.Json.Serialization;

namespace SvitloTray.Core;

public enum SourceMode
{
    Address, // settlement / street / house
    Queue,   // queue directly, e.g. 2.1
    Link,    // any energy-ua.info link (works for other regions too)
}

public sealed class AppSettings
{
    public string BaseUrl { get; set; } = EnergyUaClient.DefaultBaseUrl;
    public SourceMode Mode { get; set; } = SourceMode.Address;

    // Defaults to the address from the original link.
    public string City { get; set; } = "Прилуки";
    public string Street { get; set; } = "Земська";
    public string House { get; set; } = "65";

    public string Queue { get; set; } = "2.1";
    public string Link { get; set; } = "";

    /// <summary>How many minutes before a state change to warn (0 — off).</summary>
    public int NotifyMinutesBefore { get; set; } = 15;
    public bool NotifyOnScheduleChange { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool AutoStart { get; set; }
    public bool ShowWidget { get; set; }

    /// <summary>Interface language.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.Ukrainian;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Site base URL and schedule page path.</summary>
    public (string BaseUrl, string Path) ResolveSource()
    {
        switch (Mode)
        {
            case SourceMode.Queue:
                if (!System.Text.RegularExpressions.Regex.IsMatch(Queue.Trim(), @"^\d+[.\-]\d+$"))
                    throw new EnergyUaException(S.ErrQueueFormat);
                return (BaseUrl, EnergyUaClient.QueuePath(Queue.Replace('-', '.')));

            case SourceMode.Link:
                if (!Uri.TryCreate(Link.Trim(), UriKind.Absolute, out var uri) ||
                    !uri.Host.EndsWith("energy-ua.info", StringComparison.OrdinalIgnoreCase) ||
                    !(uri.AbsolutePath.StartsWith("/grafik/", StringComparison.OrdinalIgnoreCase) ||
                      uri.AbsolutePath.StartsWith("/cherga/", StringComparison.OrdinalIgnoreCase)))
                    throw new EnergyUaException(S.ErrLinkFormat);
                return ($"{uri.Scheme}://{uri.Host}", uri.AbsolutePath);

            default:
                if (string.IsNullOrWhiteSpace(City) || string.IsNullOrWhiteSpace(Street) || string.IsNullOrWhiteSpace(House))
                    throw new EnergyUaException(S.ErrAddressEmpty);
                return (BaseUrl, EnergyUaClient.AddressPath(City, Street, House));
        }
    }

    [JsonIgnore]
    public string FallbackName => Mode switch
    {
        SourceMode.Queue => S.QueueName(Queue),
        SourceMode.Link => Uri.TryCreate(Link, UriKind.Absolute, out var u) ? Uri.UnescapeDataString(u.AbsolutePath) : Link,
        _ => $"{City}, {Street} {House}",
    };
}

/// <summary>Settings and the last loaded schedule in %AppData%\SvitloTray.</summary>
public sealed class SettingsStore
{
    // Order matters: static fields are initialized top to bottom,
    // so Instance is declared after Dir and Json.
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SvitloTray");
    static string SettingsPath => Path.Combine(Dir, "settings.json");
    static string CachePath => Path.Combine(Dir, "last-schedule.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SettingsStore Instance { get; } = new();

    SettingsStore()
    {
        Current = LoadSettings();
        Loc.Language = Current.Language;
    }

    public AppSettings Current { get; private set; }

    public void Save(AppSettings settings)
    {
        Current = settings;
        Loc.Language = settings.Language;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Json));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Json) ?? new AppSettings();
        }
        catch (Exception) { /* corrupted file — start with default settings */ }
        return new AppSettings();
    }

    public ScheduleSnapshot? LoadCachedSnapshot()
    {
        try
        {
            if (File.Exists(CachePath))
                return JsonSerializer.Deserialize<ScheduleSnapshot>(File.ReadAllText(CachePath), Json);
        }
        catch (Exception) { }
        return null;
    }

    public void SaveCachedSnapshot(ScheduleSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(snapshot, Json));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void ClearCachedSnapshot()
    {
        try { if (File.Exists(CachePath)) File.Delete(CachePath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
