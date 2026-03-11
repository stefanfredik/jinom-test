using FoTestingApp.Models;

namespace FoTestingApp.Helpers;

/// <summary>
/// Menyimpan sesi login pengguna aktif sepanjang aplikasi berjalan.
/// </summary>
public static class SessionManager
{
    private static User? _currentUser;
    private static string? _token;
    private static DateTime _lastActivityAt;

    public static User? CurrentUser => _currentUser;
    public static string? GetToken() => _token;

    public static bool IsLoggedIn => _currentUser is not null && !string.IsNullOrEmpty(_token);

    public static void Login(User user, string token)
    {
        _currentUser = user;
        _token = token;
        _lastActivityAt = DateTime.Now;
    }

    public static void Logout()
    {
        _currentUser = null;
        _token = null;
    }

    /// <summary>
    /// Perbarui timestamp aktivitas terakhir (dipanggil saat ada input pengguna).
    /// </summary>
    public static void RecordActivity()
    {
        _lastActivityAt = DateTime.Now;
    }

    /// <summary>
    /// Mengecek apakah sesi sudah kadaluarsa berdasarkan konfigurasi timeout.
    /// </summary>
    public static bool IsSessionExpired()
    {
        if (!IsLoggedIn) { return true; }

        var timeout = ConfigManager.GetSessionTimeoutMinutes();
        return DateTime.Now - _lastActivityAt > TimeSpan.FromMinutes(timeout);
    }
}
