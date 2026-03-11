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
            var results = await _api.SearchCustomersAsync(query, _testMode);

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

        FormContainer.Visibility = Visibility.Collapsed;
        ProgressContainer.Visibility = Visibility.Visible;
        DiagnosticLogsPanel.Children.Clear();
        MainCircularProgress.Value = 0;
        ProgressPercentText.Text = "0";
        CurrentActivityText.Text = "Preparing Tests...";
        TaskCountText.Text = "Task 1 of 7";
        SubTaskProgressBar.Value = 0;
        SubTaskDetailText.Text = "Initializing diagnostic engine...";
        SaveReportBtn.Visibility = Visibility.Collapsed;
        EndTestBtnText.Text = "CANCEL DIAGNOSTICS";

        try
        {
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

            await RunAllTestsAsync(_currentSessionId.Value, customer.PackageMbps);

            SaveReportBtn.Visibility = Visibility.Visible;
            EndTestBtnText.Text = "CLOSE DIAGNOSTICS";
            EndTestBtn.Foreground = (System.Windows.Media.Brush)FindResource("TextDarkBrush");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Test execution failed");
            CurrentActivityText.Text = "Diagnostics Failed";
            EndTestBtnText.Text = "CLOSE DIAGNOSTICS";
        }
    }

    private async System.Threading.Tasks.Task RunAllTestsAsync(int sessionId, int packageMbps)
    {
        var networkSvc = new NetworkTestService(_api, sessionId);
        var progress = new Progress<(int percent, string status)>(p =>
        {
            MainCircularProgress.Value = p.percent;
            ProgressPercentText.Text = p.percent.ToString();
            
            // Extract task count and subtask from the status string if passed in a specific format
            // like "Task 1 of 7|Running Speedtest|Downloading..."
            var parts = p.status.Split('|');
            if (parts.Length == 3)
            {
                TaskCountText.Text = parts[0];
                CurrentActivityText.Text = parts[1];
                SubTaskDetailText.Text = parts[2];
                SubTaskProgressBar.Value = p.percent; // Keep it simple by syncing to overall percent for now
            }
            else
            {
                SubTaskDetailText.Text = p.status;
            }
        });

        // Pass DiagnosticLogsPanel instead of ResultsPanel so services can append their UI logs
        await networkSvc.RunAllAsync(DiagnosticLogsPanel, progress, packageMbps);

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
            NavigationService?.Navigate(new ReportPage(_currentSessionId.Value));
        }
    }

    private void EndTestBtn_Click(object sender, RoutedEventArgs e)
    {
        // Cancel or close the view
        ProgressContainer.Visibility = Visibility.Collapsed;
        FormContainer.Visibility = Visibility.Visible;
    }
}
