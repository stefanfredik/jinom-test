using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Xunit;

namespace FoTestingApp.Tests;

/// <summary>
/// The design contract (DESIGN.md), checked on the source: WPF resolves resource keys and icon
/// names only at run time, so a typo or an old key would otherwise surface as a crash or an
/// invisible control on the technician's laptop. Reads files; runs on any OS.
/// </summary>
public class UiContractTests
{
    private static readonly string AppDir = FindAppDir();

    private static string FindAppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "FoTestingApp");
            if (File.Exists(Path.Combine(candidate, "FoTestingApp.csproj"))) return candidate;
        }
        throw new InvalidOperationException("FoTestingApp folder not found above the test output.");
    }

    private static IEnumerable<string> Sources(string pattern, params string[] folders) =>
        folders.SelectMany(f => Directory.EnumerateFiles(Path.Combine(AppDir, f), pattern, SearchOption.AllDirectories));

    private static string Rel(string path) => Path.GetRelativePath(AppDir, path);

    private static IEnumerable<string> AllXaml() =>
        Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static HashSet<string> DefinedKeys() =>
        AllXaml().SelectMany(f => Regex.Matches(File.ReadAllText(f), "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value))
            .ToHashSet();

    /// <summary>Keys the code asks for: markup extensions in XAML, and string keys in C#.</summary>
    private static IEnumerable<(string File, string Key)> UsedKeys()
    {
        foreach (var f in AllXaml())
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{(?:StaticResource|DynamicResource)\s+([^}\s]+)\}"))
                yield return (Rel(f), m.Groups[1].Value);
        }

        foreach (var f in Sources("*.cs", "Views", "Helpers", "Services"))
        {
            var s = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(s, @"(?:SetResourceReference\([^,]+,\s*|FindResource\(|TryFindResource\()""([^""]+)"""))
                yield return (Rel(f), m.Groups[1].Value);
            // Brush keys handed around as strings (StatusVisual, Ui.Text, ...).
            foreach (Match m in Regex.Matches(s, @"""([A-Z][A-Za-z]+Brush)"""))
                yield return (Rel(f), m.Groups[1].Value);
            foreach (Match m in Regex.Matches(s, @"""(Text\.[A-Za-z]+|M3\.[A-Za-z]+)"""))
                yield return (Rel(f), m.Groups[1].Value);
        }
    }

    [Fact]
    public void Every_resource_key_used_is_defined()
    {
        var defined = DefinedKeys();
        var missing = UsedKeys()
            // MaterialDesignInXAML's own styles and brushes are defined by the library.
            .Where(u => !defined.Contains(u.Key) && !u.Key.StartsWith("MaterialDesign", StringComparison.Ordinal))
            .Select(u => $"{u.File}: {u.Key}")
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, "Undefined resource keys:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Light_and_dark_tokens_define_the_same_keys()
    {
        static HashSet<string> Keys(string file) =>
            Regex.Matches(File.ReadAllText(Path.Combine(AppDir, "Themes", file)), "x:Key=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToHashSet();

        var light = Keys("Tokens.Light.xaml");
        var dark = Keys("Tokens.Dark.xaml");

        Assert.Empty(light.Except(dark));
        Assert.Empty(dark.Except(light));
    }

    [Fact]
    public void Views_take_colour_type_and_elevation_from_the_theme()
    {
        var problems = new List<string>();
        foreach (var f in Sources("*.xaml", "Views"))
        {
            var s = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(s, "\"#[0-9A-Fa-f]{3,8}\"")) problems.Add($"{Rel(f)}: hex colour {m.Value} (use a token brush)");
            if (s.Contains("FontSize=\"")) problems.Add($"{Rel(f)}: FontSize (use a Text.* style)");
            if (Regex.IsMatch(s, "FontFamily=\"[^{]")) problems.Add($"{Rel(f)}: FontFamily (use a Text.* style)");
            if (s.Contains("DropShadowEffect") || s.Contains("ShadowDepth") || s.Contains("ElevationAssist"))
                problems.Add($"{Rel(f)}: shadow (only floating elements in Themes may have one)");
        }

        foreach (var f in Sources("*.cs", "Views", "Helpers"))
        {
            var s = File.ReadAllText(f);
            if (Regex.IsMatch(s, @"ColorConverter\.ConvertFromString|Color\.FromRgb|Color\.FromArgb|Brushes\.(?!Transparent\b)[A-Z]"))
                problems.Add($"{Rel(f)}: literal colour in code (use SetResourceReference with a token)");
            if (s.Contains("MessageBox.Show") && !f.EndsWith("Ui.cs", StringComparison.Ordinal))
                problems.Add($"{Rel(f)}: MessageBox (use Ui.ConfirmAsync / Ui.AlertAsync / Ui.Notify)");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Ui_text_has_no_emoji()
    {
        var emoji = new Regex(@"[☀-➿]|\uD83C[\uDF00-\uDFFF]|\uD83D[\uDC00-\uDEFF]|\uD83E[\uDD00-\uDEFF]");
        var found = Sources("*.xaml", "Views").Concat(Sources("*.cs", "Views", "Helpers", "Services"))
            .Where(f => emoji.IsMatch(File.ReadAllText(f)))
            .Select(Rel)
            .ToList();

        Assert.True(found.Count == 0, "Emoji in UI text (use a PackIcon):\n" + string.Join("\n", found));
    }

    [Fact]
    public void Every_icon_name_exists_in_the_icon_pack()
    {
        var kinds = PackIconKinds();
        var unknown = new List<string>();
        foreach (var f in AllXaml().Concat(Sources("*.cs", "Views", "Helpers", "Services")))
        {
            var s = File.ReadAllText(f);
            var names = Regex.Matches(s, @"(?:\bKind|LeadingIcon|TrailingIcon)=""([A-Za-z0-9]+)""").Select(m => m.Groups[1].Value)
                .Concat(Regex.Matches(s, @"PackIconKind\.([A-Za-z0-9]+)").Select(m => m.Groups[1].Value));
            unknown.AddRange(names.Where(n => !kinds.Contains(n)).Select(n => $"{Rel(f)}: {n}"));
        }

        Assert.True(unknown.Count == 0, "Unknown PackIconKind names:\n" + string.Join("\n", unknown.Distinct()));
    }

    /// <summary>
    /// The names of PackIconKind, read from the MaterialDesignThemes assembly the app restored
    /// (metadata only: the WPF assembly is not loaded, so this works off Windows too).
    /// </summary>
    private static HashSet<string> PackIconKinds()
    {
        var csproj = File.ReadAllText(Path.Combine(AppDir, "FoTestingApp.csproj"));
        var version = Regex.Match(csproj, "Include=\"MaterialDesignThemes\" Version=\"([^\"]+)\"").Groups[1].Value;
        var root = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var dll = Directory.EnumerateFiles(Path.Combine(root, "materialdesignthemes", version.ToLowerInvariant(), "lib"),
                "MaterialDesignThemes.Wpf.dll", SearchOption.AllDirectories)
            .OrderByDescending(p => p)
            .FirstOrDefault() ?? throw new InvalidOperationException("MaterialDesignThemes not restored: run dotnet restore on FoTestingApp first.");

        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            if (md.GetString(type.Name) != "PackIconKind") continue;
            foreach (var field in type.GetFields())
            {
                var name = md.GetString(md.GetFieldDefinition(field).Name);
                if (name != "value__") kinds.Add(name);
            }
        }

        Assert.True(kinds.Count > 1000, $"PackIconKind not found in {dll}");
        return kinds;
    }
}
