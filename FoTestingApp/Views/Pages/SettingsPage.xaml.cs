using System.Windows;
using System.Windows.Controls;
using FoTestingApp.Helpers;

namespace FoTestingApp.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        DnsTargetBox.Text = string.Join(", ", ConfigManager.GetPingDnsTargets());
        PingGwCountBox.Text = ConfigManager.GetPingGatewayCount().ToString();
        PingDnsCountBox.Text = ConfigManager.GetPingDnsCount().ToString();
        PingLocalTargetBox.Text = string.Join(", ", ConfigManager.GetPingDomainLokalDomains());
        NslookupNasionalBox.Text = string.Join(", ", ConfigManager.GetNslookupNasionalDomains());
        NslookupInternasionalBox.Text = string.Join(", ", ConfigManager.GetNslookupInternasionalDomains());
        GatewayTargetBox.Text = ConfigManager.GetPingGatewayTarget();
        SessionTimeoutBox.Text = ConfigManager.GetSessionTimeoutMinutes().ToString();
    }

    private static string[] List(TextBox box) =>
        box.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A whole number within [min, max], or the reason it is not — nothing is silently replaced.</summary>
    private static int? Number(TextBox box, string name, int min, int max, List<string> errors)
    {
        if (int.TryParse(box.Text.Trim(), out var value) && value >= min && value <= max)
        {
            return value;
        }

        errors.Add($"{name} harus angka {min}–{max}.");
        return null;
    }

    private void SaveSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        var dnsTargets = List(DnsTargetBox);
        var lokalDomains = List(PingLocalTargetBox);
        var nasionalDomains = List(NslookupNasionalBox);
        var internasionalDomains = List(NslookupInternasionalBox);
        var gateway = GatewayTargetBox.Text.Trim();

        var gwCount = Number(PingGwCountBox, "Jumlah ping gateway", 1, 1000, errors);
        var dnsCount = Number(PingDnsCountBox, "Jumlah ping DNS", 1, 1000, errors);
        var timeout = Number(SessionTimeoutBox, "Timeout sesi", 5, 480, errors);

        if (dnsTargets.Length == 0) errors.Add("IP DNS target wajib diisi.");
        if (lokalDomains.Length == 0) errors.Add("Domain ping lokal wajib diisi.");
        if (nasionalDomains.Length == 0) errors.Add("Domain NSLookup nasional wajib diisi.");
        if (internasionalDomains.Length == 0) errors.Add("Domain NSLookup internasional wajib diisi.");
        if (gateway.Length > 0 && !System.Net.IPAddress.TryParse(gateway, out _)) errors.Add("IP gateway tidak valid (kosongkan untuk deteksi otomatis).");

        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Pengaturan belum disimpan", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            ConfigManager.SaveSettings(new Dictionary<string, object>
            {
                ["NetworkTest:PingDns:Targets"] = dnsTargets,
                ["NetworkTest:PingGateway:Target"] = gateway,
                ["NetworkTest:PingGateway:Count"] = gwCount!.Value,
                ["NetworkTest:PingDns:Count"] = dnsCount!.Value,
                ["NetworkTest:PingDomainLokal:Domains"] = lokalDomains,
                ["NetworkTest:NslookupNasional:Domains"] = nasionalDomains,
                ["NetworkTest:NslookupInternasional:Domains"] = internasionalDomains,
                ["App:SessionTimeoutMinutes"] = timeout!.Value,
            });
            MessageBox.Show("Pengaturan berhasil disimpan. Perubahan aktif untuk sesi berikutnya.",
                "Berhasil", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal menyimpan pengaturan: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
