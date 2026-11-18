using System.Diagnostics;
using System.Net.Http;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using Serilog;

namespace FoTestingApp.Services;

/// <summary>
/// Application tests: Browsing (how long a page takes to answer), Streaming (the sites answer, and a
/// video-sized download is fast enough) and Social Media (the sites answer).
///
/// Requests carry a browser User-Agent (AppTest:UserAgent): many sites refuse a client without one,
/// which would read as the customer's internet failing. Each page is fetched on a fresh connection,
/// so DNS, TCP and TLS are part of every measurement and a retry is not flattered by a warm one.
/// </summary>
public class AppTestService
{
    /// <summary>A video-sized download from a CDN, read for a few seconds to estimate throughput.</summary>
    private const string StreamingProbeUrl = "https://speed.cloudflare.com/__down?bytes=25000000";

    /// <summary>A steady 1080p stream needs about this much.</summary>
    private const double StreamingMinMbps = 5.0;

    public async Task RunAllAsync(TestRun run, System.Windows.Controls.StackPanel panel)
    {
        using var client = CreateClient();

        // Browsing: every page within the threshold.
        run.Cancellation.ThrowIfCancellationRequested();
        var browsingUrls = ConfigManager.GetBrowsingUrls();
        var thresholdSec = ConfigManager.GetBrowsingThresholdSeconds();
        var loadTimes = new Dictionary<string, double?>();
        var errors = new Dictionary<string, string>();
        foreach (var url in browsingUrls)
        {
            var (seconds, error) = await MeasureLoadTimeAsync(url, thresholdSec, run.Cancellation);
            loadTimes[url] = seconds is null ? null : Math.Round(seconds.Value, 2);
            if (error is not null)
            {
                errors[url] = error;
            }
        }

        var browsingPass = browsingUrls.Length > 0 && loadTimes.Values.All(t => t is not null && t <= thresholdSec);
        await run.RecordAsync(TestTypes.BrowsingTest, string.Join(", ", browsingUrls),
            new { results = loadTimes, errors, threshold_sec = thresholdSec, error = browsingUrls.Length == 0 ? "Daftar URL kosong." : null }, browsingPass);
        AddCard(panel, "Browsing", browsingPass, loadTimes.Select(t => (t.Key, t.Value is { } s && s <= thresholdSec,
            t.Value is { } v ? $"{v:0.0} detik" : errors.GetValueOrDefault(t.Key, "gagal"))));

        // Streaming: the sites answer, and the line carries a 1080p stream.
        run.Cancellation.ThrowIfCancellationRequested();
        var streamingUrls = ConfigManager.GetStreamingUrls();
        var streaming = await CheckAccessibilityAsync(client, streamingUrls, ConfigManager.GetStreamingThresholdSeconds(), run.Cancellation);
        var throughput = await MeasureStreamingThroughputAsync(client, run.Cancellation);
        streaming["Video_Throughput"] = throughput.Text;
        var streamingPass = streamingUrls.Length > 0 && streaming.Values.All(IsOk) && throughput.Ok;
        await run.RecordAsync(TestTypes.StreamingTest, string.Join(", ", streamingUrls), new { results = streaming }, streamingPass);
        AddCard(panel, "Streaming", streamingPass, streaming.Select(s => (s.Key, IsOk(s.Value), s.Value)));

        // Social Media: the sites answer.
        run.Cancellation.ThrowIfCancellationRequested();
        var socialUrls = ConfigManager.GetSocialMediaUrls();
        var social = await CheckAccessibilityAsync(client, socialUrls, ConfigManager.GetSocialMediaThresholdSeconds(), run.Cancellation);
        var socialPass = socialUrls.Length > 0 && social.Values.All(IsOk);
        await run.RecordAsync(TestTypes.SocialMediaTest, string.Join(", ", socialUrls), new { results = social }, socialPass);
        AddCard(panel, "Social Media", socialPass, social.Select(s => (s.Key, IsOk(s.Value), s.Value)));
    }

    private static bool IsOk(string value) => value.StartsWith("Loaded") || value.StartsWith("Lancar");

    private static HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, PooledConnectionLifetime = TimeSpan.Zero })
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(60),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ConfigManager.GetAppTestUserAgent());
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,*/*;q=0.8");
        return client;
    }

    /// <summary>
    /// Seconds until the page's document has arrived (redirects included), on a fresh connection.
    /// Waits past the threshold (up to three times it) so a slow page shows its real time instead of
    /// "error"; null with the reason when it does not load at all.
    /// </summary>
    private static async Task<(double? Seconds, string? Error)> MeasureLoadTimeAsync(string url, int thresholdSec, CancellationToken ct)
    {
        using var client = CreateClient(TimeSpan.FromSeconds(Math.Max(thresholdSec * 3, 30)));
        try
        {
            var sw = Stopwatch.StartNew();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
            sw.Stop();
            return (int)response.StatusCode < 500
                ? (sw.Elapsed.TotalSeconds, null)
                : (null, $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return (null, "timeout");
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "Browsing test could not reach {Url}", url);
            return (null, ex.HttpRequestError.ToString());
        }
    }

    /// <summary>
    /// Whether each site answers within the threshold. Any answer below 500 means the site is
    /// reachable from this line — a 403 from bot protection is the site answering, not the internet
    /// failing — and the status code is kept with it.
    /// </summary>
    private static async Task<Dictionary<string, string>> CheckAccessibilityAsync(HttpClient client, string[] urls, int timeoutSec, CancellationToken ct)
    {
        var results = new Dictionary<string, string>();
        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSec)));
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var code = (int)response.StatusCode;
                results[url] = code < 500 ? $"Loaded (HTTP {code})" : $"Failed (HTTP {code})";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                results[url] = $"Failed (timeout {timeoutSec} detik)";
            }
            catch (HttpRequestException ex)
            {
                results[url] = $"Failed ({ex.HttpRequestError})";
                Log.Warning(ex, "App test could not reach {Url}", url);
            }
        }

        return results;
    }

    /// <summary>Reads a large download for 5 seconds after its first byte; enough for 1080p is OK.</summary>
    private static async Task<(bool Ok, string Text)> MeasureStreamingThroughputAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(StreamingProbeUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Failed (CDN video HTTP {(int)response.StatusCode})");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
            window.CancelAfter(TimeSpan.FromSeconds(5));
            var buffer = new byte[81920];
            long total = 0;
            var sw = Stopwatch.StartNew();
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer, window.Token)) > 0)
                {
                    total += read;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The 5-second window closed: the measurement is what arrived in it.
            }
            sw.Stop();

            var mbps = total * 8.0 / 1_000_000 / Math.Max(sw.Elapsed.TotalSeconds, 0.1);
            return mbps >= StreamingMinMbps
                ? (true, $"Lancar untuk 1080p ({mbps:0.0} Mbps)")
                : (false, $"Failed (terlalu lambat: {mbps:0.0} Mbps)");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Streaming throughput probe failed");
            return (false, "Failed (koneksi ke CDN video gagal)");
        }
    }

    private static void AddCard(System.Windows.Controls.StackPanel panel, string label, bool passed, IEnumerable<(string Key, bool Ok, string Value)> items)
    {
        var failed = items.Where(i => !i.Ok).Select(i => $"{Host(i.Key)}: {i.Value}").ToList();
        var description = passed ? "Semua akses berhasil" : failed.Count > 0 ? $"Gagal di: {string.Join(", ", failed)}" : "Tidak ada target yang diuji";
        panel.Dispatcher.Invoke(() => panel.Children.Add(NetworkTestService.BuildCard(label, description, passed, MaterialDesignThemes.Wpf.PackIconKind.Web)));
    }

    private static string Host(string key) =>
        Uri.TryCreate(key, UriKind.Absolute, out var uri) ? uri.Host : key.Replace('_', ' ');
}
