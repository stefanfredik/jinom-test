using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;
using FoTestingApp.Helpers;
using FoTestingApp.Models;

namespace FoTestingApp.Services;

/// <summary>
/// Pengujian kecepatan internet: Jinom Speedtest (custom) dan Ookla Speedtest CLI.
/// </summary>
public class SpeedtestService
{
    private readonly ApiService _api;
    private readonly int _sessionId;
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(60) };

    public SpeedtestService(ApiService api, int sessionId)
    {
        _api = api;
        _sessionId = sessionId;
    }

    public async Task<(int passCount, int totalCount)> RunAllAsync(StackPanel panel, int packageMbps)
    {
        var pass = 0;
        var total = 0;

        // Jinom Speedtest
        total++;
        var jinomResult = await RunJinomSpeedtestAsync(packageMbps);
        if (jinomResult.pass) { pass++; }

        try
        {
            await _api.SaveTestResultAsync(new FoTestResult
            {
                SessionId = _sessionId,
                TestType = TestTypes.SpeedtestJinom,
                Target = ConfigManager.GetJinomSpeedtestServer(),
                ResultData = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    download_mbps = jinomResult.downloadMbps,
                    upload_mbps = jinomResult.uploadMbps,
                    threshold_pct = ConfigManager.GetSpeedtestThresholdPercentage(),
                    package_mbps = packageMbps,
                })),
                Status = jinomResult.pass ? TestStatus.Pass : TestStatus.Fail,
            });
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to save Jinom speedtest result"); }

        panel.Dispatcher.Invoke(() => panel.Children.Add(BuildSpeedCard(
            "Jinom Speedtest", jinomResult.downloadMbps, jinomResult.uploadMbps, jinomResult.pass)));

        // Ookla Speedtest (via CLI jika tersedia)
        total++;
        var ooklaResult = await RunOoklaSpeedtestAsync();

        try
        {
            await _api.SaveTestResultAsync(new FoTestResult
            {
                SessionId = _sessionId,
                TestType = TestTypes.SpeedtestOokla,
                Target = "speedtest.net",
                ResultData = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    download_mbps = ooklaResult.downloadMbps,
                    upload_mbps = ooklaResult.uploadMbps,
                    error = ooklaResult.error,
                })),
                Status = ooklaResult.downloadMbps > 0 ? TestStatus.Pass : TestStatus.Fail,
            });
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to save Ookla speedtest result"); }

        panel.Dispatcher.Invoke(() => panel.Children.Add(BuildSpeedCard(
            "Ookla Speedtest", ooklaResult.downloadMbps, ooklaResult.uploadMbps,
            ooklaResult.downloadMbps > 0, ooklaResult.error)));

        if (ooklaResult.downloadMbps > 0) { pass++; }

        return (pass, total);
    }

    /// <summary>
    /// Jinom Speedtest: download file besar dari server Jinom, ukur throughput.
    /// </summary>
    private async Task<(double downloadMbps, double uploadMbps, bool pass)> RunJinomSpeedtestAsync(int packageMbps)
    {
        try
        {
            var server = ConfigManager.GetJinomSpeedtestServer();
            var threshold = ConfigManager.GetSpeedtestThresholdPercentage();

            // Download test: ukur throughput download 10MB file
            var stopwatch = Stopwatch.StartNew();
            var data = await _client.GetByteArrayAsync($"{server}/download?size=10485760");
            stopwatch.Stop();

            var downloadMbps = data.Length * 8.0 / 1_000_000 / stopwatch.Elapsed.TotalSeconds;

            // Upload test: POST 5MB data
            stopwatch.Restart();
            var uploadData = new byte[5 * 1024 * 1024];
            var content = new ByteArrayContent(uploadData);
            await _client.PostAsync($"{server}/upload", content);
            stopwatch.Stop();

            var uploadMbps = uploadData.Length * 8.0 / 1_000_000 / stopwatch.Elapsed.TotalSeconds;

            var minRequired = packageMbps * threshold / 100.0;
            var pass = downloadMbps >= minRequired;

            return (Math.Round(downloadMbps, 2), Math.Round(uploadMbps, 2), pass);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Jinom speedtest error");
            return (0, 0, false);
        }
    }

    /// <summary>
    /// Ookla Speedtest via CLI (speedtest.exe harus ada di PATH atau folder app).
    /// </summary>
    private async Task<(double downloadMbps, double uploadMbps, string? error)> RunOoklaSpeedtestAsync()
    {
        var exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "speedtest.exe");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "speedtest",
                Arguments = "--format=json --accept-license --accept-gdpr",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null) { return (0, 0, "Speedtest CLI tidak ditemukan"); }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var json = JsonDocument.Parse(output);
            var download = json.RootElement.GetProperty("download").GetProperty("bandwidth").GetDouble() * 8 / 1_000_000;
            var upload = json.RootElement.GetProperty("upload").GetProperty("bandwidth").GetDouble() * 8 / 1_000_000;

            return (Math.Round(download, 2), Math.Round(upload, 2), null);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Ookla speedtest error");
            return (0, 0, ex.Message);
        }
    }

    private static Border BuildSpeedCard(string label, double download, double upload, bool pass, string? error = null)
    {
        var primaryColor = pass ? (SolidColorBrush)new BrushConverter().ConvertFromString("#22C55E")! : (SolidColorBrush)new BrushConverter().ConvertFromString("#EF4444")!;
        var bgColor = pass ? (SolidColorBrush)new BrushConverter().ConvertFromString("#1A22C55E")! : (SolidColorBrush)new BrushConverter().ConvertFromString("#1AEF4444")!;
        var iconKind = pass ? MaterialDesignThemes.Wpf.PackIconKind.Speedometer : MaterialDesignThemes.Wpf.PackIconKind.SpeedometerSlow;
        var statusText = pass ? "COMPLETED" : "FAILED";
        
        var subtitle = error ?? $"↓ {download} Mbps   ↑ {upload} Mbps";

        return FoTestingApp.Helpers.UIHelper.CreateLogCard(label, subtitle, primaryColor, bgColor, iconKind, statusText);
    }
}
