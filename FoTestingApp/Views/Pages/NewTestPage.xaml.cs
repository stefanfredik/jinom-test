using System.Windows;
using System.Windows.Controls;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services;

namespace FoTestingApp.Views.Pages;

public partial class NewTestPage : Page
{
    private readonly ApiService _api = new();
    private int? _currentSessionId;
    private CancellationTokenSource? _searchCts;

    private readonly string _testMode;

    public NewTestPage(string testMode = "customer")
    {
        InitializeComponent();
        _testMode = testMode;
        ApplyTestMode();
    }

    private void ApplyTestMode()
    {
        if (_testMode == "pop")
        {
            SiteIdBox.Visibility = Visibility.Collapsed;
            MaterialDesignThemes.Wpf.HintAssist.SetHint(PackageMbpsBox, "Paket Langganan JNIX (Mbps) *");
        }
        else
        {
            SiteIdBox.Visibility = Visibility.Visible;
            MaterialDesignThemes.Wpf.HintAssist.SetHint(PackageMbpsBox, "Paket Layanan (Mbps) *");
        }
    }

    // ── Customer Search ───────────────────────────────────────────────────────

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;

        var query = SearchBox.Text.Trim();
        if (query.Length < 2)
        {
            SearchDropdown.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            // Debounce 300ms
            await Task.Delay(300, cts.Token);
            var results = await _api.SearchCustomersAsync(query);

            if (!cts.IsCancellationRequested)
            {
                SearchDropdown.ItemsSource = results;
                SearchDropdown.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        catch (TaskCanceledException) { /* normal debounce cancel */ }
    }

    private void SearchDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchDropdown.SelectedItem is not FoCustomer customer) { return; }

        // Auto-fill semua field dari pelanggan yang dipilih
        SiteIdBox.Text = customer.SiteId ?? string.Empty;
        FullNameBox.Text = customer.FullName;
        AddressBox.Text = customer.Address;
        PackageMbpsBox.Text = customer.PackageMbps.ToString();

        SearchDropdown.Visibility = Visibility.Collapsed;
        SearchBox.Text = customer.FullName;
    }

    private async void StartTestBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateForm()) { return; }

        StartTestBtn.IsEnabled = false;
        SaveReportBtn.IsEnabled = false;
        ResultsPanel.Children.Clear();
        TestProgressBar.Value = 0;

        try
        {
            // Simpan/update data pelanggan
            var isPopMode = _testMode == "pop";
            var customer = new FoCustomer
            {
                SiteType = isPopMode ? "pop" : "customer",
                SiteId = isPopMode ? null : string.IsNullOrWhiteSpace(SiteIdBox.Text) ? null : SiteIdBox.Text.Trim(),
                FullName = FullNameBox.Text.Trim(),
                Address = AddressBox.Text.Trim(),
                PackageMbps = int.TryParse(PackageMbpsBox.Text, out var pkg) ? pkg : 0,
                TechnicalNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
            };

            var customerId = await _api.UpsertCustomerAsync(customer);

            // Buat sesi pengujian baru
            var certId = $"CERT-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";
            var session = new FoTestSession
            {
                CertificationId = certId,
                CustomerId = customerId,
                TechnicianId = SessionManager.CurrentUser?.Id ?? 0,
                TestDate = DateTime.Now,
                StartTime = DateTime.Now,
                OverallStatus = TestOverallStatus.Fail,
            };

            _currentSessionId = await _api.CreateTestSessionAsync(session);

            // Jalankan semua network tests berurutan
            await RunAllTestsAsync(_currentSessionId.Value, customer.PackageMbps);

            SaveReportBtn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Test execution failed");
            MessageBox.Show($"Pengujian gagal: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusLabel.Text = "❌ Pengujian gagal. Silakan coba lagi.";
        }
        finally
        {
            StartTestBtn.IsEnabled = true;
        }
    }

    private async System.Threading.Tasks.Task RunAllTestsAsync(int sessionId, int packageMbps)
    {
        var networkSvc = new NetworkTestService(_api, sessionId);
        var progress = new Progress<(int percent, string status)>(p =>
        {
            TestProgressBar.Value = p.percent;
            StatusLabel.Text = p.status;
        });

        await networkSvc.RunAllAsync(ResultsPanel, progress, packageMbps);

        try
        {
            await _api.UpdateSessionStatusAsync(sessionId, networkSvc.OverallStatus, DateTime.Now);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to update session status");
        }
    }

    private bool ValidateForm()
    {
        var isPopMode = _testMode == "pop";
        if ((!isPopMode && string.IsNullOrWhiteSpace(SiteIdBox.Text)) ||
            string.IsNullOrWhiteSpace(FullNameBox.Text) ||
            string.IsNullOrWhiteSpace(AddressBox.Text) ||
            string.IsNullOrWhiteSpace(PackageMbpsBox.Text))
        {
            MessageBox.Show("Field bertanda * wajib diisi.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(PackageMbpsBox.Text, out var pkg) || pkg <= 0)
        {
            MessageBox.Show("Paket layanan harus berupa angka positif (Mbps).", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void SaveReportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSessionId.HasValue)
        {
            // Navigasi ke halaman laporan dengan session ID
            NavigationService?.Navigate(new ReportPage(_currentSessionId.Value));
        }
    }
}
