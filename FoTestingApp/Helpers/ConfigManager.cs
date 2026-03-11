using System.IO;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FoTestingApp.Helpers;

/// <summary>
/// Mengelola konfigurasi aplikasi dari appsettings.json.
/// Mendukung read dan write parameter tanpa rebuild.
/// </summary>
public static class ConfigManager
{
    private static IConfiguration? _config;
    private static string _configPath = string.Empty;

    public static void Initialize()
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        Reload();
    }

    private static void Reload()
    {
        _config = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
    }

    /// <summary>
    /// Simpan perubahan konfigurasi ke appsettings.json tanpa rebuild aplikasi.
    /// </summary>
    public static void SaveSettings(Dictionary<string, object> changes)
    {
        var json = File.ReadAllText(_configPath);
        var root = JsonNode.Parse(json)!.AsObject();

        foreach (var (keyPath, value) in changes)
        {
            var parts = keyPath.Split(':');
            var current = root;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (current[parts[i]] is not JsonObject nested)
                {
                    nested = new JsonObject();
                    current[parts[i]] = nested;
                }

                current = nested;
            }

            current[parts[^1]] = value switch
            {
                int intVal => JsonValue.Create(intVal),
                string[] arr => JsonNode.Parse(JsonSerializer.Serialize(arr)),
                _ => JsonValue.Create(value.ToString()),
            };
        }

        File.WriteAllText(_configPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Reload();
    }

    public static string? GetSetting(string key) => _config?[key];

    public static int GetSessionTimeoutMinutes() =>
        int.TryParse(_config!["App:SessionTimeoutMinutes"], out var v) ? v : 30;

    // Network Test configs
    public static string GetPingGatewayTarget() => _config!["NetworkTest:PingGateway:Target"] ?? string.Empty;
    public static int GetPingGatewayCount() => int.TryParse(_config!["NetworkTest:PingGateway:Count"], out var v) ? v : 100;
    public static int GetPingGatewayThresholdMs() => int.TryParse(_config!["NetworkTest:PingGateway:ThresholdAvgMs"], out var v) ? v : 10;

    public static string GetPingDnsTarget() => _config!["NetworkTest:PingDns:Target"] ?? "103.122.65.66";
    public static int GetPingDnsCount() => int.TryParse(_config!["NetworkTest:PingDns:Count"], out var v) ? v : 100;
    public static int GetPingDnsThresholdMs() => int.TryParse(_config!["NetworkTest:PingDns:ThresholdAvgMs"], out var v) ? v : 15;

    public static string[] GetNslookupNasionalDomains() =>
        _config!.GetSection("NetworkTest:NslookupNasional:Domains").Get<string[]>() ?? ["jinom.net", "kompas.com"];

    public static string[] GetNslookupInternasionalDomains() =>
        _config!.GetSection("NetworkTest:NslookupInternasional:Domains").Get<string[]>() ?? ["google.com", "facebook.com"];

    public static string GetPingDomainLokalTarget() => _config!["NetworkTest:PingDomainLokal:Target"] ?? "detik.com";
    public static int GetPingDomainLokalCount() => int.TryParse(_config!["NetworkTest:PingDomainLokal:Count"], out var v) ? v : 10;

    // App Test configs
    public static string[] GetBrowsingUrls() =>
        _config!.GetSection("AppTest:Browsing:Urls").Get<string[]>() ?? ["https://kompas.com", "https://jinom.net"];
    public static int GetBrowsingThresholdSeconds() => int.TryParse(_config!["AppTest:Browsing:ThresholdSeconds"], out var v) ? v : 10;

    public static string[] GetStreamingUrls() =>
        _config!.GetSection("AppTest:Streaming:Urls").Get<string[]>() ?? ["https://youtube.com", "https://tiktok.com"];

    public static string[] GetSocialMediaUrls() =>
        _config!.GetSection("AppTest:SocialMedia:Urls").Get<string[]>() ?? ["https://whatsapp.com", "https://facebook.com"];

    // Speedtest
    public static string GetJinomSpeedtestServer() => _config!["Speedtest:JinomServer"] ?? "https://speedtest.jinom.net";
    public static int GetSpeedtestThresholdPercentage() => int.TryParse(_config!["Speedtest:ThresholdPercentage"], out var v) ? v : 99;
}
