using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services.Core;
using Serilog;

namespace FoTestingApp.Services;

/// <summary>
/// Speedtests: Fast.com (Netflix's servers) and Ookla's CLI. Each passes when its download reaches
/// Speedtest:ThresholdPercentage % of the customer's package (SpeedVerdict); the result keeps the
/// package and the speed it needed, so the report shows why.
///
/// SRS FR-040 asks for the Jinom speedtest server; until it is up and its protocol is known, Fast.com
/// stands in for it.
/// </summary>
public class SpeedtestService
{
    private const int MeasureSeconds = 10;

    public async Task RunAllAsync(TestRun run, System.Windows.Controls.StackPanel panel, int packageMbps)
    {
        var thresholdPct = ConfigManager.GetSpeedtestThresholdPercentage();

        run.Cancellation.ThrowIfCancellationRequested();
        var fast = await RunFastSpeedtestAsync(run.Cancellation);
        await RecordAsync(run, panel, TestTypes.SpeedtestFast, "fast.com", fast, packageMbps, thresholdPct);

        run.Cancellation.ThrowIfCancellationRequested();
        var ookla = await RunOoklaSpeedtestAsync(run.Cancellation);
        await RecordAsync(run, panel, TestTypes.SpeedtestOokla, "speedtest.net", ookla, packageMbps, thresholdPct);
    }

    private sealed record SpeedResult(double DownloadMbps, double UploadMbps, string? Error, double? PingMs = null, string? Server = null);

    private static async Task RecordAsync(TestRun run, System.Windows.Controls.StackPanel panel, string testType, string target, SpeedResult result, int packageMbps, int thresholdPct)
    {
        var required = SpeedVerdict.RequiredMbps(packageMbps, thresholdPct);
        var passed = result.Error is null && SpeedVerdict.Passes(result.DownloadMbps, packageMbps, thresholdPct);
        var error = result.Error
            ?? (!passed && required is not null ? $"Download {result.DownloadMbps:0.0} Mbps di bawah {required:0.0} Mbps ({thresholdPct}% dari paket {packageMbps} Mbps)" : null);

        await run.RecordAsync(testType, target, new
        {
            download_mbps = result.DownloadMbps,
            upload_mbps = result.UploadMbps,
            package_mbps = packageMbps > 0 ? packageMbps : (int?)null,
            threshold_pct = thresholdPct,
            required_mbps = required is null ? (double?)null : Math.Round(required.Value, 1),
            ping_ms = result.PingMs,
            server = result.Server,
            error,
        }, passed);

        var description = result.Error ?? $"↓ {result.DownloadMbps:0.0} Mbps   ↑ {result.UploadMbps:0.0} Mbps"
            + (required is not null ? $"   (minimal {required:0.0} Mbps)" : string.Empty);
        panel.Dispatcher.Invoke(() => panel.Children.Add(NetworkTestService.BuildCard(
            ResultFormatter.TestName(testType), description, passed, MaterialDesignThemes.Wpf.PackIconKind.Speedometer)));
    }

    // ── Fast.com ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fast.com: the page's script carries the API token; the API names Netflix servers to download
    /// from. Download and upload each run for 10 seconds over two connections per server.
    /// </summary>
    private static async Task<SpeedResult> RunFastSpeedtestAsync(CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ConfigManager.GetAppTestUserAgent());

            var html = await client.GetStringAsync("https://fast.com", ct);
            var script = Regex.Match(html, @"/app-([a-f0-9]+)\.js");
            if (!script.Success)
            {
                return new SpeedResult(0, 0, "Gagal membaca halaman Fast.com");
            }

            var js = await client.GetStringAsync($"https://fast.com{script.Value}", ct);
            var token = Regex.Match(js, @"token:\s*[""']([^""']+)[""']");
            if (!token.Success)
            {
                return new SpeedResult(0, 0, "Gagal membaca token Fast.com");
            }

            var api = await client.GetStringAsync(
                $"https://api.fast.com/netflix/speedtest/v2?https=true&token={Uri.EscapeDataString(token.Groups[1].Value)}&urlCount=5", ct);
            using var apiJson = JsonDocument.Parse(api);
            var urls = apiJson.RootElement.GetProperty("targets").EnumerateArray()
                .Select(t => t.GetProperty("url").GetString())
                .OfType<string>()
                .ToList();
            if (urls.Count == 0)
            {
                return new SpeedResult(0, 0, "Tidak ada server Fast.com yang tersedia");
            }

            using var transfer = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var download = await MeasureAsync(ct, urls, (url, window) => DownloadAsync(transfer, AppendSize(url), window));
            var upload = await MeasureAsync(ct, urls, (url, window) => UploadAsync(transfer, url, window));

            return new SpeedResult(Math.Round(download, 2), Math.Round(upload, 2), download > 0 ? null : "Download Fast.com tidak menerima data");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Fast.com speedtest error");
            return new SpeedResult(0, 0, $"Fast.com gagal: {ex.Message}");
        }
    }

    private static string AppendSize(string url) => url.Contains('?') ? $"{url}&size=26214400" : $"{url}?size=26214400";

    /// <summary>
    /// Runs two transfers per server in a loop for <see cref="MeasureSeconds"/> and returns Mbps over
    /// the time actually measured; the clock starts before the first transfer.
    /// </summary>
    private static async Task<double> MeasureAsync(CancellationToken ct, IReadOnlyList<string> urls, Func<string, CancellationToken, Task<long>> transfer)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(TimeSpan.FromSeconds(MeasureSeconds));
        long bytes = 0;

        var sw = Stopwatch.StartNew();
        var workers = urls.SelectMany(url => Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            while (!window.IsCancellationRequested)
            {
                Interlocked.Add(ref bytes, await transfer(url, window.Token));
            }
        }))).ToList();
        try { await Task.WhenAll(workers); } catch (OperationCanceledException) { }
        sw.Stop();

        ct.ThrowIfCancellationRequested();
        return bytes * 8.0 / 1_000_000 / Math.Max(sw.Elapsed.TotalSeconds, 0.1);
    }

    /// <summary>Bytes received from one download until it ends or the window closes.</summary>
    private static async Task<long> DownloadAsync(HttpClient client, string url, CancellationToken window)
    {
        long read = 0;
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, window);
            if (!response.IsSuccessStatusCode)
            {
                await Task.Delay(200, window);
                return 0;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(window);
            var buffer = new byte[81920];
            int n;
            while ((n = await stream.ReadAsync(buffer, window)) > 0)
            {
                read += n;
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { }
        catch (IOException) { }

        return read;
    }

    /// <summary>
    /// Bytes of one upload. Bytes in a request the server then refused are not counted: only an
    /// accepted request, or one still being sent when the window closed, measures the line.
    /// </summary>
    private static async Task<long> UploadAsync(HttpClient client, string url, CancellationToken window)
    {
        long sent = 0;
        try
        {
            using var content = new CountingContent(1024 * 1024, n => sent += n);
            using var response = await client.PostAsync(url, content, window);
            if (!response.IsSuccessStatusCode)
            {
                await Task.Delay(200, window);
                return 0;
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException)
        {
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }

        return sent;
    }

    /// <summary>A body of random bytes that reports what was written to the connection.</summary>
    private sealed class CountingContent(int size, Action<long> onWritten) : HttpContent
    {
        private static readonly byte[] Payload = RandomPayload();

        private static byte[] RandomPayload()
        {
            var bytes = new byte[1024 * 1024];
            Random.Shared.NextBytes(bytes);
            return bytes;
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context, CancellationToken cancellationToken)
        {
            const int chunk = 16384;
            for (var offset = 0; offset < size; offset += chunk)
            {
                var length = Math.Min(chunk, size - offset);
                await stream.WriteAsync(Payload.AsMemory(offset % Payload.Length, length), cancellationToken);
                onWritten(length);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = size;
            return true;
        }
    }

    // ── Ookla ───────────────────────────────────────────────────────────────────

    private static async Task<SpeedResult> RunOoklaSpeedtestAsync(CancellationToken ct)
    {
        try
        {
            var exePath = await EnsureSpeedtestCliAsync(ct);
            if (exePath is null)
            {
                return new SpeedResult(0, 0, "Speedtest CLI Ookla tidak ditemukan dan gagal diunduh");
            }

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
            if (process is null)
            {
                return new SpeedResult(0, 0, "Gagal menjalankan Speedtest CLI");
            }

            var output = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                ct.ThrowIfCancellationRequested();
                return new SpeedResult(0, 0, "Speedtest Ookla timeout (120 detik)");
            }

            if (process.ExitCode != 0)
            {
                Log.Warning("Speedtest CLI exit code {Code}: {Stderr}", process.ExitCode, await stderr);
                return new SpeedResult(0, 0, $"Speedtest Ookla gagal (exit {process.ExitCode})");
            }

            var parsed = OoklaResult.Parse(await output);
            return parsed is null
                ? new SpeedResult(0, 0, "Hasil Speedtest Ookla tidak terbaca")
                : new SpeedResult(parsed.DownloadMbps, parsed.UploadMbps, null, parsed.PingMs, parsed.Server);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Ookla speedtest error");
            return new SpeedResult(0, 0, $"Speedtest Ookla gagal: {ex.Message}");
        }
    }

    /// <summary>
    /// The Ookla CLI: in the app's tools folder, else on PATH — only if it is Ookla's ("Speedtest by
    /// Ookla"; pip's speedtest-cli takes other flags) — else downloaded from Ookla.
    /// </summary>
    private static async Task<string?> EnsureSpeedtestCliAsync(CancellationToken ct)
    {
        var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
        var exePath = Path.Combine(toolsDir, "speedtest.exe");
        if (File.Exists(exePath))
        {
            return exePath;
        }

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "speedtest.exe",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is not null)
            {
                var version = await p.StandardOutput.ReadToEndAsync(ct);
                await p.WaitForExitAsync(ct);
                if (p.ExitCode == 0 && version.Contains("Speedtest by Ookla", StringComparison.OrdinalIgnoreCase))
                {
                    return "speedtest.exe";
                }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Not on PATH.
        }

        try
        {
            Log.Information("Downloading the Ookla Speedtest CLI");
            Directory.CreateDirectory(toolsDir);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var zip = await client.GetByteArrayAsync(ConfigManager.GetOoklaCliUrl(), ct);
            var zipPath = Path.Combine(toolsDir, "speedtest.zip");
            await File.WriteAllBytesAsync(zipPath, zip, ct);
            ZipFile.ExtractToDirectory(zipPath, toolsDir, overwriteFiles: true);
            File.Delete(zipPath);

            if (File.Exists(exePath))
            {
                return exePath;
            }

            Log.Warning("speedtest.exe not found after extracting the zip");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to download the Speedtest CLI");
        }

        return null;
    }
}
