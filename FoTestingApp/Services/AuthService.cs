using FoTestingApp.Helpers;
using FoTestingApp.Models;
using Serilog;

namespace FoTestingApp.Services;

/// <summary>
/// Menangani autentikasi pengguna menggunakan email + BCrypt password
/// dari tabel `users` database OpenAccess.
/// </summary>
public class AuthService
{
    private readonly ApiService _api;

    public AuthService(ApiService api)
    {
        _api = api;
    }

    /// <summary>
    /// Verifikasi email dan password terhadap API OpenAccess.
    /// Mengembalikan User jika berhasil, null jika gagal.
    /// </summary>
    public async Task<User?> LoginAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var user = await _api.LoginAsync(email, password);
        return user;
    }

    public void Logout()
    {
        Log.Information("User logged out: {Email}", SessionManager.CurrentUser?.Email);
        SessionManager.Logout();
    }
}
