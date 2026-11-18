using System.Globalization;
using System.Text.Json;
using FoTestingApp.Models;

namespace FoTestingApp.Services.Core;

/// <summary>
/// How one test result's data reads, for the results screen, the dashboard and the PDF alike, so
/// the three never disagree. Values the app once wrote as stand-ins for "no measurement" (9999 ms
/// for a ping that never answered, -1 s for a page that failed) are shown as what they mean,
/// never as numbers.
/// </summary>
public static class ResultFormatter
{
    /// <summary>Legacy stand-in for "no reply" in ping results written before nulls were used.</summary>
    private const double LegacyNoReplyMs = 9999;

    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    public static string TestName(string testType) => testType switch
    {
        TestTypes.PingGateway => "Ping Gateway ISP",
        TestTypes.PingDns => "Ping DNS Server",
        TestTypes.NslookupNasional => "NSLookup Nasional",
        TestTypes.NslookupInternasional => "NSLookup Internasional",
        TestTypes.PingDomainLokal => "Ping Domain Lokal",
        TestTypes.BrowsingTest => "Browsing",
        TestTypes.StreamingTest => "Streaming",
        TestTypes.SocialMediaTest => "Social Media",
        TestTypes.SpeedtestFast => "Speedtest Fast.com",
        TestTypes.SpeedtestOokla => "Speedtest Ookla",
        _ => testType,
    };

    /// <summary>The result's data as label/value rows; empty when there is no data.</summary>
    public static IReadOnlyList<(string Label, string Value)> Rows(string testType, JsonElement? data)
    {
        var rows = new List<(string, string)>();
        if (Unwrap(data) is not { } root || root.ValueKind != JsonValueKind.Object)
        {
            return rows;
        }

        if (Text(root, "error") is { Length: > 0 } error)
        {
            rows.Add(("Keterangan", error));
        }

        switch (testType)
        {
            case TestTypes.PingGateway or TestTypes.PingDns or TestTypes.PingDomainLokal:
                var sent = Int(root, "ping_count");
                var rto = Int(root, "rto");
                rows.Add(("Rata-rata", Ms(Number(root, "avg_ms"))));
                rows.Add(("Minimum", Ms(Number(root, "min_ms"))));
                rows.Add(("Maksimum", Ms(Number(root, "max_ms"))));
                rows.Add(("RTO", sent is > 0 && rto is not null
                    ? $"{rto} dari {sent} ({(rto.Value * 100.0 / sent.Value).ToString("0.#", Id)}%)"
                    : rto?.ToString(Id) ?? "-"));
                break;

            case TestTypes.NslookupNasional or TestTypes.NslookupInternasional:
                if (root.TryGetProperty("domains", out var domains) && domains.ValueKind == JsonValueKind.Object)
                {
                    foreach (var domain in domains.EnumerateObject())
                    {
                        rows.Add((domain.Name, DnsStatus(domain.Value.GetString())));
                    }
                }
                if (root.TryGetProperty("dns_servers", out var servers) && servers.ValueKind == JsonValueKind.Array)
                {
                    rows.Add(("Server DNS", string.Join(", ", servers.EnumerateArray().Select(s => s.GetString()))));
                }
                break;

            case TestTypes.BrowsingTest:
                if (root.TryGetProperty("results", out var pages) && pages.ValueKind == JsonValueKind.Object)
                {
                    foreach (var page in pages.EnumerateObject())
                    {
                        rows.Add((Host(page.Name), Seconds(page.Value)));
                    }
                }
                break;

            case TestTypes.StreamingTest or TestTypes.SocialMediaTest:
                if (root.TryGetProperty("results", out var sites) && sites.ValueKind == JsonValueKind.Object)
                {
                    foreach (var site in sites.EnumerateObject())
                    {
                        rows.Add((Host(site.Name), site.Value.ValueKind == JsonValueKind.String ? site.Value.GetString() ?? "-" : site.Value.ToString()));
                    }
                }
                break;

            case TestTypes.SpeedtestFast or TestTypes.SpeedtestOokla:
                rows.Add(("Download", Mbps(Number(root, "download_mbps"))));
                rows.Add(("Upload", Mbps(Number(root, "upload_mbps"))));
                if (Number(root, "required_mbps") is { } required)
                {
                    rows.Add(("Minimal lulus", $"{Mbps(required)} ({Int(root, "threshold_pct")}% dari paket {Int(root, "package_mbps")} Mbps)"));
                }
                if (Number(root, "ping_ms") is { } ping)
                {
                    rows.Add(("Ping", Ms(ping)));
                }
                if (Text(root, "server") is { Length: > 0 } server)
                {
                    rows.Add(("Server", server));
                }
                break;
        }

        return rows;
    }

    /// <summary>One line for a list: the error when it failed with one, else the key figures.</summary>
    public static string Summary(string testType, JsonElement? data, bool passed)
    {
        if (Unwrap(data) is not { } root || root.ValueKind != JsonValueKind.Object)
        {
            return "-";
        }

        if (!passed && Text(root, "error") is { Length: > 0 } error)
        {
            return $"Gagal: {error}";
        }

        return testType switch
        {
            TestTypes.PingGateway or TestTypes.PingDns or TestTypes.PingDomainLokal =>
                $"Avg: {Ms(Number(root, "avg_ms"))} | Max: {Ms(Number(root, "max_ms"))} | RTO: {Int(root, "rto")?.ToString(Id) ?? "-"}",
            TestTypes.SpeedtestFast or TestTypes.SpeedtestOokla =>
                $"↓ {Mbps(Number(root, "download_mbps"))} | ↑ {Mbps(Number(root, "upload_mbps"))}",
            _ => string.Join(" | ", Rows(testType, data).Select(r => $"{r.Label}: {r.Value}")),
        };
    }

    private static JsonElement? Unwrap(JsonElement? data)
    {
        if (data is not { } root)
        {
            return null;
        }

        // Some rows were stored as a JSON string holding the object.
        if (root.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var doc = JsonDocument.Parse(root.GetString() ?? "{}");
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return root;
    }

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static int? Int(JsonElement root, string name) =>
        Number(root, name) is { } n ? (int)Math.Round(n) : null;

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A latency; no reply (null, or the old 9999) reads as such.</summary>
    public static string Ms(double? ms) =>
        ms is null || ms >= LegacyNoReplyMs ? "Tidak ada balasan" : $"{ms.Value.ToString("0.#", Id)} ms";

    public static string Mbps(double? mbps) => mbps is null ? "-" : $"{mbps.Value.ToString("0.0", Id)} Mbps";

    /// <summary>A page load time; a failure (null, or the old -1) reads as such.</summary>
    private static string Seconds(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.GetDouble() >= 0
            ? $"{value.GetDouble().ToString("0.0", Id)} detik"
            : "Gagal dimuat";

    private static string DnsStatus(string? status) => status switch
    {
        "Resolved" => "Berhasil",
        "Timeout" => "Timeout",
        null => "-",
        _ => "Gagal",
    };

    private static string Host(string key) =>
        key.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(key, UriKind.Absolute, out var uri)
            ? uri.Host
            : key.Replace('_', ' ');
}
