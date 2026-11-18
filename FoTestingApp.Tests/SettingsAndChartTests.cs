using System.Text.Json;
using FoTestingApp.Models;
using FoTestingApp.Services.Core;
using Xunit;

namespace FoTestingApp.Tests;

/// <summary>The parameters set in Jinom Assets, as the app reads them (ApiService's JSON options).</summary>
public class TestSettingsTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class Envelope
    {
        public TestSettings? Data { get; set; }
    }

    private static TestSettings FromServer() =>
        JsonSerializer.Deserialize<Envelope>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fo-test-settings.json")), Json)!.Data!;

    [Fact]
    public void Reads_every_parameter_the_server_sends()
    {
        var s = FromServer().Normalized();

        Assert.Equal(30, s.SessionTimeoutMinutes);
        Assert.Equal(200, s.PingIntervalMs);
        Assert.Equal(string.Empty, s.PingGateway.Target);
        Assert.Equal(100, s.PingGateway.Count);
        Assert.Equal(10, s.PingGateway.ThresholdAvgMs);
        Assert.Equal(["103.122.65.66", "8.8.8.8", "1.1.1.1"], s.PingDns.Targets);
        Assert.Equal(35, s.PingDns.ThresholdAvgMs);
        Assert.Equal(["jinom.net", "kompas.com"], s.PingDomainLokal.Domains);
        Assert.Equal(35, s.PingDomainLokal.Count);
        Assert.Equal(3, s.NslookupNasional.Domains.Length);
        Assert.Equal(10, s.NslookupInternasional.TimeoutSeconds);
        Assert.Equal(10, s.Browsing.ThresholdSeconds);
        Assert.Equal(30, s.SocialMedia.ThresholdSeconds);
        Assert.Equal(85, s.Speedtest.ThresholdPercentage);
        Assert.StartsWith("https://", s.Speedtest.OoklaCliUrl);
        Assert.Contains("Mozilla", s.UserAgent);
        Assert.False(string.IsNullOrWhiteSpace(s.ServerTimezone));
    }

    [Fact]
    public void Keeps_values_from_an_older_server_within_what_a_test_can_run()
    {
        var s = new TestSettings
        {
            SessionTimeoutMinutes = 0,
            PingDns = new PingSettings { Targets = [" 8.8.8.8 ", "", "8.8.8.8"], Count = 0, ThresholdAvgMs = -5, MaxRto = -1 },
            Speedtest = new SpeedtestSettings { ThresholdPercentage = 250 },
        }.Normalized();

        Assert.Equal(5, s.SessionTimeoutMinutes);
        Assert.Equal(["8.8.8.8"], s.PingDns.Targets);
        Assert.Equal(1, s.PingDns.Count);
        Assert.Equal(1, s.PingDns.ThresholdAvgMs);
        Assert.Equal(0, s.PingDns.MaxRto);
        Assert.Equal(100, s.Speedtest.ThresholdPercentage);
    }

    [Fact]
    public void Refuses_to_run_on_parameters_never_loaded()
    {
        TestSettings.Clear();
        Assert.Throws<InvalidOperationException>(() => TestSettings.Require());

        TestSettings.Use(FromServer());
        Assert.Equal(100, TestSettings.Require().PingDns.Count);
        TestSettings.Clear();
    }
}

/// <summary>The package speed comes from the customer's data, never typed.</summary>
public class PackageFromDataTests
{
    [Theory]
    [InlineData("20", "Home 50 Mbps", 20)] // the service's bandwidth wins
    [InlineData(null, "Dijinet  PAKET INTERNET UP TO 10 MBPS 1P 200", 10)]
    [InlineData("", "Dijinet  Paket 5Mbps", 5)]
    [InlineData(null, "Broadband 1 Gbps", 1000)]
    [InlineData(null, "Dijinet-grasi-1", 0)] // a version number, not a speed
    [InlineData(null, "pulo.net MM-Lite-1", 0)]
    [InlineData(null, "Dijinet  HomeNet Lite", 0)]
    [InlineData(null, null, 0)]
    public void Reads_the_speed_from_the_bandwidth_else_a_speed_written_in_the_name(string? bandwidthDown, string? package, int mbps) =>
        Assert.Equal(mbps, PackageSpeed.ForService(bandwidthDown, package));
}

/// <summary>What the charts show, from stored results.</summary>
public class ChartDataTests
{
    private static FoTestResult Result(string type, string target, object data, bool passed) => new()
    {
        TestType = type,
        Target = target,
        ResultData = JsonDocument.Parse(JsonSerializer.Serialize(data)),
        Status = passed ? TestStatus.Pass : TestStatus.Fail,
    };

    [Fact]
    public void Shows_each_pinged_targets_average_against_its_limit_and_no_reply_as_unmeasured()
    {
        var items = ChartData.Latency([
            Result(TestTypes.PingDns, "8.8.8.8", new { avg_ms = 12.4, threshold_ms = 35 }, true),
            Result(TestTypes.PingGateway, "10.0.0.1", new { avg_ms = (double?)null, threshold_ms = 10 }, false),
            Result(TestTypes.PingDomainLokal, "jinom.net", new { avg_ms = 9999, threshold_ms = 35 }, false), // the old stand-in
            Result(TestTypes.BrowsingTest, "kompas.com", new { }, true),
        ]);

        Assert.Equal(3, items.Count);
        Assert.Equal(new BulletItem("8.8.8.8", 12.4, 35, "ms", true), items[0]);
        Assert.Null(items[1].Value);
        Assert.Null(items[2].Value);
        Assert.False(items[2].Passed);
    }

    [Fact]
    public void Shows_download_against_the_package_and_a_pop_without_a_limit()
    {
        var items = ChartData.Download([
            Result(TestTypes.SpeedtestFast, "fast.com", new { download_mbps = 42.5, required_mbps = 42.5 }, true),
            Result(TestTypes.SpeedtestOokla, "speedtest.net", new { download_mbps = 0, required_mbps = (double?)null }, false),
        ]);

        Assert.Equal(new BulletItem("Fast.com", 42.5, 42.5, "Mbps", true, HigherIsBetter: true), items[0]);
        Assert.Equal("Ookla", items[1].Label);
        Assert.Null(items[1].Value); // 0 Mbps means nothing was measured
        Assert.Null(items[1].Limit);
    }

    [Fact]
    public void Reads_every_echo_of_a_ping_with_lost_ones_as_null()
    {
        var ping = Result(TestTypes.PingDns, "1.1.1.1", new { samples_ms = new long?[] { 8, null, 11 }, threshold_ms = 35 }, false);

        Assert.Equal([8, null, 11], ChartData.PingSamples(ping));
        Assert.Equal(35, ChartData.PingLimit(ping));
        // A result stored before the samples were kept has none.
        Assert.Null(ChartData.PingSamples(Result(TestTypes.PingDns, "1.1.1.1", new { avg_ms = 8 }, true)));
    }
}
