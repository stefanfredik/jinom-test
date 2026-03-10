using System.Windows;
using System.Windows.Controls;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services;

namespace FoTestingApp.Views.Pages;

public partial class NewTestPage : Page
{
    private readonly DatabaseService _db = new();
    private int? _currentSessionId;
    private CancellationTokenSource? _searchCts;

    public NewTestPage()
    {
        InitializeComponent();
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
            var results = await _db.SearchCustomersAsync(query);

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
        CustomerNumberBox.Text = customer.CustomerNumber;
        FullNameBox.Text = customer.FullName;
        AddressBox.Text = customer.Address;
        PackageMbpsBox.Text = customer.PackageMbps.ToString();
        PhoneBox.Text = customer.Phone ?? string.Empty;

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

        // Simpan/update data pelanggan
        var customer = new FoCustomer
        {
            CustomerNumber = CustomerNumberBox.Text.Trim(),
            FullName = FullNameBox.Text.Trim(),
            Address = AddressBox.Text.Trim(),
            PackageMbps = int.TryParse(PackageMbpsBox.Text, out var pkg) ? pkg : 0,
            Phone = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim(),
            TechnicalNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
        };

        var customerId = await _db.UpsertCustomerAsync(customer);

        // Buat sesi pengujian baru
        var certId = $"CERT-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";
        var session = new FoTestSession
        {
            CertificationId = certId,
            CustomerId = customerId,
            TechnicianId = SessionManager.CurrentUser!.Id,
            TestDate = DateTime.Now,
            OverallStatus = TestOverallStatus.Fail,
        };

        _currentSessionId = await _db.CreateTestSessionAsync(session);

        // Jalankan semua network tests berurutan (akan diimplementasi Sprint 1)
        await RunAllTestsAsync(_currentSessionId.Value, customer.PackageMbps);

        StartTestBtn.IsEnabled = true;
        SaveReportBtn.IsEnabled = true;
    }

    private async System.Threading.Tasks.Task RunAllTestsAsync(int sessionId, int packageMbps)
    {
        var networkSvc = new NetworkTestService(_db, sessionId);
        var progress = new Progress<(int percent, string status)>(p =>
        {
            TestProgressBar.Value = p.percent;
            StatusLabel.Text = p.status;
        });

        await networkSvc.RunAllAsync(ResultsPanel, progress, packageMbps);
        await _db.UpdateSessionStatusAsync(sessionId, networkSvc.OverallStatus);
    }

    private bool ValidateForm()
    {
        if (string.IsNullOrWhiteSpace(CustomerNumberBox.Text) ||
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
