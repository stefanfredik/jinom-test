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
        DnsTargetBox.Text = ConfigManager.GetPingDnsTarget();
        PingGwCountBox.Text = ConfigManager.GetPingGatewayCount().ToString();
        PingDnsCountBox.Text = ConfigManager.GetPingDnsCount().ToString();
        PingLocalTargetBox.Text = string.Join(", ", ConfigManager.GetPingDomainLokalDomains());
        NslookupNasionalBox.Text = string.Join(", ", ConfigManager.GetNslookupNasionalDomains());
        NslookupInternasionalBox.Text = string.Join(", ", ConfigManager.GetNslookupInternasionalDomains());
        SessionTimeoutBox.Text = ConfigManager.GetSessionTimeoutMinutes().ToString();
    }

    private void SaveSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var lokalDomains = PingLocalTargetBox.Text
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var nasionalDomains = NslookupNasionalBox.Text
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var internasionalDomains = NslookupInternasionalBox.Text
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            var changes = new Dictionary<string, object>
            {
                ["NetworkTest:PingDns:Target"] = DnsTargetBox.Text.Trim(),
                ["NetworkTest:PingGateway:Count"] = int.TryParse(PingGwCountBox.Text, out var gwc) ? gwc : 100,
                ["NetworkTest:PingDns:Count"] = int.TryParse(PingDnsCountBox.Text, out var dnsc) ? dnsc : 100,
                ["NetworkTest:PingDomainLokal:Domains"] = lokalDomains,
                ["NetworkTest:NslookupNasional:Domains"] = nasionalDomains,
                ["NetworkTest:NslookupInternasional:Domains"] = internasionalDomains,
                ["App:SessionTimeoutMinutes"] = int.TryParse(SessionTimeoutBox.Text, out var to) ? to : 30,
            };

            ConfigManager.SaveSettings(changes);
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
