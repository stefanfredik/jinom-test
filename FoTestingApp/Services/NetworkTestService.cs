using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Controls;
using System.Windows.Media;
using DnsClient;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services.Core;
using Serilog;

namespace FoTestingApp.Services;

/// <summary>
/// Runs the whole test package in order — Ping Gateway, Ping DNS, NSLookup, Ping Domain Lokal, then
/// the application tests and the speedtests — showing each result as it comes. Every configured
/// test produces a result: one that cannot run (no gateway found, an empty list in Settings) is a
/// FAIL with its reason, never silently left out of the count.
/// </summary>
public class NetworkTestService
{
    public async Task RunAllAsync(TestRun run, StackPanel resultsPanel, IProgress<(int percent, string status)> progress, int packageMbps)
    {
        var ct = run.Cancellation;
        progress.Report((0, "Memulai pengujian..."));

        // 1. Ping Gateway ISP: the configured target, else the active adapter's default gateway.
        progress.Report((5, "Task 1 of 7|Ping Gateway ISP|Mengirim paket ICMP..."));
        var gateway = ResolveGateway();
        if (gateway is null)
        {
            await FailUnrunnableAsync(run, resultsPanel, TestTypes.PingGateway, "-", "Gateway tidak ditemukan: tidak ada adapter jaringan aktif dengan default gateway.");
        }
        else
        {
            await RunPingTestAsync(run, resultsPanel, TestTypes.PingGateway, gateway,
                ConfigManager.GetPingGatewayCount(), ConfigManager.GetPingGatewayThresholdMs(), ConfigManager.GetPingGatewayMaxRto());
        }
        progress.Report((20, "Task 1 of 7|Ping Gateway ISP|Selesai"));

        // 2. Ping DNS
        progress.Report((22, "Task 2 of 7|Ping DNS Server|Mengirim paket ICMP..."));
        await PingEachAsync(run, resultsPanel, TestTypes.PingDns, ConfigManager.GetPingDnsTargets(),
            ConfigManager.GetPingDnsCount(), ConfigManager.GetPingDnsThresholdMs(), ConfigManager.GetPingDnsMaxRto());
        progress.Report((40, "Task 2 of 7|Ping DNS Server|Selesai"));

        // 3. NSLookup Nasional
        progress.Report((42, "Task 3 of 7|NSLookup Nasional|Resolving DNS..."));
        await RunNslookupAsync(run, resultsPanel, TestTypes.NslookupNasional, ConfigManager.GetNslookupNasionalDomains(), ConfigManager.GetNslookupNasionalTimeoutSeconds());
        progress.Report((55, "Task 3 of 7|NSLookup Nasional|Selesai"));

        // 4. NSLookup Internasional
        progress.Report((57, "Task 4 of 7|NSLookup Internasional|Resolving DNS..."));
        await RunNslookupAsync(run, resultsPanel, TestTypes.NslookupInternasional, ConfigManager.GetNslookupInternasionalDomains(), ConfigManager.GetNslookupInternasionalTimeoutSeconds());
        progress.Report((65, "Task 4 of 7|NSLookup Internasional|Selesai"));

        // 5. Ping Domain Lokal
        progress.Report((67, "Task 5 of 7|Ping Domain Lokal|Mengirim paket ICMP..."));
        await PingEachAsync(run, resultsPanel, TestTypes.PingDomainLokal, ConfigManager.GetPingDomainLokalDomains(),
            ConfigManager.GetPingDomainLokalCount(), ConfigManager.GetPingDomainLokalThresholdMs(), ConfigManager.GetPingDomainLokalMaxRto());
        progress.Report((80, "Task 5 of 7|Ping Domain Lokal|Selesai"));

        // 6. App Tests
        progress.Report((82, "Task 6 of 7|Aksesibilitas Aplikasi|Loading resource..."));
        await new AppTestService().RunAllAsync(run, resultsPanel);
        progress.Report((92, "Task 6 of 7|Aksesibilitas Aplikasi|Selesai"));

        // 7. Speedtest
        progress.Report((93, "Task 7 of 7|Speedtest|Mengukur throughput..."));
        await new SpeedtestService().RunAllAsync(run, resultsPanel, packageMbps);
        progress.Report((100, $"Task 7 of 7|Selesai|Status Akhir: {run.OverallStatus}"));
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>The gateway to ping: the configured one, else the first active IPv4 default gateway.</summary>
    private static string? ResolveGateway()
    {
        var configured = ConfigManager.GetPingGatewayTarget();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .SelectMany(nic => nic.GetIPProperties().GatewayAddresses)
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))
                ?.ToString();
        }
        catch (NetworkInformationException ex)
        {
            Log.Warning(ex, "Could not read the network adapters' gateways");
            return null;
        }
    }

    private async Task PingEachAsync(TestRun run, StackPanel panel, string testType, string[] targets, int count, int thresholdMs, int maxRto)
    {
        if (targets.Length == 0)
        {
            await FailUnrunnableAsync(run, panel, testType, "-", "Daftar target kosong. Isi di Pengaturan.");
            return;
        }

        foreach (var target in targets)
        {
            await RunPingTestAsync(run, panel, testType, target, count, thresholdMs, maxRto);
        }
    }

    // ── Ping Test ──────────────────────────────────────────────────────────────

    private async Task RunPingTestAsync(TestRun run, StackPanel panel, string testType, string target, int count, int thresholdMs, int maxRto)
    {
        run.Cancellation.ThrowIfCancellationRequested();
        var latencies = new List<long>();
        var interval = ConfigManager.GetPingIntervalMs();
        string? error = null;

        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            run.Cancellation.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(target, 2000);
                if (reply.Status == IPStatus.Success)
                {
                    latencies.Add(reply.RoundtripTime);
                }
            }
            catch (PingException ex)
            {
                // A name that does not resolve fails every ping the same way: say so once.
                error ??= ex.InnerException?.Message ?? ex.Message;
                Log.Warning(ex, "Ping error to {Target}", target);
            }

            // Spaced like ping does, so routers that rate-limit ICMP do not drop back-to-back echoes as RTO.
            if (i < count - 1 && interval > 0)
            {
                await Task.Delay(interval, run.Cancellation);
            }
        }

        var stats = PingStats.From(count, latencies);
        var passed = stats.Passes(thresholdMs, maxRto);
        var data = new
        {
            ping_count = stats.Sent,
            received = stats.Received,
            rto = stats.Rto,
            loss_pct = Math.Round(stats.LossPercent, 1),
            avg_ms = stats.AvgMs is null ? (double?)null : Math.Round(stats.AvgMs.Value, 1),
            min_ms = stats.MinMs,
            max_ms = stats.MaxMs,
            threshold_ms = thresholdMs,
            max_rto = maxRto,
            error = stats.Received == 0 ? error ?? "Tidak ada balasan" : null,
        };

        await run.RecordAsync(testType, target, data, passed);

        var label = ResultFormatter.TestName(testType);
        var desc = stats.Received == 0
            ? $"{target}: tidak ada balasan ({stats.Sent} RTO)"
            : $"{target} — Avg: {ResultFormatter.Ms(stats.AvgMs)} | RTO: {stats.Rto}";
        panel.Dispatcher.Invoke(() => panel.Children.Add(BuildCard(label, desc, passed, MaterialDesignThemes.Wpf.PackIconKind.CheckCircle)));
    }

    // ── NSLookup Test ───────────────────────────────────────────────────────────

    /// <summary>
    /// Asks the adapter's DNS servers directly, without a cache, so a server that stopped answering
    /// fails here instead of being covered by Windows' DNS cache or the hosts file.
    /// </summary>
    private async Task RunNslookupAsync(TestRun run, StackPanel panel, string testType, string[] domains, int timeoutSeconds)
    {
        run.Cancellation.ThrowIfCancellationRequested();
        if (domains.Length == 0)
        {
            await FailUnrunnableAsync(run, panel, testType, "-", "Daftar domain kosong. Isi di Pengaturan.");
            return;
        }

        var lookup = new LookupClient(new LookupClientOptions
        {
            UseCache = false,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)),
            Retries = 1,
            ThrowDnsErrors = false,
        });
        var servers = lookup.NameServers.Select(s => s.Address.ToString()).Distinct().ToArray();
        var domainResults = new Dictionary<string, string>();

        foreach (var domain in domains)
        {
            run.Cancellation.ThrowIfCancellationRequested();
            try
            {
                var answer = await lookup.QueryAsync(domain, QueryType.A, cancellationToken: run.Cancellation);
                domainResults[domain] = !answer.HasError && answer.Answers.ARecords().Any() ? "Resolved" : "Failed";
            }
            catch (DnsResponseException ex) when (ex.Code == DnsResponseCode.ConnectionTimeout)
            {
                domainResults[domain] = "Timeout";
            }
            catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "NSLookup of {Domain} failed", domain);
                domainResults[domain] = "Failed";
            }
        }

        var passed = domainResults.Values.All(v => v == "Resolved");
        await run.RecordAsync(testType, string.Join(", ", domains), new { domains = domainResults, dns_servers = servers }, passed);

        var failed = domainResults.Where(d => d.Value != "Resolved").Select(d => $"{d.Key} ({d.Value})").ToList();
        var desc = failed.Count > 0 ? $"Gagal di: {string.Join(", ", failed)}" : $"Semua domain ter-resolve lewat {string.Join(", ", servers)}";
        panel.Dispatcher.Invoke(() => panel.Children.Add(BuildCard(ResultFormatter.TestName(testType), desc, passed, MaterialDesignThemes.Wpf.PackIconKind.Dns)));
    }

    /// <summary>A test that could not run is a FAIL with its reason, counted like any other.</summary>
    private static async Task FailUnrunnableAsync(TestRun run, StackPanel panel, string testType, string target, string reason)
    {
        await run.RecordAsync(testType, target, new { error = reason }, passed: false);
        panel.Dispatcher.Invoke(() => panel.Children.Add(BuildCard(ResultFormatter.TestName(testType), reason, false, MaterialDesignThemes.Wpf.PackIconKind.AlertCircle)));
    }

    // ── UI Card Builders ────────────────────────────────────────────────────────

    internal static System.Windows.Controls.Border BuildCard(string label, string description, bool passed, MaterialDesignThemes.Wpf.PackIconKind passIcon)
    {
        var converter = new BrushConverter();
        var primary = (SolidColorBrush)converter.ConvertFromString(passed ? "#22C55E" : "#EF4444")!;
        var background = (SolidColorBrush)converter.ConvertFromString(passed ? "#1A22C55E" : "#1AEF4444")!;
        var icon = passed ? passIcon : MaterialDesignThemes.Wpf.PackIconKind.AlertCircle;

        return UIHelper.CreateLogCard(label, description, primary, background, icon, passed ? "PASS" : "FAIL");
    }
}
