using System.Globalization;
using System.Text.RegularExpressions;

namespace FoTestingApp.Services.Core;

/// <summary>
/// A package's download speed in Mbps from how billing writes it: "50 Mbps", "Jinom 100M",
/// "1 Gbps", "1,5 Gbps", "1.000 Mbps" (Indonesian thousands separator) or a bare "20".
/// 0 when no speed can be read.
/// </summary>
public static partial class PackageSpeed
{
    [GeneratedRegex(@"(\d+(?:[.,]\d+)*)\s*(gbps|gb|g|mbps|mb|m|kbps|kb|k)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WithUnit();

    [GeneratedRegex(@"\d+(?:[.,]\d+)*")]
    private static partial Regex AnyNumber();

    public static int ParseMbps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var match = WithUnit().Match(text);
        if (match.Success && TryNumber(match.Groups[1].Value, out var value))
        {
            var unit = match.Groups[2].Value.ToLowerInvariant();
            var mbps = unit[0] switch
            {
                'g' => value * 1000,
                'k' => value / 1000,
                _ => value,
            };
            return (int)Math.Round(mbps, MidpointRounding.AwayFromZero);
        }

        var number = AnyNumber().Match(text);
        return number.Success && TryNumber(number.Value, out var bare) ? (int)Math.Round(bare, MidpointRounding.AwayFromZero) : 0;
    }

    /// <summary>
    /// The speed written in a package's name ("PAKET INTERNET UP TO 10 MBPS", "Broadband 1 Gbps"):
    /// only a number with its unit counts. A bare number in a name is a version or a price
    /// ("Dijinet-grasi-1", "MM-Lite-1", "1P 200"), never a speed: 0 then.
    /// </summary>
    public static int ParseNameMbps(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

        var match = WithUnit().Match(name);
        if (!match.Success || !TryNumber(match.Groups[1].Value, out var value))
        {
            return 0;
        }

        var unit = match.Groups[2].Value.ToLowerInvariant();
        var mbps = unit[0] switch { 'g' => value * 1000, 'k' => value / 1000, _ => value };
        return (int)Math.Round(mbps, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The customer's package speed: the service's download bandwidth as billing records it, else the
    /// speed written in the package's name; 0 when neither says.
    /// </summary>
    public static int ForService(string? bandwidthDown, string? packageName)
    {
        var fromBandwidth = ParseMbps(bandwidthDown);
        return fromBandwidth > 0 ? fromBandwidth : ParseNameMbps(packageName);
    }

    /// <summary>
    /// "1.000" (thousands) is 1000, "1,5" or "1.5" (one decimal part of one or two digits) is 1.5.
    /// </summary>
    private static bool TryNumber(string raw, out double value)
    {
        var groups = raw.Split('.', ',');
        var isThousands = groups.Length > 1 && groups.Skip(1).All(g => g.Length == 3);
        var normalised = isThousands
            ? string.Concat(groups)
            : groups.Length == 2 ? $"{groups[0]}.{groups[1]}" : string.Concat(groups);

        return double.TryParse(normalised, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
