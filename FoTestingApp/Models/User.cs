namespace FoTestingApp.Models;

/// <summary>
/// Merepresentasikan user dari tabel `users` di OpenAccess.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
}
