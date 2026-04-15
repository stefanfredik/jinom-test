using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using Serilog;

namespace FoTestingApp.Services;

/// <summary>
/// Pengujian aplikasi: Browsing (load time), Streaming, Social Media (aksesibilitas HTTP).
/// </summary>
public class AppTestService
{
    private readonly ApiService _api;
    private readonly int _sessionId;
    private static readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public AppTestService(ApiService api, int sessionId)
    {
        _api = api;
        _sessionId = sessionId;
    }

    public async Task<(int passCount, int totalCount)> RunAllAsync(StackPanel panel, IProgress<(int percent, string status)> progress)
    {
        var pass = 0;
        var total = 0;

        // Browsing
        total++;
        var browsingUrls = ConfigManager.GetBrowsingUrls();
        var browsingResults = new Dictionary<string, double>();
        var thresholdSec = ConfigManager.GetBrowsingThresholdSeconds();

        foreach (var url in browsingUrls)
        {
            var loadTime = await MeasureLoadTimeAsync(url);
            browsingResults[url] = loadTime;
        }

        var browsingPass = browsingResults.Values.All(t => t <= thresholdSec && t >= 0);
        if (browsingPass) { pass++; }

        try
        {
            await _api.SaveTestResultAsync(new FoTestResult
            {
                SessionId = _sessionId,
                TestType = TestTypes.BrowsingTest,
                Target = string.Join(", ", browsingUrls),
                ResultData = JsonDocument.Parse(JsonSerializer.Serialize(new { results = browsingResults, threshold_sec = thresholdSec })),
                Status = browsingPass ? TestStatus.Pass : TestStatus.Fail,
            });
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to save browsing result"); }

        panel.Dispatcher.Invoke(() => panel.Children.Add(BuildHttpCard("Browsing", browsingResults.ToDictionary(
            k => k.Key, v => v.Value >= 0 ? $"{v.Value:F1}s" : "Error"), browsingPass)));

        // Streaming
        total++;
        var streamingUrls = ConfigManager.GetStreamingUrls();
        var streamingResults = await SimulateStreamingAsync(streamingUrls);
        var streamingPass = streamingResults.Values.All(v => !v.StartsWith("Failed") && !v.StartsWith("Error"));
        if (streamingPass) { pass++; }

        try
        {
            await _api.SaveTestResultAsync(new FoTestResult
            {
                SessionId = _sessionId,
                TestType = TestTypes.StreamingTest,
                Target = string.Join(", ", streamingUrls),
                ResultData = JsonDocument.Parse(JsonSerializer.Serialize(new { results = streamingResults })),
                Status = streamingPass ? TestStatus.Pass : TestStatus.Fail,
            });
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to save streaming result"); }

        panel.Dispatcher.Invoke(() => panel.Children.Add(BuildHttpCard("Streaming", streamingResults, streamingPass)));

        // Social Media
        total++;
        var socialUrls = ConfigManager.GetSocialMediaUrls();
        var socialResults = await CheckAccessibilityAsync(socialUrls);
        var socialPass = socialResults.Values.All(v => v == "Loaded");
        if (socialPass) { pass++; }

        try
        {
            await _api.SaveTestResultAsync(new FoTestResult
            {
                SessionId = _sessionId,
                TestType = TestTypes.SocialMediaTest,
                Target = string.Join(", ", socialUrls),
                ResultData = JsonDocument.Parse(JsonSerializer.Serialize(new { results = socialResults })),
                Status = socialPass ? TestStatus.Pass : TestStatus.Fail,
            });
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to save social media result"); }

        panel.Dispatcher.Invoke(() => panel.Children.Add(BuildHttpCard("Social Media", socialResults, socialPass)));

        return (pass, total);
    }

    private async Task<double> MeasureLoadTimeAsync(string url)
    {
        try
        {
            var start = DateTime.UtcNow;
            var response = await _client.GetAsync(url);
            var elapsed = (DateTime.UtcNow - start).TotalSeconds;
            return response.IsSuccessStatusCode ? elapsed : -1;
        }
        catch
        {
            return -1;
        }
    }

    private async Task<Dictionary<string, string>> SimulateStreamingAsync(string[] urls)
    {
        var results = new Dictionary<string, string>();
        
        // 1. Pastikan situs utamanya bisa diakses terlebih dahulu
        foreach (var url in urls)
        {
            try
            {
                var response = await _client.GetAsync(url);
                results[url] = response.IsSuccessStatusCode ? "Akses Web OK" : "Failed";
            }
            catch
            {
                results[url] = "Failed (Timeout)";
            }
        }

        // 2. Simulasi Throughput Streaming dengan CDN Dummy (Cloudflare 25MB chunk)
        var dummyUrl = "https://speed.cloudflare.com/__down?bytes=25000000";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // Batasi 5 detik
            using var response = await _client.GetAsync(dummyUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                var buffer = new byte[81920];
                long totalBytesRead = 0;
                var start = DateTime.UtcNow;
                
                try 
                {
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                    {
                        totalBytesRead += bytesRead;
                    }
                }
                catch (OperationCanceledException) { /* Diabaikan, waktu 5 detik habis (normal) */ }

                var duration = (DateTime.UtcNow - start).TotalSeconds;
                if (duration <= 0) duration = 0.1;

                var mbps = (totalBytesRead * 8.0) / (1_000_000.0) / duration;
                
                // Standar 1080p butuh setidaknya ~5 Mbps stabil
                if (mbps >= 5.0)
                {
                    results["Video_Throughput"] = $"{mbps:F1} Mbps (Lancar untuk 1080p)";
                }
                else
                {
                    results["Video_Throughput"] = $"Failed (Terlalu lambat: {mbps:F1} Mbps)";
                }
            }
            else
            {
                results["Video_Throughput"] = "Failed (Server CDN lambat/Mati)";
            }
        }
        catch (Exception)
        {
            results["Video_Throughput"] = "Failed (Koneksi ke CDN Video gagal)";
        }

        return results;
    }

    private async Task<Dictionary<string, string>> CheckAccessibilityAsync(string[] urls)
    {
        var results = new Dictionary<string, string>();
        foreach (var url in urls)
        {
            try
            {
                var response = await _client.GetAsync(url);
                results[url] = response.IsSuccessStatusCode ? "Loaded" : "Failed";
            }
            catch
            {
                results[url] = "Failed";
            }
        }

        return results;
    }

    private static Border BuildHttpCard(string label, Dictionary<string, string> results, bool pass)
    {
        var primaryColor = pass ? (SolidColorBrush)new BrushConverter().ConvertFromString("#22C55E")! : (SolidColorBrush)new BrushConverter().ConvertFromString("#EF4444")!;
        var bgColor = pass ? (SolidColorBrush)new BrushConverter().ConvertFromString("#1A22C55E")! : (SolidColorBrush)new BrushConverter().ConvertFromString("#1AEF4444")!;
        var iconKind = pass ? MaterialDesignThemes.Wpf.PackIconKind.CheckCircle : MaterialDesignThemes.Wpf.PackIconKind.AlertCircle;
        var statusText = pass ? "COMPLETED" : "FAILED";
        
        var failedUrls = results.Where(r => r.Value == "Failed" || r.Value.StartsWith("Error")).Select(r => r.Key).ToList();
        var subtitle = failedUrls.Count > 0 ? $"Gagal di: {string.Join(", ", failedUrls)}" : "Semua akses berhasil";

        return FoTestingApp.Helpers.UIHelper.CreateLogCard(label, subtitle, primaryColor, bgColor, iconKind, statusText);
    }
}
