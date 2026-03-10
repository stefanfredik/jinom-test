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
    private readonly DatabaseService _db;

    public AuthService(DatabaseService db)
    {
        _db = db;
    }

    /// <summary>
    /// Verifikasi email dan password terhadap database OpenAccess.
    /// Mengembalikan User jika berhasil, null jika gagal.
    /// </summary>
    public async Task<User?> LoginAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var user = await _db.GetUserByEmailAsync(email.Trim().ToLower());
        if (user is null)
        {
            Log.Warning("Login failed — email not found: {Email}", email);
            return null;
        }

        // Verifikasi password BCrypt (12 rounds, sesuai OpenAccess .env BCRYPT_ROUNDS=12)
        var isValid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        if (!isValid)
        {
            Log.Warning("Login failed — wrong password for: {Email}", email);
            return null;
        }

        Log.Information("Login successful: {Email} (ID={Id})", email, user.Id);
        SessionManager.Login(user);
        return user;
    }

    public void Logout()
    {
        Log.Information("User logged out: {Email}", SessionManager.CurrentUser?.Email);
        SessionManager.Logout();
    }
}
