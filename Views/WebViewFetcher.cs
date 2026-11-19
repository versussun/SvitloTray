using System.Text;
using Microsoft.Maui.ApplicationModel;
using SvitloTray.Core;

namespace SvitloTray.Views;

/// <summary>
/// Fallback path: opens the page in an invisible WebView (WebView2 = Edge),
/// which passes the Cloudflare JS check by itself, and extracts the needed JSON blocks from the DOM.
/// </summary>
public sealed class WebViewFetcher : IBrowserFetcher
{
    // Collect only the needed parts of the page into a mini HTML and return it as base64,
    // so there is no dependency on string escaping when returning from JS.
    const string ExtractScript =
        "(function(){try{" +
        "var ids=['schedulePayload','selectPagePayload','homeCityPayload'];var out='';" +
        "for(var i=0;i<ids.length;i++){var e=document.getElementById(ids[i]);" +
        "if(e){out+='<script id=\"'+ids[i]+'\" type=\"application/json\">'+e.textContent+'</scr'+'ipt>';}}" +
        "var q=document.querySelector('.queue-badge');if(q){out+='<strong class=\"queue-badge\">'+q.textContent+'</strong>';}" +
        "var h=document.querySelector('h1');if(h){out+='<h1>'+h.textContent+'</h1>';}" +
        "return out?btoa(unescape(encodeURIComponent(out))):'';" +
        "}catch(err){return '';}})()";

    readonly WebView _webView;
    readonly SemaphoreSlim _lock = new(1, 1);

    public WebViewFetcher(WebView webView) => _webView = webView;

    public async Task<string?> GetHtmlAsync(string url, string expectedMarker, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(() => LoadAsync(url, expectedMarker, ct)).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    async Task<string?> LoadAsync(string url, string expectedMarker, CancellationToken ct)
    {
        var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNavigated(object? sender, WebNavigatedEventArgs e) => navigated.TrySetResult(e.Result == WebNavigationResult.Success);

        _webView.Navigated += OnNavigated;
        try
        {
            _webView.Source = new UrlWebViewSource { Url = url };
            await Task.WhenAny(navigated.Task, Task.Delay(TimeSpan.FromSeconds(30), ct));
        }
        finally
        {
            _webView.Navigated -= OnNavigated;
        }

        // Cloudflare may show a check first and reload the page — wait for the data.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var html = Decode(await _webView.EvaluateJavaScriptAsync(ExtractScript));
            if (html is not null && html.Contains(expectedMarker, StringComparison.Ordinal)) return html;
            await Task.Delay(1000, ct);
        }
        return null;
    }

    static string? Decode(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;
        var b64 = result.Trim().Trim('"').Replace("\\", "");
        if (b64.Length == 0 || b64 == "null") return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch (FormatException) { return null; }
    }
}
