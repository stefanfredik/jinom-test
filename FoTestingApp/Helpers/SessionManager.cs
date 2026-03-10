using FoTestingApp.Models;

namespace FoTestingApp.Helpers;

/// <summary>
/// Menyimpan sesi login pengguna aktif sepanjang aplikasi berjalan.
/// </summary>
public static class SessionManager
{
    private static User? _currentUser;
    private static DateTime _lastActivityAt;

    public static User? CurrentUser => _currentUser;

    public static bool IsLoggedIn => _currentUser is not null;

    public static void Login(User user)
    {
        _currentUser = user;
        _lastActivityAt = DateTime.Now;
    }

    public static void Logout()
    {
        _currentUser = null;
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
