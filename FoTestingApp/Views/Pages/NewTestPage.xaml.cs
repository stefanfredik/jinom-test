using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services;

namespace FoTestingApp.Views.Pages;

public partial class NewTestPage : Page
{
    private readonly ApiService _api = new();
    private int? _currentSessionId;

    private string _testMode = "customer";

    public NewTestPage()
    {
        InitializeComponent();
    }

    // ── Stage Navigation ──────────────────────────────────────────────────────

    private void StartLandingBtn_Click(object sender, RoutedEventArgs e)
    {
        LandingContainer.Visibility = Visibility.Collapsed;
        LocationContainer.Visibility = Visibility.Visible;
    }

    private void PopCard_Click(object sender, RoutedEventArgs e)
    {
        SelectTestMode("pop");
    }

    private void PopCard_Click(object sender, MouseButtonEventArgs e)
    {
        SelectTestMode("pop");
    }

    private void CpeCard_Click(object sender, RoutedEventArgs e)
    {
        SelectTestMode("customer");
    }

    private void CpeCard_Click(object sender, MouseButtonEventArgs e)
    {
        SelectTestMode("customer");
    }

    private void SelectTestMode(string mode)
    {
        _testMode = mode;
        LocationContainer.Visibility = Visibility.Collapsed;
        FormContainer.Visibility = Visibility.Visible;
        ApplyTestMode();
    }

    private void BackToLanding_Click(object sender, RoutedEventArgs e)
    {
        LocationContainer.Visibility = Visibility.Collapsed;
        LandingContainer.Visibility = Visibility.Visible;
    }

    private void BackToLocation_Click(object sender, RoutedEventArgs e)
    {
        FormContainer.Visibility = Visibility.Collapsed;
        LocationContainer.Visibility = Visibility.Visible;
    }

    private void ApplyTestMode()
    {
        if (_testMode == "pop")
        {
            FormTitle.Text = "POP Network Test";
            FormSubtitle.Text = "PENGUJIAN JARINGAN POP";
            FormSectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ServerNetwork;
            FormSectionTitle.Text = "Data POP";
            NameFieldLabel.Text = "NAMA POP";
            MaterialDesignThemes.Wpf.HintAssist.SetHint(FullNameBox, "Masukkan nama POP");
            PackageFieldLabel.Text = "PAKET JNIX";
            MaterialDesignThemes.Wpf.HintAssist.SetHint(PackageMbpsBox, "Contoh: 100 Mbps");
        }
        else
        {
            FormTitle.Text = "Customer Network Test";
            FormSubtitle.Text = "PENGUJIAN JARINGAN CPE";
            FormSectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.AccountPlus;
            FormSectionTitle.Text = "Data Pelanggan";
            NameFieldLabel.Text = "NAMA PELANGGAN";
            MaterialDesignThemes.Wpf.HintAssist.SetHint(FullNameBox, "Masukkan nama pelanggan");
            PackageFieldLabel.Text = "PAKET INTERNET";
            MaterialDesignThemes.Wpf.HintAssist.SetHint(PackageMbpsBox, "Contoh: 100 Mbps");
        }
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
                FullName = FullNameBox.Text.Trim(),
                Address = "-",
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
        if (string.IsNullOrWhiteSpace(FullNameBox.Text) ||
            string.IsNullOrWhiteSpace(PackageMbpsBox.Text))
        {
            MessageBox.Show("Nama dan Paket wajib diisi.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(PackageMbpsBox.Text, out var pkg) || pkg <= 0)
        {
            MessageBox.Show("Paket harus berupa angka positif (Mbps).", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        ProgressContainer.Visibility = Visibility.Collapsed;
        LandingContainer.Visibility = Visibility.Visible;
    }
}
