using FoTestingApp.Models;
using FoTestingApp.Services.Core;
using Xunit;

namespace FoTestingApp.Tests;

public class PingStatsTests
{
    [Fact]
    public void Averages_the_answered_pings_and_counts_the_lost_ones()
    {
        var stats = PingStats.From(5, [4, 6, 5]);

        Assert.Equal(3, stats.Received);
        Assert.Equal(2, stats.Rto);
        Assert.Equal(5.0, stats.AvgMs);
        Assert.Equal(4, stats.MinMs);
        Assert.Equal(6, stats.MaxMs);
        Assert.Equal(40.0, stats.LossPercent);
    }

    [Fact]
    public void Has_no_latency_at_all_when_nothing_answered()
    {
        var stats = PingStats.From(100, []);

        Assert.Null(stats.AvgMs);
        Assert.Null(stats.MinMs);
        Assert.Null(stats.MaxMs);
        Assert.Equal(100, stats.Rto);
        Assert.False(stats.Passes(thresholdAvgMs: 10, maxRto: 1000));
    }

    [Fact]
    public void Compares_the_unrounded_average_so_a_fraction_over_the_limit_fails()
    {
        // 10, 11 → 10.5 ms: over a 10 ms limit, though it truncates to 10.
        Assert.False(PingStats.From(2, [10, 11]).Passes(thresholdAvgMs: 10, maxRto: 0));
        Assert.True(PingStats.From(2, [10, 10]).Passes(thresholdAvgMs: 10, maxRto: 0));
    }

    [Fact]
    public void Fails_on_more_lost_replies_than_allowed()
    {
        var oneLost = PingStats.From(3, [1, 1]);

        Assert.False(oneLost.Passes(thresholdAvgMs: 10, maxRto: 0));
        Assert.True(oneLost.Passes(thresholdAvgMs: 10, maxRto: 1));
    }
}

public class SpeedVerdictTests
{
    [Theory]
    [InlineData(85.0, 100, 85, true)]   // exactly the threshold
    [InlineData(84.9, 100, 85, false)]
    [InlineData(2.0, 100, 85, false)]   // the old rule passed anything above 0
    [InlineData(99.0, 100, 99, true)]
    [InlineData(0.0, 100, 85, false)]
    public void Passes_at_the_threshold_share_of_the_package(double download, int package, int pct, bool passes) =>
        Assert.Equal(passes, SpeedVerdict.Passes(download, package, pct));

    [Fact]
    public void A_site_without_a_package_passes_on_any_measured_speed_but_not_on_none()
    {
        Assert.True(SpeedVerdict.Passes(3.2, packageMbps: 0, thresholdPercent: 85));
        Assert.False(SpeedVerdict.Passes(0, packageMbps: 0, thresholdPercent: 85));
        Assert.Null(SpeedVerdict.RequiredMbps(0, 85));
    }

    [Fact]
    public void Names_the_speed_a_package_needs() =>
        Assert.Equal(42.5, SpeedVerdict.RequiredMbps(50, 85));
}

public class PackageSpeedTests
{
    [Theory]
    [InlineData("50 Mbps", 50)]
    [InlineData("Jinom 100M", 100)]
    [InlineData("1 Gbps", 1000)]
    [InlineData("1,5 Gbps", 1500)]
    [InlineData("2.5 Gbps", 2500)]
    [InlineData("1.000 Mbps", 1000)]
    [InlineData("512 Kbps", 1)]
    [InlineData("20", 20)]
    [InlineData("Paket Rumah", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Reads_the_speed_from_how_billing_writes_it(string? text, int mbps) =>
        Assert.Equal(mbps, PackageSpeed.ParseMbps(text));
}

public class RunVerdictTests
{
    [Theory]
    [InlineData(0, 0, TestOverallStatus.Fail)]   // nothing ran: never a vacuous PASS
    [InlineData(5, 5, TestOverallStatus.Pass)]
    [InlineData(3, 5, TestOverallStatus.Partial)]
    [InlineData(0, 5, TestOverallStatus.Fail)]
    public void Is_pass_only_when_tests_ran_and_all_passed(int pass, int total, TestOverallStatus expected) =>
        Assert.Equal(expected, RunVerdict.Overall(pass, total));
}

public class OoklaResultTests
{
    private const string Result = """
        {"type":"log","timestamp":"2026-11-18T03:00:00Z","message":"Server selection","level":"info"}
        {"type":"result","ping":{"jitter":0.8,"latency":4.36},"download":{"bandwidth":12500000,"bytes":1},"upload":{"bandwidth":6250000,"bytes":1},"server":{"name":"Jinom","location":"Denpasar"}}
        """;

    [Fact]
    public void Converts_bytes_per_second_to_megabits_and_reads_the_result_line()
    {
        var result = OoklaResult.Parse(Result);

        Assert.NotNull(result);
        Assert.Equal(100.0, result.DownloadMbps);
        Assert.Equal(50.0, result.UploadMbps);
        Assert.Equal(4.4, result.PingMs);
        Assert.Equal("Jinom — Denpasar", result.Server);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Speedtest by Ookla: error")]
    [InlineData("""{"type":"log","message":"x"}""")]
    public void Is_null_without_a_result(string output) => Assert.Null(OoklaResult.Parse(output));
}
