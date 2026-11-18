namespace FoTestingApp.Services.Core;

/// <summary>
/// Whether a measured download speed passes for the package the customer pays for: at least
/// <c>thresholdPercent</c> % of it (Speedtest:ThresholdPercentage). A site without a package
/// (a POP) has nothing to compare against, so there a measured speed is all that is asked.
/// </summary>
public static class SpeedVerdict
{
    /// <summary>The download a package needs to pass, in Mbps; null when there is no package.</summary>
    public static double? RequiredMbps(int packageMbps, int thresholdPercent) =>
        packageMbps > 0 ? packageMbps * Math.Clamp(thresholdPercent, 1, 100) / 100.0 : null;

    public static bool Passes(double downloadMbps, int packageMbps, int thresholdPercent)
    {
        if (downloadMbps <= 0)
        {
            return false;
        }

        var required = RequiredMbps(packageMbps, thresholdPercent);
        return required is null || downloadMbps >= required.Value;
    }
}
