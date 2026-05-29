using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FastComTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Fast.com Diagnostic Test...");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            // 1. Get token from Fast.com html
            Console.WriteLine("Step 1: Fetching fast.com html...");
            var html = await client.GetStringAsync("https://fast.com");
            var tokenMatch = System.Text.RegularExpressions.Regex.Match(html, @"/app-([a-f0-9]+)\.js");
            if (!tokenMatch.Success)
            {
                Console.WriteLine("Failed to match script url! HTML content excerpt:");
                Console.WriteLine(html.Length > 500 ? html.Substring(0, 500) : html);
                return;
            }

            var scriptUrl = $"https://fast.com{tokenMatch.Value}";
            Console.WriteLine($"Found script url: {scriptUrl}");
            var script = await client.GetStringAsync(scriptUrl);
            var apiTokenMatch = System.Text.RegularExpressions.Regex.Match(script, @"token:\s*[""']([^""']+)[""']");
            if (!apiTokenMatch.Success)
            {
                Console.WriteLine("Failed to match token inside script!");
                return;
            }

            var token = apiTokenMatch.Groups[1].Value;
            Console.WriteLine($"Extracted API Token: {token}");

            // 2. Get targets from API
            var apiUrl = $"https://api.fast.com/netflix/speedtest/v2?https=true&token={Uri.EscapeDataString(token)}&urlCount=5";
            Console.WriteLine($"Step 2: Fetching targets from {apiUrl}...");
            var apiResponse = await client.GetStringAsync(apiUrl);
            Console.WriteLine($"API Response: {apiResponse}");
            var apiJson = JsonDocument.Parse(apiResponse);
            var targets = apiJson.RootElement.GetProperty("targets");

            if (targets.GetArrayLength() == 0)
            {
                Console.WriteLine("No target servers returned.");
                return;
            }

            // 3. Download Speedtest
            Console.WriteLine("\nStep 3: Starting Download Test (10s)...");
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            long totalDownloadBytes = 0;
            var downloadTasks = new List<Task>();

            foreach (var target in targets.EnumerateArray())
            {
                var url = target.GetProperty("url").GetString();
                if (url == null) continue;

                var downloadUrl = url.Contains('?') ? $"{url}&size=26214400" : $"{url}?size=26214400";
                Console.WriteLine($"Target DL URL: {downloadUrl}");

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
            Console.WriteLine($"Downloaded Bytes: {totalDownloadBytes} bytes in {dlSw.Elapsed.TotalSeconds:F2} seconds");
            Console.WriteLine($"Calculated Download Speed: {downloadMbps:F2} Mbps");

            // 4. Upload Speedtest
            Console.WriteLine("\nStep 4: Starting Upload Test (10s)...");
            var uCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            long totalUploadBytes = 0;
            var uploadTasks = new List<Task>();
            byte[] uploadPayload = new byte[1024 * 1024]; // 1MB chunk
            new Random().NextBytes(uploadPayload);

            foreach (var target in targets.EnumerateArray())
            {
                var url = target.GetProperty("url").GetString();
                if (url == null) continue;
                Console.WriteLine($"Target UL URL: {url}");

                for (int i = 0; i < 2; i++)
                {
                    uploadTasks.Add(Task.Run(async () =>
                    {
                        int attempt = 0;
                        while (!uCts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                attempt++;
                                using var content = new ByteArrayContent(uploadPayload);
                                using var response = await client.PostAsync(url, content, uCts.Token);
                                if (response.IsSuccessStatusCode)
                                {
                                    Interlocked.Add(ref totalUploadBytes, uploadPayload.Length);
                                }
                                else
                                {
                                    if (attempt == 1)
                                    {
                                        Console.WriteLine($"POST failed with status: {response.StatusCode} ({response.ReasonPhrase}) for {url}");
                                        var body = await response.Content.ReadAsStringAsync();
                                        Console.WriteLine($"Response body: {body}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (attempt == 1)
                                {
                                    Console.WriteLine($"POST exception: {ex.Message} for {url}");
                                }
                            }
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
            Console.WriteLine($"Uploaded Bytes: {totalUploadBytes} bytes in {ulSw.Elapsed.TotalSeconds:F2} seconds");
            Console.WriteLine($"Calculated Upload Speed: {uploadMbps:F2} Mbps");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Global Exception: {ex}");
        }
    }

    private static async Task<long> DownloadAndCountBytesAsync(HttpClient client, string url, CancellationToken ct)
    {
        long totalRead = 0;
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                totalRead += bytesRead;
                if (ct.IsCancellationRequested) break;
            }
            return totalRead;
        }
        catch (Exception ex)
        {
            // Return whatever bytes were successfully read before cancellation/error
            return totalRead;
        }
    }
}
