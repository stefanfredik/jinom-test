using System.Text.Json;
using FoTestingApp.Models;
using FoTestingApp.Services.Core;
using Serilog;

namespace FoTestingApp.Services;

/// <summary>
/// One run of the test package for a session: where results go, how it is cancelled, and what it
/// produced. Every result is kept here as it is made — the results screen shows what was measured,
/// not what happened to reach the server — and a result the API did not take is remembered, so the
/// technician is told instead of seeing a PASS for tests that were never saved.
/// </summary>
public sealed class TestRun(ApiService api, int sessionId, CancellationToken cancellation)
{
    private readonly List<FoTestResult> _results = [];
    private readonly List<string> _unsaved = [];

    public int SessionId { get; } = sessionId;
    public CancellationToken Cancellation { get; } = cancellation;

    public IReadOnlyList<FoTestResult> Results => _results;

    /// <summary>Tests whose result the API did not store (test names).</summary>
    public IReadOnlyList<string> Unsaved => _unsaved;

    public TestOverallStatus OverallStatus => RunVerdict.Overall(_results);

    /// <summary>A ping test began: its type, target, how many echoes and the limit in ms (for the live chart).</summary>
    public event Action<string, string, int, int>? PingStarted;

    /// <summary>One echo came back (its time in ms) or was lost (null).</summary>
    public event Action<long?>? PingReplied;

    public void ReportPingStarted(string testType, string target, int count, int thresholdMs) => PingStarted?.Invoke(testType, target, count, thresholdMs);

    public void ReportPingReply(long? rttMs) => PingReplied?.Invoke(rttMs);

    /// <summary>Keeps a result and sends it to the API; a failed send is remembered, not thrown.</summary>
    public async Task<FoTestResult> RecordAsync(string testType, string target, object data, bool passed)
    {
        var result = new FoTestResult
        {
            SessionId = SessionId,
            TestType = testType,
            Target = target,
            ResultData = JsonDocument.Parse(JsonSerializer.Serialize(data)),
            Status = passed ? TestStatus.Pass : TestStatus.Fail,
            CreatedAt = DateTime.Now,
        };
        _results.Add(result);

        try
        {
            // Not tied to the run's cancellation: a test that finished is saved even if the next is cancelled.
            await api.SaveTestResultAsync(result);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Result of {TestType} ({Target}) was not saved for session {SessionId}", testType, target, SessionId);
            _unsaved.Add($"{ResultFormatter.TestName(testType)} ({target})");
        }

        return result;
    }
}
