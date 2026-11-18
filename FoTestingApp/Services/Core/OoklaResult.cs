using System.Text.Json;

namespace FoTestingApp.Services.Core;

/// <summary>
/// The result of Ookla's speedtest CLI (<c>--format=json</c>). Bandwidth is reported in bytes per
/// second: Mbps = bytes × 8 / 1,000,000. The CLI may print log lines before the result, so the
/// line whose type is "result" is the one read.
/// </summary>
public sealed record OoklaResult(double DownloadMbps, double UploadMbps, double? PingMs, string? Server)
{
    public static OoklaResult? Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var type) && type.GetString() != "result")
                {
                    continue;
                }
                if (!root.TryGetProperty("download", out var download) || !root.TryGetProperty("upload", out var upload))
                {
                    continue;
                }

                double? ping = root.TryGetProperty("ping", out var p) && p.TryGetProperty("latency", out var latency) ? latency.GetDouble() : null;
                string? server = root.TryGetProperty("server", out var s)
                    ? string.Join(" — ", new[] { Str(s, "name"), Str(s, "location") }.Where(x => !string.IsNullOrWhiteSpace(x)))
                    : null;

                return new OoklaResult(
                    Math.Round(BytesPerSecondToMbps(download), 2),
                    Math.Round(BytesPerSecondToMbps(upload), 2),
                    ping is null ? null : Math.Round(ping.Value, 1),
                    string.IsNullOrWhiteSpace(server) ? null : server);
            }
            catch (JsonException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (KeyNotFoundException)
            {
            }
        }

        return null;
    }

    private static double BytesPerSecondToMbps(JsonElement section) =>
        section.GetProperty("bandwidth").GetDouble() * 8 / 1_000_000;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
