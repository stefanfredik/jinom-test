using System.IO;
using System.Text.Json;
using Serilog;

namespace FoTestingApp.Helpers;

/// <summary>
/// What one technician chose on this laptop — the theme and the last email they logged in with —
/// kept in %LOCALAPPDATA%\JinomTest\preferences.json, apart from appsettings.json (which is the
/// admin's, and embedded in the .exe). Never a password or a token.
/// </summary>
public sealed class UserPreferences
{
    public string Theme { get; set; } = ThemeMode.System.ToString();
    public string? LastEmail { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JinomTest", "preferences.json");

    private static UserPreferences? _current;

    public static UserPreferences Current => _current ??= Load();

    private static UserPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(FilePath)) ?? new UserPreferences();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Preferences could not be read; using defaults");
        }

        return new UserPreferences();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Preferences could not be saved");
        }
    }
}
