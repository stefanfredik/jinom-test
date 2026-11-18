using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Serilog;

namespace FoTestingApp.Helpers;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Light or dark, the way jinom-assets does it: the same M3 roles from the same seed, one scheme or
/// the other (Themes/Tokens.Light.xaml, Themes/Tokens.Dark.xaml). "System" follows Windows' app mode.
/// Every screen paints with DynamicResource, so swapping the scheme repaints it in place.
/// </summary>
public static class ThemeService
{
    private const string LightSource = "pack://application:,,,/Themes/Tokens.Light.xaml";
    private const string DarkSource = "pack://application:,,,/Themes/Tokens.Dark.xaml";

    public static ThemeMode Mode { get; private set; } = ThemeMode.System;

    public static bool IsDark { get; private set; }

    /// <summary>Raised after the scheme changed, for what cannot follow a DynamicResource.</summary>
    public static event Action? Changed;

    public static void ApplySaved()
    {
        Apply(Enum.TryParse<ThemeMode>(UserPreferences.Current.Theme, out var mode) ? mode : ThemeMode.System, save: false);
    }

    /// <summary>Light → dark → light; leaving "System" for an explicit choice.</summary>
    public static void Toggle() => Apply(IsDark ? ThemeMode.Light : ThemeMode.Dark);

    public static void Apply(ThemeMode mode, bool save = true)
    {
        Mode = mode;
        IsDark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => WindowsPrefersDark(),
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Tokens.", StringComparison.Ordinal) == true);
        var next = new ResourceDictionary { Source = new Uri(IsDark ? DarkSource : LightSource, UriKind.Absolute) };
        if (current is null)
        {
            dictionaries.Add(next);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(current)] = next;
        }

        // MaterialDesign's own base theme, for what its controls compute rather than read from a brush.
        var palette = new PaletteHelper();
        var theme = palette.GetTheme();
        theme.SetBaseTheme(IsDark ? BaseTheme.Dark : BaseTheme.Light);
        theme.SetPrimaryColor((Color)next["PrimaryColor"]);
        palette.SetTheme(theme);

        if (save)
        {
            UserPreferences.Current.Theme = mode.ToString();
            UserPreferences.Current.Save();
        }

        Changed?.Invoke();
    }

    private static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Windows' app theme could not be read");
            return false;
        }
    }
}
