using System.Globalization;

namespace FoTestingApp.Services.Core;

/// <summary>
/// Times exchanged with jinom-assets. The server reads a time without an offset in its own zone
/// (config app.timezone, Asia/Makassar = WITA), and answers either an ISO time with an offset
/// (a datetime-cast column, e.g. test_date as "…Z") or a plain "yyyy-MM-dd HH:mm:ss" in its own
/// zone (start_time, end_time). A technician's laptop may be on WIB or WIT, so every time is
/// converted at this boundary — sent in the server's zone, shown in the laptop's.
///
/// Always formatted with the invariant culture: under id-ID the ':' of a format string becomes
/// '.', which would send "10.30.00".
/// </summary>
public static class ServerTime
{
    public const string WireFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>The server's zone; WITA (UTC+8, no daylight saving) when the id is unknown.</summary>
    public static TimeZoneInfo ResolveZone(string? id)
    {
        foreach (var candidate in new[] { id, "Asia/Makassar", "Singapore Standard Time" })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("WITA", TimeSpan.FromHours(8), "WITA", "WITA");
    }

    /// <summary>A local (or UTC) time as the server's wall clock, for a request.</summary>
    public static string ToWire(DateTime time, TimeZoneInfo serverZone)
    {
        var utc = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
        return TimeZoneInfo.ConvertTimeFromUtc(utc, serverZone).ToString(WireFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>A time from a response, as the laptop's local time; null when it cannot be read.</summary>
    public static DateTime? FromWire(string? value, TimeZoneInfo serverZone)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // An explicit offset or Z: an instant.
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset) && HasOffset(value))
        {
            return withOffset.ToLocalTime().DateTime;
        }

        // No offset: the server's wall clock.
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
        {
            var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, serverZone).ToLocalTime();
        }

        return null;
    }

    private static bool HasOffset(string value)
    {
        var tail = value.Length > 10 ? value[10..] : string.Empty;
        return tail.EndsWith('Z') || tail.Contains('+') || tail.LastIndexOf('-') > 0;
    }
}
