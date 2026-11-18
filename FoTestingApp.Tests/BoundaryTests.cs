using System.Globalization;
using System.Text.Json;
using FoTestingApp.Models;
using FoTestingApp.Services.Core;
using Xunit;

namespace FoTestingApp.Tests;

public class ServerTimeTests
{
    private static readonly TimeZoneInfo Wita = ServerTime.ResolveZone("Asia/Makassar");

    [Fact]
    public void Sends_a_time_as_the_servers_wall_clock_with_invariant_separators()
    {
        var utc = new DateTime(2026, 11, 18, 2, 30, 0, DateTimeKind.Utc);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // id-ID turns the ':' of a format into '.'; the wire format must not follow it.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("id-ID");
            Assert.Equal("2026-11-18 10:30:00", ServerTime.ToWire(utc, Wita));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Reads_an_instant_and_a_server_wall_clock_as_the_same_moment()
    {
        var fromInstant = ServerTime.FromWire("2026-11-18T02:30:00.000000Z", Wita);
        var fromWallClock = ServerTime.FromWire("2026-11-18 10:30:00", Wita);

        Assert.NotNull(fromInstant);
        Assert.Equal(fromInstant, fromWallClock);
        Assert.Equal(new DateTime(2026, 11, 18, 2, 30, 0, DateTimeKind.Utc), fromInstant!.Value.ToUniversalTime());
    }

    [Fact]
    public void Round_trips_a_local_time()
    {
        var local = new DateTime(2026, 11, 18, 9, 15, 0, DateTimeKind.Local);
        Assert.Equal(local, ServerTime.FromWire(ServerTime.ToWire(local, Wita), Wita));
    }

    [Fact]
    public void Is_null_for_nothing_or_garbage()
    {
        Assert.Null(ServerTime.FromWire(null, Wita));
        Assert.Null(ServerTime.FromWire("bukan tanggal", Wita));
    }
}

public class ResultFormatterTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Shows_a_ping_that_never_answered_as_no_reply_new_and_old_data_alike()
    {
        var current = ResultFormatter.Rows(TestTypes.PingDns, Json("""{"ping_count":100,"avg_ms":null,"min_ms":null,"max_ms":null,"rto":100}"""));
        var legacy = ResultFormatter.Rows(TestTypes.PingDns, Json("""{"ping_count":100,"avg_ms":9999,"min_ms":9999,"max_ms":9999,"rto":100}"""));

        Assert.Equal("Tidak ada balasan", current.Single(r => r.Label == "Rata-rata").Value);
        Assert.Equal("Tidak ada balasan", legacy.Single(r => r.Label == "Rata-rata").Value);
        Assert.Equal("100 dari 100 (100%)", current.Single(r => r.Label == "RTO").Value);
    }

    [Fact]
    public void Shows_a_page_that_failed_as_failed_not_as_minus_one_second()
    {
        var rows = ResultFormatter.Rows(TestTypes.BrowsingTest, Json("""{"results":{"https://kompas.com":-1,"https://jinom.net":2.345}}"""));

        Assert.Equal("Gagal dimuat", rows.Single(r => r.Label == "kompas.com").Value);
        Assert.Equal("2,3 detik", rows.Single(r => r.Label == "jinom.net").Value);
    }

    [Fact]
    public void Names_the_speed_a_package_needed()
    {
        var rows = ResultFormatter.Rows(TestTypes.SpeedtestOokla,
            Json("""{"download_mbps":40.25,"upload_mbps":20,"package_mbps":50,"required_mbps":42.5,"threshold_pct":85,"ping_ms":4.4,"server":"Jinom"}"""));

        Assert.Equal("40,3 Mbps", rows.Single(r => r.Label == "Download").Value);
        Assert.Equal("42,5 Mbps (85% dari paket 50 Mbps)", rows.Single(r => r.Label == "Minimal lulus").Value);
        Assert.Equal("4,4 ms", rows.Single(r => r.Label == "Ping").Value);
    }

    [Fact]
    public void Leads_a_failed_summary_with_its_reason()
    {
        var data = Json("""{"download_mbps":0,"upload_mbps":0,"error":"Speedtest timeout"}""");

        Assert.Equal("Gagal: Speedtest timeout", ResultFormatter.Summary(TestTypes.SpeedtestOokla, data, passed: false));
    }

    [Fact]
    public void Reads_data_stored_as_a_json_string()
    {
        var wrapped = Json(JsonSerializer.Serialize("""{"download_mbps":10,"upload_mbps":5}"""));

        Assert.Equal("↓ 10,0 Mbps | ↑ 5,0 Mbps", ResultFormatter.Summary(TestTypes.SpeedtestFast, wrapped, passed: true));
    }
}

public class TolerantEnumConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new TolerantEnumConverter<TestOverallStatus>(TestOverallStatus.Fail) },
    };

    [Theory]
    [InlineData("\"PASS\"", TestOverallStatus.Pass)]
    [InlineData("\"partial\"", TestOverallStatus.Partial)]
    [InlineData("\"CANCELLED\"", TestOverallStatus.Fail)]
    public void Reads_any_case_and_falls_back_on_an_unknown_status(string json, TestOverallStatus expected) =>
        Assert.Equal(expected, JsonSerializer.Deserialize<TestOverallStatus>(json, Options));
}
