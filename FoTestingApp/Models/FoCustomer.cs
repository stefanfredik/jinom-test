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

    public string? PackageName { get; set; }

    /// <summary>The customer's service number (nomor layanan), for display; site_id is their subscriber id.</summary>
    public string? ServiceNumber { get; set; }
    /// <summary>The package for a list: "-" for a site without one (a POP), never "0 Mbps".</summary>
    public string PackageText => PackageMbps > 0 ? $"{PackageMbps} Mbps" : "-";

    public string DisplayName => string.IsNullOrWhiteSpace(PackageName) ? FullName : $"{FullName} - {PackageName}";
}
