using System.Text.Json;

namespace FoTestingApp.Models;

/// <summary>
/// Hasil satu jenis pengujian dalam sebuah sesi (tabel fo_test_results).
/// </summary>
public class FoTestResult
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string TestType { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public JsonDocument? ResultData { get; set; }
    public TestStatus Status { get; set; } = TestStatus.Fail;
    public DateTime CreatedAt { get; set; }
}

public enum TestStatus
{
    Pass,
    Fail,
}

/// <summary>Konstanta untuk nilai test_type di tabel fo_test_results.</summary>
public static class TestTypes
{
    public const string PingGateway = "ping_gateway";
    public const string PingDns = "ping_dns";
    public const string NslookupNasional = "nslookup_nasional";
    public const string NslookupInternasional = "nslookup_internasional";
    public const string PingDomainLokal = "ping_domain_lokal";
    public const string BrowsingTest = "browsing_test";
    public const string StreamingTest = "streaming_test";
    public const string SocialMediaTest = "social_media_test";
    public const string SpeedtestJinom = "speedtest_jinom";
    public const string SpeedtestOokla = "speedtest_ookla";
}
