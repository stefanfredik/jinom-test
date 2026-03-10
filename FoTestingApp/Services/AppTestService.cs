using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FoTestingApp.Helpers;
using FoTestingApp.Models;

namespace FoTestingApp.Services;

/// <summary>
/// Pengujian aplikasi: Browsing (load time), Streaming, Social Media (aksesibilitas HTTP).
/// </summary>
public class AppTestService
{
    private readonly DatabaseService _db;
    private readonly int _sessionId;
    private static readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public AppTestService(DatabaseService db, int sessionId)
    {
        _db = db;
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

        await _db.SaveTestResultAsync(new FoTestResult
        {
            SessionId = _sessionId,
            TestType = TestTypes.BrowsingTest,
            Target = string.Join(", ", browsingUrls),
            ResultData = JsonDocument.Parse(JsonSerializer.Serialize(new { results = browsingResults, threshold_sec = thresholdSec })),
            Status = browsingPass ? TestStatus.Pass : TestStatus.Fail,
        });

        panel.Dispatcher.Invoke(() => panel.Children.Add(BuildHttpCard("Browsing", browsingResults.ToDictionary(
            k => k.Key, v => v.Value >= 0 ? $"{v.Value:F1}s" : "Error"), browsingPass)));

        // Streaming
        total++;
        var streamingUrls = ConfigManager.GetStreamingUrls();
        var streamingResults = await CheckAccessibilityAsync(streamingUrls);
        var streamingPass = streamingResults.Values.All(v => v == "Loaded");
        if (streamingPass) { pass++; }

        await _db.SaveTestResultAsync(new FoTestResult
        {
            SessionId = _sessionId,
            TestType = TestTypes.StreamingTest,
            Target = string.Join(", ", streamingUrls),
            ResultData = JsonDocument.Parse(JsonSerializer.Serialize(new { results = streamingResults })),
            Status = streamingPass ? TestStatus.Pass : TestStatus.Fail,
        });

        panel.Dispatcher.Invoke(() => panel.Children.Add(BuildHttpCard("Streaming", streamingResults, streamingPass)));

        // Social Media
        total++;
        var socialUrls = ConfigManager.GetSocialMediaUrls();
        var socialResults = await CheckAccessibilityAsync(socialUrls);
        var socialPass = socialResults.Values.All(v => v == "Loaded");
        if (socialPass) { pass++; }

        await _db.SaveTestResultAsync(new FoTestResult
        {
            SessionId = _sessionId,
            TestType = TestTypes.SocialMediaTest,
            Target = string.Join(", ", socialUrls),
            ResultData = JsonDocument.Parse(JsonSerializer.Serialize(new { results = socialResults })),
            Status = socialPass ? TestStatus.Pass : TestStatus.Fail,
        });

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
        var icon = pass ? "✅" : "❌";
        var color = pass ? Brushes.Green : Brushes.Red;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = $"{icon} {label}", FontWeight = FontWeights.SemiBold, Foreground = color });
        foreach (var (url, result) in results)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"  • {url}: {result}",
                FontSize = 12,
                Foreground = result is "Loaded" || !result.StartsWith("Error") ? Brushes.DimGray : Brushes.Red,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        return new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(16, 12, 16, 12),
            Child = panel,
        };
    }
}
