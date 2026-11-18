using System.IO;
using Microsoft.Extensions.Configuration;

namespace FoTestingApp.Helpers;

/// <summary>
/// What the app needs before it can reach Jinom Assets: the server's address and where logs go
/// (appsettings.json embedded in the .exe, overridable by a file beside it). Every test parameter
/// lives in Jinom Assets (Integrasi → Jinom Test) and is read after login: see TestSettings.
/// </summary>
public static class ConfigManager
{
    private static IConfiguration? _config;

    public static void Initialize()
    {
        var builder = new ConfigurationBuilder();

        var assembly = typeof(ConfigManager).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase));
        using var stream = resourceName is null ? null : assembly.GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            builder.AddJsonStream(stream);
        }

        // A file beside the .exe points one laptop at another server without a rebuild.
        var external = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (File.Exists(external))
        {
            builder.AddJsonFile(external, optional: true, reloadOnChange: false);
        }

        _config = builder.Build();
    }

    public static string? GetSetting(string key) => _config?[key];
}
