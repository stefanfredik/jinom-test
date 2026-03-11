using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services;
using Serilog;

namespace FoTestingApp.Services;

/// <summary>
/// Menjalankan semua pengujian jaringan (Ping Gateway, Ping DNS, NSLookup, Ping Domain Lokal)
/// secara berurutan dan menampilkan hasil real-time ke UI.
/// </summary>
public class NetworkTestService
{
    private readonly ApiService _api;
    private readonly int _sessionId;
    private int _passCount;
    private int _totalCount;

    public NetworkTestService(ApiService api, int sessionId)
    {
        _api = api;
        _sessionId = sessionId;
    }

    public TestOverallStatus OverallStatus =>
        _totalCount == 0 ? TestOverallStatus.Fail :
        _passCount == _totalCount ? TestOverallStatus.Pass :
        _passCount > 0 ? TestOverallStatus.Partial :
        TestOverallStatus.Fail;

    /// <summary>
    /// Jalankan semua test berurutan. Update progress bar dan panel hasil via IProgress.
    /// </summary>
    public async Task RunAllAsync(StackPanel resultsPanel, IProgress<(int percent, string status)> progress, int packageMbps)
    {
        _passCount = 0;
        _totalCount = 0;

        progress.Report((0, "Memulai pengujian..."));

        // 1. Ping Gateway ISP
        progress.Report((5, "Task 1 of 7|Ping Gateway ISP|Mengirim paket ICMP..."));
        var gwTarget = ConfigManager.GetPingGatewayTarget();
        if (!string.IsNullOrEmpty(gwTarget))
        {
            await RunPingTestAsync(resultsPanel, TestTypes.PingGateway, gwTarget,
                ConfigManager.GetPingGatewayCount(), ConfigManager.GetPingGatewayThresholdMs());
        }
        progress.Report((20, "Task 1 of 7|Ping Gateway ISP|Selesai"));

        // 2. Ping DNS
        progress.Report((22, "Task 2 of 7|Ping DNS Server|Mengirim paket ICMP..."));
        await RunPingTestAsync(resultsPanel, TestTypes.PingDns, ConfigManager.GetPingDnsTarget(),
            ConfigManager.GetPingDnsCount(), ConfigManager.GetPingDnsThresholdMs());
        progress.Report((40, "Task 2 of 7|Ping DNS Server|Selesai"));

        // 3. NSLookup Nasional
        progress.Report((42, "Task 3 of 7|NSLookup Nasional|Resolving DNS..."));
        await RunNslookupAsync(resultsPanel, TestTypes.NslookupNasional, ConfigManager.GetNslookupNasionalDomains());
        progress.Report((55, "Task 3 of 7|NSLookup Nasional|Selesai"));

        // 4. NSLookup Internasional
        progress.Report((57, "Task 4 of 7|NSLookup Internasional|Resolving DNS..."));
        await RunNslookupAsync(resultsPanel, TestTypes.NslookupInternasional, ConfigManager.GetNslookupInternasionalDomains());
        progress.Report((65, "Task 4 of 7|NSLookup Internasional|Selesai"));

        // 5. Ping Domain Lokal
        progress.Report((67, "Task 5 of 7|Ping Domain Lokal|Mengirim paket ICMP..."));
        var localDomains = ConfigManager.GetPingDomainLokalDomains();
        foreach (var domain in localDomains)
        {
            await RunPingTestAsync(resultsPanel, TestTypes.PingDomainLokal, domain,
                ConfigManager.GetPingDomainLokalCount(), ConfigManager.GetPingGatewayThresholdMs());
        }
        progress.Report((80, "Task 5 of 7|Ping Domain Lokal|Selesai"));

        // 6. App Tests
        progress.Report((82, "Task 6 of 7|Aksesibilitas Aplikasi|Loading resource..."));
        var appSvc = new AppTestService(_api, _sessionId);
        var appResults = await appSvc.RunAllAsync(resultsPanel, progress);
        _passCount += appResults.passCount;
        _totalCount += appResults.totalCount;
        progress.Report((92, "Task 6 of 7|Aksesibilitas Aplikasi|Selesai"));

        // 7. Speedtest
        progress.Report((93, "Task 7 of 7|Speedtest|Mengukur throughput..."));
        var speedSvc = new SpeedtestService(_api, _sessionId);
        var speedResults = await speedSvc.RunAllAsync(resultsPanel, packageMbps);
        _passCount += speedResults.passCount;
        _totalCount += speedResults.totalCount;
        progress.Report((100, $"Task 7 of 7|Selesai|Status Akhir: {OverallStatus}"));
    }

    // ── Ping Test ──────────────────────────────────────────────────────────────

    private async Task RunPingTestAsync(StackPanel panel, string testType, string target, int count, int thresholdMs)
    {
        _totalCount++;
        var pingSvc = new Ping();
        var latencies = new List<long>();
        var rtoCount = 0;

        for (var i = 0; i < count; i++)
        {
            try
            {
                var reply = await pingSvc.SendPingAsync(target, 2000);
                if (reply.Status == IPStatus.Success)
                {
                    latencies.Add(reply.RoundtripTime);
                }
                else
                {
                    rtoCount++;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Ping error to {Target}", target);
                rtoCount++;
            }
        }

        var avg = latencies.Count > 0 ? (long)latencies.Average() : 9999;
        var max = latencies.Count > 0 ? latencies.Max() : 9999;
        var min = latencies.Count > 0 ? latencies.Min() : 9999;
        var status = avg <= thresholdMs && rtoCount == 0 ? TestStatus.Pass : TestStatus.Fail;

        if (status == TestStatus.Pass) { _passCount++; }

        var resultData = new { ping_count = count, avg_ms = avg, max_ms = max, min_ms = min, rto = rtoCount, threshold_ms = thresholdMs };

        // Simpan ke API
        try
        {
            await _api.SaveTestResultAsync(new FoTestResult
            {
                SessionId = _sessionId,
                TestType = testType,
                Target = target,
                ResultData = JsonDocument.Parse(JsonSerializer.Serialize(resultData)),
                Status = status,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save {TestType} result", testType);
        }

        // Tampilkan di UI
        panel.Dispatcher.Invoke(() =>
            panel.Children.Add(BuildResultCard(testType, target, status, resultData)));
    }

    // ── NSLookup Test ───────────────────────────────────────────────────────────

    private async Task RunNslookupAsync(StackPanel panel, string testType, string[] domains)
    {
        _totalCount++;
        var domainResults = new Dictionary<string, string>();

        foreach (var domain in domains)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(domain);
                domainResults[domain] = addresses.Length > 0 ? "Resolved" : "Failed";
            }
            catch
            {
                domainResults[domain] = "Failed";
            }
        }

        var allResolved = domainResults.Values.All(v => v == "Resolved");
        var status = allResolved ? TestStatus.Pass : TestStatus.Fail;
        if (status == TestStatus.Pass) { _passCount++; }

        var resultData = new { domains = domainResults };

        try
        {
            await _api.SaveTestResultAsync(new FoTestResult
            {
                SessionId = _sessionId,
                TestType = testType,
                Target = string.Join(", ", domains),
                ResultData = JsonDocument.Parse(JsonSerializer.Serialize(resultData)),
                Status = status,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save {TestType} result", testType);
        }

        panel.Dispatcher.Invoke(() =>
            panel.Children.Add(BuildNslookupCard(testType, domainResults, status)));
    }

    // ── UI Card Builders ────────────────────────────────────────────────────────

    private static Border BuildResultCard(string testType, string target, TestStatus status, object data)
    {
        var isPass = status == TestStatus.Pass;
        var primaryColor = isPass ? (SolidColorBrush)new BrushConverter().ConvertFromString("#22C55E")! : (SolidColorBrush)new BrushConverter().ConvertFromString("#EF4444")!;
        var bgColor = isPass ? (SolidColorBrush)new BrushConverter().ConvertFromString("#1A22C55E")! : (SolidColorBrush)new BrushConverter().ConvertFromString("#1AEF4444")!;
        var iconKind = isPass ? MaterialDesignThemes.Wpf.PackIconKind.CheckCircle : MaterialDesignThemes.Wpf.PackIconKind.AlertCircle;
        var statusText = isPass ? "COMPLETED" : "FAILED";
        
        var json = JsonSerializer.Serialize(data);
        var parsed = JsonDocument.Parse(json);

        var label = testType switch
        {
            TestTypes.PingGateway => "Ping Gateway ISP",
            TestTypes.PingDns => "Ping DNS Server",
            TestTypes.PingDomainLokal => "Ping Domain Lokal",
            _ => testType,
        };
        
        var descText = $"Avg: {parsed.RootElement.GetProperty("avg_ms").GetInt64()}ms | RTO: {parsed.RootElement.GetProperty("rto").GetInt32()}";

        return CreateLogCard(label, descText, primaryColor, bgColor, iconKind, statusText);
    }

    private static Border BuildNslookupCard(string testType, Dictionary<string, string> domains, TestStatus status)
    {
        var isPass = status == TestStatus.Pass;
        var primaryColor = isPass ? (SolidColorBrush)new BrushConverter().ConvertFromString("#22C55E")! : (SolidColorBrush)new BrushConverter().ConvertFromString("#EF4444")!;
        var bgColor = isPass ? (SolidColorBrush)new BrushConverter().ConvertFromString("#1A22C55E")! : (SolidColorBrush)new BrushConverter().ConvertFromString("#1AEF4444")!;
        var iconKind = isPass ? MaterialDesignThemes.Wpf.PackIconKind.Dns : MaterialDesignThemes.Wpf.PackIconKind.AlertCircle;
        var statusText = isPass ? "COMPLETED" : "FAILED";
        
        var label = testType == TestTypes.NslookupNasional ? "NSLookup Nasional" : "NSLookup Internasional";
        
        var failedDomains = domains.Where(d => d.Value != "Resolved").Select(d => d.Key).ToList();
        var descText = failedDomains.Count > 0 ? $"Gagal di: {string.Join(", ", failedDomains)}" : "Semua domain berhasil di-resolve";

        return UIHelper.CreateLogCard(label, descText, primaryColor, bgColor, iconKind, statusText);
    }
}
