namespace FoTestingApp.Services.Core;

/// <summary>
/// The test parameters, as set in Jinom Assets under Integrasi → Jinom Test and read from
/// GET reseller-certification/fo-test/settings after login and before every run. The app keeps no
/// parameters of its own: what is not loaded is not guessed (<see cref="Require"/> throws), so a
/// test never runs against values nobody set.
///
/// Property names map to the server's snake_case keys (ApiService's naming policy).
/// </summary>
public sealed class TestSettings
{
    public int SessionTimeoutMinutes { get; set; }
    public int PingIntervalMs { get; set; }
    public PingSettings PingGateway { get; set; } = new();
    public PingSettings PingDns { get; set; } = new();
    public PingSettings PingDomainLokal { get; set; } = new();
    public LookupSettings NslookupNasional { get; set; } = new();
    public LookupSettings NslookupInternasional { get; set; } = new();
    public PageSettings Browsing { get; set; } = new();
    public PageSettings Streaming { get; set; } = new();
    public PageSettings SocialMedia { get; set; } = new();
    public SpeedtestSettings Speedtest { get; set; } = new();
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>The zone the server reads and writes times without an offset in (its app.timezone).</summary>
    public string ServerTimezone { get; set; } = string.Empty;

    /// <summary>When a superadmin last saved them (ISO 8601), null while they are the defaults.</summary>
    public string? UpdatedAt { get; set; }

    /// <summary>The parameters loaded last; null until the first load after login.</summary>
    public static TestSettings? Current { get; private set; }

    public static void Use(TestSettings settings) => Current = settings.Normalized();

    public static void Clear() => Current = null;

    public static TestSettings Require() =>
        Current ?? throw new InvalidOperationException("Pengaturan pengujian belum dimuat dari Jinom Assets.");

    /// <summary>
    /// The same parameters within what the tests can work with — the server validates them, this
    /// only keeps a value an older server sends from hanging or skipping a test: counts 1–1000,
    /// limits at least 1, lists without blanks or repeats.
    /// </summary>
    public TestSettings Normalized()
    {
        static string[] Clean(string[]? items) =>
            (items ?? []).Select(i => i.Trim()).Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        static PingSettings Ping(PingSettings p) => new()
        {
            Target = p.Target?.Trim() ?? string.Empty,
            Targets = Clean(p.Targets),
            Domains = Clean(p.Domains),
            Count = Math.Clamp(p.Count, 1, 1000),
            ThresholdAvgMs = Math.Max(1, p.ThresholdAvgMs),
            MaxRto = Math.Max(0, p.MaxRto),
        };

        static LookupSettings Lookup(LookupSettings l) => new() { Domains = Clean(l.Domains), TimeoutSeconds = Math.Clamp(l.TimeoutSeconds, 1, 60) };

        static PageSettings Page(PageSettings p) => new() { Urls = Clean(p.Urls), ThresholdSeconds = Math.Clamp(p.ThresholdSeconds, 1, 300) };

        return new TestSettings
        {
            SessionTimeoutMinutes = Math.Clamp(SessionTimeoutMinutes, 5, 480),
            PingIntervalMs = Math.Clamp(PingIntervalMs, 0, 5000),
            PingGateway = Ping(PingGateway),
            PingDns = Ping(PingDns),
            PingDomainLokal = Ping(PingDomainLokal),
            NslookupNasional = Lookup(NslookupNasional),
            NslookupInternasional = Lookup(NslookupInternasional),
            Browsing = Page(Browsing),
            Streaming = Page(Streaming),
            SocialMedia = Page(SocialMedia),
            Speedtest = new SpeedtestSettings
            {
                ThresholdPercentage = Math.Clamp(Speedtest.ThresholdPercentage, 1, 100),
                OoklaCliUrl = Speedtest.OoklaCliUrl?.Trim() ?? string.Empty,
            },
            UserAgent = UserAgent?.Trim() ?? string.Empty,
            ServerTimezone = ServerTimezone?.Trim() ?? string.Empty,
            UpdatedAt = UpdatedAt,
        };
    }
}

/// <summary>A ping test: the gateway (Target, empty = detect), DNS servers (Targets) or domains (Domains).</summary>
public sealed class PingSettings
{
    public string Target { get; set; } = string.Empty;
    public string[] Targets { get; set; } = [];
    public string[] Domains { get; set; } = [];
    public int Count { get; set; }
    public int ThresholdAvgMs { get; set; }
    public int MaxRto { get; set; }
}

public sealed class LookupSettings
{
    public string[] Domains { get; set; } = [];
    public int TimeoutSeconds { get; set; }
}

public sealed class PageSettings
{
    public string[] Urls { get; set; } = [];
    public int ThresholdSeconds { get; set; }
}

public sealed class SpeedtestSettings
{
    public int ThresholdPercentage { get; set; }
    public string OoklaCliUrl { get; set; } = string.Empty;
}
