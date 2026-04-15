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
/// Pengujian kecepatan internet: Fast.com Speedtest dan Ookla Speedtest CLI.
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

        // Fast.com Speedtest
        total++;
        var fastResult = await RunFastSpeedtestAsync();

        try
        {
            await _api.SaveTestResultAsync(new FoTestResult
            {
                SessionId = _sessionId,
                TestType = TestTypes.SpeedtestFast,
                Target = "fast.com",
                ResultData = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    download_mbps = fastResult.downloadMbps,
                    upload_mbps = fastResult.uploadMbps,
                    error = fastResult.error,
                })),
                Status = fastResult.downloadMbps > 0 ? TestStatus.Pass : TestStatus.Fail,
            });
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to save Fast.com speedtest result"); }

        panel.Dispatcher.Invoke(() => panel.Children.Add(BuildSpeedCard(
            "Fast.com Speedtest", fastResult.downloadMbps, fastResult.uploadMbps, fastResult.downloadMbps > 0, fastResult.error)));

        if (fastResult.downloadMbps > 0) { pass++; }

        // Ookla Speedtest (via CLI, auto-download jika belum ada)
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
    /// Fast.com Speedtest: download dari server Netflix untuk ukur kecepatan.
    /// </summary>
    private async Task<(double downloadMbps, double uploadMbps, string? error)> RunFastSpeedtestAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

            // 1. Ambil token dari halaman Fast.com
            var html = await client.GetStringAsync("https://fast.com");
            var tokenMatch = System.Text.RegularExpressions.Regex.Match(html, @"/app-([a-f0-9]+)\.js");
            if (!tokenMatch.Success)
                return (0, 0, "Gagal mendapatkan script Fast.com");

            var scriptUrl = $"https://fast.com{tokenMatch.Value}";
            var script = await client.GetStringAsync(scriptUrl);
            var apiTokenMatch = System.Text.RegularExpressions.Regex.Match(script, @"token:\s*[""']([^""']+)[""']");
            if (!apiTokenMatch.Success)
                return (0, 0, "Gagal mendapatkan API token Fast.com");

            var token = apiTokenMatch.Groups[1].Value;

            // 2. Dapatkan URL download dari API Fast.com
            var apiUrl = $"https://api.fast.com/netflix/speedtest/v2?https=true&token={Uri.EscapeDataString(token)}&urlCount=5";
            var apiResponse = await client.GetStringAsync(apiUrl);
            var apiJson = JsonDocument.Parse(apiResponse);
            var targets = apiJson.RootElement.GetProperty("targets");

            if (targets.GetArrayLength() == 0)
                return (0, 0, "Tidak ada server Fast.com yang tersedia");

            // 3. Download paralel & berulang selama 10 detik
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            long totalDownloadBytes = 0;
            var downloadTasks = new List<Task>();

            foreach (var target in targets.EnumerateArray())
            {
                var url = target.GetProperty("url").GetString();
                if (url == null) continue;

                var downloadUrl = url.Contains('?') ? $"{url}&size=26214400" : $"{url}?size=26214400";
                
                // Tambahkan 2 koneksi per URL
                for (int i = 0; i < 2; i++)
                {
                    downloadTasks.Add(Task.Run(async () =>
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            var bytes = await DownloadAndCountBytesAsync(client, downloadUrl, cts.Token);
                            Interlocked.Add(ref totalDownloadBytes, bytes);
                        }
                    }));
                }
            }

            var dlSw = Stopwatch.StartNew();
            try { await Task.WhenAll(downloadTasks); } catch { }
            dlSw.Stop();

            var actualDlSeconds = dlSw.Elapsed.TotalSeconds;
            if (actualDlSeconds > 10) actualDlSeconds = 10;
            var downloadMbps = totalDownloadBytes * 8.0 / 1_000_000 / actualDlSeconds;

            // 4. Upload paralel (POST/PUT ke endpoint yang sama) selama 10 detik
            var uCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            long totalUploadBytes = 0;
            var uploadTasks = new List<Task>();
            byte[] uploadPayload = new byte[1024 * 1024]; // 1MB chunk

            // Isi array dengan random byte
            new Random().NextBytes(uploadPayload);

            foreach (var target in targets.EnumerateArray())
            {
                var url = target.GetProperty("url").GetString();
                if (url == null) continue;

                for (int i = 0; i < 2; i++)
                {
                    uploadTasks.Add(Task.Run(async () =>
                    {
                        while (!uCts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                using var content = new ByteArrayContent(uploadPayload);
                                using var response = await client.PostAsync(url, content, uCts.Token);
                                if (response.IsSuccessStatusCode)
                                {
                                    Interlocked.Add(ref totalUploadBytes, uploadPayload.Length);
                                }
                            }
                            catch { /* Abaikan error koneksi tertutup dll */ }
                        }
                    }));
                }
            }

            var ulSw = Stopwatch.StartNew();
            try { await Task.WhenAll(uploadTasks); } catch { }
            ulSw.Stop();

            var actualUlSeconds = ulSw.Elapsed.TotalSeconds;
            if (actualUlSeconds > 10) actualUlSeconds = 10;
            var uploadMbps = totalUploadBytes * 8.0 / 1_000_000 / actualUlSeconds;

            return (Math.Round(downloadMbps, 2), Math.Round(uploadMbps, 2), null);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Fast.com speedtest error");
            return (0, 0, ex.Message);
        }
    }

    /// <summary>Download URL dan hitung total bytes yang diterima.</summary>
    private static async Task<long> DownloadAndCountBytesAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                totalRead += bytesRead;
                if (ct.IsCancellationRequested) break;
            }
            return totalRead;
        }
        catch
        {
            return 0;
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

            Serilog.Log.Information("Menjalankan Ookla Speedtest CLI: {Path}", exePath);

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
                return (0, 0, $"Speedtest error (exit {process.ExitCode})");
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
    /// Pastikan speedtest CLI tersedia: cek folder tools, PATH, atau auto-download.
    /// </summary>
    private static async Task<string?> EnsureSpeedtestCliAsync()
    {
        // 1. Cek di folder tools (prioritas utama)
        var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
        var exePath = Path.Combine(toolsDir, "speedtest.exe");
        if (File.Exists(exePath))
            return exePath;

        // 2. Cek di PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "speedtest.exe",
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
                    return "speedtest.exe";
            }
        }
        catch { /* Tidak ada di PATH */ }

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
                Serilog.Log.Information("Speedtest CLI berhasil diunduh ke {Path}", exePath);
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
