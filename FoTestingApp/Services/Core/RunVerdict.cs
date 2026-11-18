using FoTestingApp.Models;

namespace FoTestingApp.Services.Core;

/// <summary>
/// A session's overall status from its tests: PASS only when there were tests and all passed.
/// A run without a single test is FAIL, never a vacuous PASS (0 of 0).
/// </summary>
public static class RunVerdict
{
    public static TestOverallStatus Overall(int passCount, int totalCount) =>
        totalCount <= 0 ? TestOverallStatus.Fail :
        passCount >= totalCount ? TestOverallStatus.Pass :
        passCount > 0 ? TestOverallStatus.Partial :
        TestOverallStatus.Fail;

    public static TestOverallStatus Overall(IEnumerable<FoTestResult> results)
    {
        var list = results as IReadOnlyCollection<FoTestResult> ?? results.ToList();
        return Overall(list.Count(r => r.Status == TestStatus.Pass), list.Count);
    }
}
