using System.Text.Json;
using FoTestingApp.Models;

namespace FoTestingApp.Services.Core;

/// <summary>One measured value against its limit (a latency under its ceiling, a speed over its floor).</summary>
/// <param name="Label">What was measured (a target, "Fast.com").</param>
/// <param name="Value">The measurement; null when nothing could be measured.</param>
/// <param name="Limit">The pass limit, or null when there is none (a POP's speed).</param>
/// <param name="HigherIsBetter">A speed must reach the limit; a latency must stay under it.</param>
public sealed record BulletItem(string Label, double? Value, double? Limit, string Unit, bool Passed, bool HigherIsBetter = false);

/// <summary>
/// What the charts show, read from stored results: the same data the PDF and the rows read, so a
/// chart never disagrees with the numbers beside it. A value that was not measured stays null (a
/// ping with no reply, the old 9999 stand-in), never a bar of zero.
/// </summary>
public static class ChartData
{
    private const double LegacyNoReplyMs = 9999;

    private static readonly string[] PingTypes = [TestTypes.PingGateway, TestTypes.PingDns, TestTypes.PingDomainLokal];
    private static readonly string[] SpeedTypes = [TestTypes.SpeedtestFast, TestTypes.SpeedtestOokla];

    public static bool IsPing(string testType) => PingTypes.Contains(testType);

    public static bool IsSpeed(string testType) => SpeedTypes.Contains(testType);

    /// <summary>Average latency per pinged target, against its limit.</summary>
    public static List<BulletItem> Latency(IEnumerable<FoTestResult> results) =>
        results.Where(r => IsPing(r.TestType))
            .Select(r =>
            {
                var root = ResultFormatter.Unwrap(r.ResultData?.RootElement);
                var avg = root is { ValueKind: JsonValueKind.Object } o ? ResultFormatter.Number(o, "avg_ms") : null;
                var limit = root is { ValueKind: JsonValueKind.Object } p ? ResultFormatter.Number(p, "threshold_ms") : null;
                return new BulletItem(string.IsNullOrWhiteSpace(r.Target) ? ResultFormatter.TestName(r.TestType) : r.Target,
                    avg >= LegacyNoReplyMs ? null : avg, limit, "ms", r.Status == TestStatus.Pass);
            })
            .ToList();

    /// <summary>Download per speedtest, against the speed the package needs (none for a POP).</summary>
    public static List<BulletItem> Download(IEnumerable<FoTestResult> results) =>
        results.Where(r => IsSpeed(r.TestType))
            .Select(r =>
            {
                var root = ResultFormatter.Unwrap(r.ResultData?.RootElement);
                var o = root is { ValueKind: JsonValueKind.Object } x ? x : (JsonElement?)null;
                var download = o is { } d ? ResultFormatter.Number(d, "download_mbps") : null;
                return new BulletItem(ResultFormatter.TestName(r.TestType).Replace("Speedtest ", string.Empty),
                    download is > 0 ? download : null, o is { } q ? ResultFormatter.Number(q, "required_mbps") : null,
                    "Mbps", r.Status == TestStatus.Pass, HigherIsBetter: true);
            })
            .ToList();

    /// <summary>
    /// Each echo's round-trip time of a ping result (samples_ms, null = lost), or null for a result
    /// stored before the samples were kept.
    /// </summary>
    public static double?[]? PingSamples(FoTestResult result)
    {
        if (!IsPing(result.TestType)
            || ResultFormatter.Unwrap(result.ResultData?.RootElement) is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("samples_ms", out var samples)
            || samples.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return samples.EnumerateArray()
            .Select(s => s.ValueKind == JsonValueKind.Number ? s.GetDouble() : (double?)null)
            .ToArray();
    }

    /// <summary>A ping result's pass limit in ms, for its chart.</summary>
    public static double? PingLimit(FoTestResult result) =>
        ResultFormatter.Unwrap(result.ResultData?.RootElement) is { ValueKind: JsonValueKind.Object } root
            ? ResultFormatter.Number(root, "threshold_ms")
            : null;
}
