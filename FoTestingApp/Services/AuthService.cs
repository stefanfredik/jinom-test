using FoTestingApp.Helpers;
using FoTestingApp.Models;
using Serilog;

namespace FoTestingApp.Services;

/// <summary>
/// Logs the technician in and out through jinom-assets' API (the app never reads a database).
/// </summary>
public class AuthService
{
    private readonly ApiService _api;

    public AuthService(ApiService api)
    {
        _api = api;
    }

    /// <summary>
    /// Logs in through jinom-assets: the user, or why not (wrong password, server unreachable, 2FA…).
    /// </summary>
    public async Task<LoginOutcome> LoginAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return new LoginOutcome(null, "Email dan password wajib diisi.");
        }

        return await _api.LoginAsync(email, password);
    }

    /// <summary>Revokes the token on the server, then ends the local session.</summary>
    public async Task LogoutAsync()
    {
        Log.Information("User logged out: {Email}", SessionManager.CurrentUser?.Email);
        await _api.LogoutAsync();
        SessionManager.Logout();
    }
}
