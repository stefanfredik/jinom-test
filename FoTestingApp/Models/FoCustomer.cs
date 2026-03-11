namespace FoTestingApp.Models;

/// <summary>
/// Data pelanggan sesi pengujian FO (tabel fo_test_customers).
/// </summary>
public class FoCustomer
{
    public int Id { get; set; }
    public string SiteType { get; set; } = "customer";
    public string? SiteId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int PackageMbps { get; set; }
    public string? Email { get; set; }
    public string? TechnicalNotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
