namespace FoTestingApp.Models;

/// <summary>
/// Sesi pengujian FO (tabel fo_test_sessions).
/// </summary>
public class FoTestSession
{
    public int Id { get; set; }
    public string CertificationId { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int TechnicianId { get; set; }
    public DateTime TestDate { get; set; }
    public TestOverallStatus OverallStatus { get; set; } = TestOverallStatus.Fail;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Navigation property (opsional, diisi manual dari JOIN).</summary>
    public FoCustomer? Customer { get; set; }
    public User? Technician { get; set; }

    /// <summary>Hasil per-test dalam sesi ini.</summary>
    public List<FoTestResult> Results { get; set; } = [];
}

public enum TestOverallStatus
{
    Pass,
    Fail,
    Partial,
}
