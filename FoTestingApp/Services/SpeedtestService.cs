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
    /// Ookla Speedtest via CLI (otomatis download jika belum ada).
    /// </summary>
    private async Task<(double downloadMbps, double uploadMbps, string? error)> RunOoklaSpeedtestAsync()
    {
        try
        {
            var exePath = await EnsureSpeedtestCliAsync();
            if (exePath == null)
                return (0, 0, "Speedtest CLI tidak ditemukan dan gagal diunduh otomatis");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--format=json --accept-license --accept-gdpr --progress=no",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null) return (0, 0, "Gagal menjalankan Speedtest CLI");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // Timeout 120 detik
            var exited = await Task.Run(() => process.WaitForExit(120_000));
            if (!exited)
            {
                try { process.Kill(true); } catch { }
                return (0, 0, "Speedtest timeout (120 detik)");
            }

            var output = await outputTask;
            var stderr = await errorTask;

            if (process.ExitCode != 0)
            {
                Serilog.Log.Warning("Speedtest CLI exit code {Code}: {Stderr}", process.ExitCode, stderr);
                return (0, 0, $"Speedtest error: {stderr.Trim()}");
            }

            if (string.IsNullOrWhiteSpace(output))
                return (0, 0, "Speedtest CLI tidak menghasilkan output");

            var json = JsonDocument.Parse(output);
            var root = json.RootElement;

            var download = root.GetProperty("download").GetProperty("bandwidth").GetDouble() * 8 / 1_000_000;
            var upload = root.GetProperty("upload").GetProperty("bandwidth").GetDouble() * 8 / 1_000_000;

            return (Math.Round(download, 2), Math.Round(upload, 2), null);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Ookla speedtest error");
            return (0, 0, ex.Message);
        }
    }

    /// <summary>
    /// Pastikan speedtest CLI tersedia: cek PATH, folder tools, atau auto-download.
    /// </summary>
    private static async Task<string?> EnsureSpeedtestCliAsync()
    {
        // 1. Cek di PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "speedtest",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                p.WaitForExit(5000);
                if (p.ExitCode == 0)
                    return "speedtest";
            }
        }
        catch { /* Tidak ada di PATH */ }

        // 2. Cek di folder tools
        var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
        var exePath = Path.Combine(toolsDir, "speedtest.exe");
        if (File.Exists(exePath))
            return exePath;

        // 3. Auto-download dari Ookla
        try
        {
            Serilog.Log.Information("Mengunduh Ookla Speedtest CLI...");
            Directory.CreateDirectory(toolsDir);

            var downloadUrl = ConfigManager.GetOoklaCliUrl();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var zipBytes = await client.GetByteArrayAsync(downloadUrl);

            var zipPath = Path.Combine(toolsDir, "speedtest.zip");
            await File.WriteAllBytesAsync(zipPath, zipBytes);

            ZipFile.ExtractToDirectory(zipPath, toolsDir, overwriteFiles: true);
            File.Delete(zipPath);

            if (File.Exists(exePath))
            {
                Serilog.Log.Information("Speedtest CLI berhasil diunduh");
                return exePath;
            }

            Serilog.Log.Warning("speedtest.exe tidak ditemukan setelah ekstrak zip");
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Gagal mengunduh Speedtest CLI");
        }

        return null;
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
