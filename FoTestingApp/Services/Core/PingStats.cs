namespace FoTestingApp.Services.Core;

/// <summary>
/// What a run of pings measured: how many were sent and answered, how many timed out (RTO),
/// and the latency of the answered ones. When nothing answered there is no latency at all
/// (null), never a stand-in number that would read as a measurement.
/// </summary>
public sealed record PingStats(int Sent, int Received, double? AvgMs, long? MinMs, long? MaxMs)
{
    /// <summary>Echo requests that got no reply (Request Time Out).</summary>
    public int Rto => Sent - Received;

    /// <summary>Lost replies as a share of those sent, 0–100.</summary>
    public double LossPercent => Sent == 0 ? 0 : Rto * 100.0 / Sent;

    public static PingStats From(int sent, IReadOnlyCollection<long> latenciesMs)
    {
        if (latenciesMs.Count == 0)
        {
            return new PingStats(sent, 0, null, null, null);
        }

        return new PingStats(sent, latenciesMs.Count, latenciesMs.Average(), latenciesMs.Min(), latenciesMs.Max());
    }

    /// <summary>
    /// Pass when something answered, the average is within the threshold — compared unrounded, so
    /// 10.9 ms does not pass a 10 ms limit — and no more replies were lost than allowed.
    /// </summary>
    public bool Passes(int thresholdAvgMs, int maxRto) =>
        Received > 0 && AvgMs <= thresholdAvgMs && Rto <= maxRto;
}
