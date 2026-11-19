using System.Net;

namespace SvitloTray.Core;

/// <summary>
/// Fallback way to load a page through the embedded browser (WebView2)
/// when Cloudflare blocks the plain HttpClient.
/// </summary>
public interface IBrowserFetcher
{
    Task<string?> GetHtmlAsync(string url, string expectedMarker, CancellationToken ct);
}

/// <summary>
/// energy-ua.info client. The site has no public API, so we fetch the HTML
/// and extract the JSON that the site embeds into the page itself.
///
///   /                                  — list of settlements
///   /select/{city}                     — streets
///   /select/{city}/{street}            — houses
///   /grafik/{city}/{street}/{house}    — schedule for an address
///   /cherga/{2-1}                      — schedule for queue 2.1
/// </summary>
public sealed class EnergyUaClient
{
    public const string DefaultBaseUrl = "https://chernigiv.energy-ua.info";

    readonly HttpClient _http;
    readonly Dictionary<string, IReadOnlyList<string>> _cityCache = new();

    public IBrowserFetcher? BrowserFallback { get; set; }

    public EnergyUaClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        var h = _http.DefaultRequestHeaders;
        h.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        h.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        h.TryAddWithoutValidation("Accept-Language", "uk-UA,uk;q=0.9,en;q=0.8");
    }

    // ---------- URLs ----------

    public static string AddressPath(string city, string street, string house) =>
        $"/grafik/{Esc(city)}/{Esc(street)}/{Esc(house)}";

    /// <summary>"2.1" → "/cherga/2-1".</summary>
    public static string QueuePath(string queue) =>
        "/cherga/" + queue.Trim().Replace('.', '-');

    public static string Combine(string baseUrl, string path) =>
        baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

    static string Esc(string s) => Uri.EscapeDataString(s.Trim());

    // ---------- Data ----------

    public async Task<ScheduleSnapshot> GetScheduleAsync(string baseUrl, string path, CancellationToken ct = default)
    {
        var url = Combine(baseUrl, path);
        var html = await GetHtmlAsync(url, "schedulePayload", ct).ConfigureAwait(false);
        return EnergyUaParser.ParseSchedulePage(html, url);
    }

    public async Task<IReadOnlyList<string>> GetCitiesAsync(string baseUrl, CancellationToken ct = default)
    {
        if (_cityCache.TryGetValue(baseUrl, out var cached)) return cached;
        var html = await GetHtmlAsync(Combine(baseUrl, "/"), "homeCityPayload", ct).ConfigureAwait(false);
        var list = EnergyUaParser.ParseCities(html);
        _cityCache[baseUrl] = list;
        return list;
    }

    public async Task<IReadOnlyList<string>> GetStreetsAsync(string baseUrl, string city, CancellationToken ct = default)
    {
        var html = await GetHtmlAsync(Combine(baseUrl, "/select/" + Esc(city)), "selectPagePayload", ct).ConfigureAwait(false);
        return EnergyUaParser.ParseSelectOptions(html);
    }

    public async Task<IReadOnlyList<string>> GetHousesAsync(string baseUrl, string city, string street, CancellationToken ct = default)
    {
        var html = await GetHtmlAsync(Combine(baseUrl, $"/select/{Esc(city)}/{Esc(street)}"), "selectPagePayload", ct).ConfigureAwait(false);
        return EnergyUaParser.ParseSelectOptions(html);
    }

    /// <summary>
    /// A plain HTTP request first; if the site answered with an error or a Cloudflare
    /// stub and the response lacks the expected data, try the embedded browser.
    /// </summary>
    public async Task<string> GetHtmlAsync(string url, string expectedMarker, CancellationToken ct = default)
    {
        Exception? httpError;
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound) throw new EnergyUaNotFoundException(url);

            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode && html.Contains(expectedMarker, StringComparison.Ordinal))
                return html;

            httpError = new EnergyUaException(S.ErrNoData((int)resp.StatusCode));
        }
        catch (EnergyUaNotFoundException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { httpError = ex; }

        if (BrowserFallback is { } browser)
        {
            try
            {
                var html = await browser.GetHtmlAsync(url, expectedMarker, ct).ConfigureAwait(false);
                if (html is not null && html.Contains(expectedMarker, StringComparison.Ordinal))
                    return html;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* keep the HTTP error */ }
        }

        throw httpError as EnergyUaException
              ?? new EnergyUaException(S.ErrLoadFailed(httpError.Message), httpError);
    }
}
