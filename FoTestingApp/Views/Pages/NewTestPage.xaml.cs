using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services;
using FoTestingApp.Services.Core;
using MaterialDesignThemes.Wpf;

namespace FoTestingApp.Views.Pages;

public partial class NewTestPage : Page
{
    private readonly ApiService _api = new();
    private int? _currentSessionId;

    /// <summary>The search in flight for each picker; a newer keystroke cancels the older search.</summary>
    private CancellationTokenSource? _popSearch;
    private CancellationTokenSource? _customerSearch;

    /// <summary>The run in progress: cancelled by "Batal" and by leaving the page, so it never goes on unseen.</summary>
    private CancellationTokenSource? _runCts;

    /// <summary>The site last tested, for "Uji Ulang" (a new session for the same site).</summary>
    private FoCustomer? _lastCustomer;

    private string _testMode = "customer";
    private bool _enableRealTimeValidation = false;

    private bool IsRunning => _runCts is not null;

    public NewTestPage()
    {
        InitializeComponent();
        DiagnosticLogsPanel.SizeChanged += DiagnosticLogsPanel_SizeChanged;
        Unloaded += (_, _) =>
        {
            _runCts?.Cancel();
            _popSearch?.Cancel();
            _customerSearch?.Cancel();
        };
    }

    // ── Stage Navigation ──────────────────────────────────────────────────────

    private void StartLandingBtn_Click(object sender, RoutedEventArgs e)
    {
        LandingContainer.Visibility = Visibility.Collapsed;
        LocationContainer.Visibility = Visibility.Visible;
    }

    private async void PopCard_Click(object sender, RoutedEventArgs e)
    {
        await SelectTestModeAsync("pop");
    }

    private async void PopCard_Click(object sender, MouseButtonEventArgs e)
    {
        await SelectTestModeAsync("pop");
    }

    private async void CpeCard_Click(object sender, RoutedEventArgs e)
    {
        await SelectTestModeAsync("customer");
    }

    private async void CpeCard_Click(object sender, MouseButtonEventArgs e)
    {
        await SelectTestModeAsync("customer");
    }

    private async Task SelectTestModeAsync(string mode)
    {
        _testMode = mode;
        LocationContainer.Visibility = Visibility.Collapsed;
        FormContainer.Visibility = Visibility.Visible;
        ApplyTestMode();

        // ApplyTestMode starts loading the list for the mode.
        await Task.CompletedTask;
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
        SelectedDetailsCard.Visibility = Visibility.Collapsed;
        _enableRealTimeValidation = false;

        // Reset borders
        var borderBrush = (SolidColorBrush)FindResource("BorderLightBrush");
        FullNameBox.BorderBrush = borderBrush;
        PackageMbpsBox.BorderBrush = borderBrush;
        AddressBox.BorderBrush = borderBrush;

        if (PackageWarningText != null) PackageWarningText.Visibility = Visibility.Collapsed;
        if (NameWarningText != null) NameWarningText.Visibility = Visibility.Collapsed;
        if (AddressWarningText != null) AddressWarningText.Visibility = Visibility.Collapsed;

        if (_testMode == "pop")
        {
            FormTitle.Text = "Pengujian di POP";
            FormSubtitle.Text = "PENGUJIAN JARINGAN POP";
            FormSectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ServerNetwork;
            FormSectionTitle.Text = "Data POP";
            PopSelectionPanel.Visibility = Visibility.Visible;
            CustomerSelectionPanel.Visibility = Visibility.Collapsed;
            NameFieldPanel.Visibility = Visibility.Collapsed;
            PackageFieldPanel.Visibility = Visibility.Collapsed;
            AddressFieldPanel.Visibility = Visibility.Collapsed;
            NotesFieldPanel.Visibility = Visibility.Visible;
            FullNameBox.IsReadOnly = true;
            // A POP has no package: its speedtest is judged on a measured speed alone.
            PackageMbpsBox.Text = string.Empty;
            FullNameBox.Text = string.Empty;
            AddressBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            PopComboBox.ItemsSource = null;
            PopComboBox.SelectedItem = null;
            PopStatusText.Text = string.Empty;
            BtnEditDetails.Visibility = Visibility.Collapsed;

            _ = LoadAvailablePopsAsync();
        }
        else
        {
            FormTitle.Text = "Pengujian Kualitas Jaringan di Pelanggan";
            FormSubtitle.Text = "PENGUJIAN JARINGAN CPE";
            FormSectionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.AccountPlus;
            FormSectionTitle.Text = "Data Pelanggan";
            NameFieldLabel.Text = "NAMA PELANGGAN";
            MaterialDesignThemes.Wpf.HintAssist.SetHint(FullNameBox, "Nama Pelanggan *");
            PackageFieldLabel.Text = "PAKET INTERNET";
            MaterialDesignThemes.Wpf.HintAssist.SetHint(PackageMbpsBox, "Kecepatan Paket (Mbps) *");
            MaterialDesignThemes.Wpf.HintAssist.SetHint(AddressBox, "Alamat Pelanggan *");
            PopSelectionPanel.Visibility = Visibility.Collapsed;
            CustomerSelectionPanel.Visibility = Visibility.Visible;
            NameFieldPanel.Visibility = Visibility.Visible;
            PackageFieldPanel.Visibility = Visibility.Visible;
            AddressFieldPanel.Visibility = Visibility.Visible;
            NotesFieldPanel.Visibility = Visibility.Visible;
            FullNameBox.IsReadOnly = false;
            PackageMbpsBox.Text = string.Empty;
            FullNameBox.Text = string.Empty;
            AddressBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            PopComboBox.ItemsSource = null;
            PopComboBox.SelectedItem = null;
            PopStatusText.Text = string.Empty;
            BtnEditDetails.Visibility = Visibility.Visible;

            _ = LoadAvailableCustomersAsync();
        }
    }

    /// <summary>
    /// Loads POPs for a search, 300 ms after the last keystroke; a newer search cancels this one, so
    /// the list always answers the text in the box (typing "budi" never ends on the results for "b").
    /// </summary>
    private async Task LoadAvailablePopsAsync(string query = "")
    {
        _popSearch?.Cancel();
        var search = _popSearch = new CancellationTokenSource();

        try
        {
            if (!string.IsNullOrEmpty(query)) await Task.Delay(300, search.Token);
            if (PopLoadingBar != null) PopLoadingBar.Visibility = Visibility.Visible;
            PopStatusText.Text = string.IsNullOrWhiteSpace(query) ? "Memuat daftar POP..." : $"Mencari POP: '{query}'...";

            var pops = await _api.GetAvailablePopsAsync(query, search.Token);
            if (search.IsCancellationRequested) return;
            PopComboBox.ItemsSource = pops;
            PopStatusText.Text = pops.Count > 0 ? $"{pops.Count} POP tersedia." : "Tidak ada POP yang tersedia.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load available POPs");
            PopStatusText.Text = ex is ApiException ? ex.Message : "Gagal memuat daftar POP. Silakan coba lagi.";
            PopComboBox.ItemsSource = null;
        }
        finally
        {
            if (ReferenceEquals(_popSearch, search) && PopLoadingBar != null) PopLoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private void PopComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PopComboBox.SelectedItem is not PopOption selectedPop)
        {
            FullNameBox.Text = string.Empty;
            AddressBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            SelectedDetailsCard.Visibility = Visibility.Collapsed;
            return;
        }

        FullNameBox.Text = selectedPop.DisplayName;
        AddressBox.Text = selectedPop.Address ?? string.Empty;
        NotesBox.Text = string.Empty;

        // Show read-only details card
        DetailsCardIcon.Kind = PackIconKind.ServerNetwork;
        DetailsNameText.Text = selectedPop.Name;
        DetailsSiteIdText.Text = selectedPop.SiteId;
        DetailsSiteIdBadge.Visibility = string.IsNullOrWhiteSpace(selectedPop.SiteId) ? Visibility.Collapsed : Visibility.Visible;
        DetailsPackageBadge.Visibility = Visibility.Collapsed;
        DetailsAddressText.Text = selectedPop.Address ?? "-";
        DetailsAddressPanel.Visibility = string.IsNullOrWhiteSpace(selectedPop.Address) ? Visibility.Collapsed : Visibility.Visible;
        SelectedDetailsCard.Visibility = Visibility.Visible;
    }

    private async void PopComboBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is ComboBox cb)
        {
            if (cb.IsDropDownOpen)
            {
                if (cb.SelectedItem == null)
                {
                    await LoadAvailablePopsAsync(cb.Text);
                }
            }
        }
    }

    private async void PopComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (PopComboBox.ItemsSource == null)
        {
            await LoadAvailablePopsAsync();
        }
    }

    /// <summary>Loads customers for a search, debounced and cancelled like the POP list.</summary>
    private async Task LoadAvailableCustomersAsync(string query = "")
    {
        _customerSearch?.Cancel();
        var search = _customerSearch = new CancellationTokenSource();

        try
        {
            if (!string.IsNullOrEmpty(query)) await Task.Delay(300, search.Token);
            if (CustomerLoadingBar != null) CustomerLoadingBar.Visibility = Visibility.Visible;
            CustomerStatusText.Text = string.IsNullOrWhiteSpace(query) ? "Memuat daftar pelanggan..." : $"Mencari pelanggan: '{query}'...";

            var customers = await _api.SearchCustomersAsync(query, search.Token);
            if (search.IsCancellationRequested) return;
            CustomerComboBox.ItemsSource = customers;
            CustomerStatusText.Text = customers.Count > 0 ? $"{customers.Count} pelanggan ditemukan." : "Pelanggan tidak ditemukan.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load customers");
            CustomerStatusText.Text = ex is ApiException ? ex.Message : "Gagal memuat pelanggan. Silakan coba lagi.";
        }
        finally
        {
            if (ReferenceEquals(_customerSearch, search) && CustomerLoadingBar != null) CustomerLoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private void CustomerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _enableRealTimeValidation = false;
        
        // Reset borders
        var borderBrush = (SolidColorBrush)FindResource("BorderLightBrush");
        FullNameBox.BorderBrush = borderBrush;
        PackageMbpsBox.BorderBrush = borderBrush;
        AddressBox.BorderBrush = borderBrush;
        
        if (PackageWarningText != null) PackageWarningText.Visibility = Visibility.Collapsed;
        if (NameWarningText != null) NameWarningText.Visibility = Visibility.Collapsed;
        if (AddressWarningText != null) AddressWarningText.Visibility = Visibility.Collapsed;

        if (CustomerComboBox.SelectedItem is not FoCustomer selectedCustomer)
        {
            SelectedDetailsCard.Visibility = Visibility.Collapsed;
            NameFieldPanel.Visibility = Visibility.Visible;
            PackageFieldPanel.Visibility = Visibility.Visible;
            AddressFieldPanel.Visibility = Visibility.Visible;
            FullNameBox.Text = string.Empty;
            PackageMbpsBox.Text = string.Empty;
            AddressBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            
            // Reset grid column
            Grid.SetColumn(PackageFieldPanel, 2);
            Grid.SetColumnSpan(PackageFieldPanel, 1);
            return;
        }

        FullNameBox.Text = selectedCustomer.FullName;
        PackageMbpsBox.Text = selectedCustomer.PackageMbps > 0 ? selectedCustomer.PackageMbps.ToString() : "";
        AddressBox.Text = selectedCustomer.Address ?? string.Empty;
        NotesBox.Text = string.Empty;

        // Show read-only details card
        DetailsCardIcon.Kind = PackIconKind.Account;
        DetailsNameText.Text = selectedCustomer.FullName;
        // The service number tells two customers of the same name apart.
        DetailsSiteIdText.Text = selectedCustomer.ServiceNumber ?? string.Empty;
        DetailsSiteIdBadge.Visibility = string.IsNullOrWhiteSpace(selectedCustomer.ServiceNumber) ? Visibility.Collapsed : Visibility.Visible;
        
        if (!string.IsNullOrWhiteSpace(selectedCustomer.PackageName))
        {
            DetailsPackageText.Text = selectedCustomer.PackageName;
            DetailsPackageBadge.Visibility = Visibility.Visible;
        }
        else if (selectedCustomer.PackageMbps > 0)
        {
            DetailsPackageText.Text = $"{selectedCustomer.PackageMbps} Mbps";
            DetailsPackageBadge.Visibility = Visibility.Visible;
        }
        else
        {
            DetailsPackageBadge.Visibility = Visibility.Collapsed;
        }
        
        DetailsAddressText.Text = selectedCustomer.Address ?? "-";
        DetailsAddressPanel.Visibility = string.IsNullOrWhiteSpace(selectedCustomer.Address) ? Visibility.Collapsed : Visibility.Visible;
        
        SelectedDetailsCard.Visibility = Visibility.Visible;
        
        // Keep input panels visible if the details are empty or missing so they can be filled manually
        NameFieldPanel.Visibility = string.IsNullOrWhiteSpace(FullNameBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        PackageFieldPanel.Visibility = string.IsNullOrWhiteSpace(PackageMbpsBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        AddressFieldPanel.Visibility = string.IsNullOrWhiteSpace(AddressBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        // Apply visual states and layout adjustments if package is missing
        if (PackageFieldPanel.Visibility == Visibility.Visible && string.IsNullOrWhiteSpace(PackageMbpsBox.Text))
        {
            _enableRealTimeValidation = true;
            PackageMbpsBox.BorderBrush = (SolidColorBrush)FindResource("FailRedBrush");
            if (PackageWarningText != null)
            {
                PackageWarningText.Text = "* Kecepatan paket belum terisi. Harap masukkan kecepatan paket pelanggan (Mbps).";
                PackageWarningText.Visibility = Visibility.Visible;
            }

            // Span full width if Name is collapsed but Package is visible
            if (NameFieldPanel.Visibility == Visibility.Collapsed)
            {
                Grid.SetColumn(PackageFieldPanel, 0);
                Grid.SetColumnSpan(PackageFieldPanel, 3);
            }
            else
            {
                Grid.SetColumn(PackageFieldPanel, 2);
                Grid.SetColumnSpan(PackageFieldPanel, 1);
            }
        }
        else
        {
            Grid.SetColumn(PackageFieldPanel, 2);
            Grid.SetColumnSpan(PackageFieldPanel, 1);
        }
    }

    private async void CustomerComboBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is ComboBox cb)
        {
            if (cb.IsDropDownOpen)
            {
                if (cb.SelectedItem == null)
                {
                    await LoadAvailableCustomersAsync(cb.Text);
                }
            }
        }
    }

    private async void CustomerComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (CustomerComboBox.ItemsSource == null)
        {
            await LoadAvailableCustomersAsync();
        }
    }

    private void BtnEditDetails_Click(object sender, RoutedEventArgs e)
    {
        NameFieldPanel.Visibility = Visibility.Visible;
        PackageFieldPanel.Visibility = Visibility.Visible;
        AddressFieldPanel.Visibility = Visibility.Visible;

        // Reset grid column span
        Grid.SetColumn(PackageFieldPanel, 2);
        Grid.SetColumnSpan(PackageFieldPanel, 1);

        if (string.IsNullOrWhiteSpace(PackageMbpsBox.Text))
        {
            _enableRealTimeValidation = true;
            PackageMbpsBox.BorderBrush = (SolidColorBrush)FindResource("FailRedBrush");
            if (PackageWarningText != null)
            {
                PackageWarningText.Text = "* Kecepatan paket belum terisi. Harap masukkan kecepatan paket pelanggan (Mbps).";
                PackageWarningText.Visibility = Visibility.Visible;
            }
        }
    }

    private void DiagnosticLogsPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        LogsScrollViewer.ScrollToEnd();
    }

    private async void StartTestBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsRunning)
        {
            MessageBox.Show("Pengujian sebelumnya masih dihentikan. Tunggu beberapa detik lalu coba lagi.", "Tunggu", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!ValidateForm()) { return; }

        StartTestBtn.IsEnabled = false;
        try
        {
            var isPopMode = _testMode == "pop";
            var selectedPop = isPopMode ? PopComboBox.SelectedItem as PopOption : null;
            var selectedCustomer = !isPopMode ? CustomerComboBox.SelectedItem as FoCustomer : null;
            var customer = new FoCustomer
            {
                SiteType = isPopMode ? "pop" : "customer",
                SiteId = isPopMode ? selectedPop?.SiteId : selectedCustomer?.SiteId,
                ServiceNumber = selectedCustomer?.ServiceNumber,
                FullName = isPopMode ? selectedPop?.DisplayName ?? string.Empty : FullNameBox.Text.Trim(),
                Address = isPopMode ? selectedPop?.Address ?? "-" : AddressBox.Text.Trim(),
                // A POP has no package; a customer's was validated as a positive number.
                PackageMbps = !isPopMode && int.TryParse(PackageMbpsBox.Text.Trim(), out var pkg) ? pkg : 0,
                Email = selectedCustomer?.Email,
                TechnicalNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
            };

            await RunSessionAsync(customer);
        }
        finally
        {
            StartTestBtn.IsEnabled = true;
        }
    }

    /// <summary>
    /// One full test session for a site: store the site, open a session, run every test, close the
    /// session with its status, show the results. Cancelled ("Batal", or leaving the page), the
    /// session is closed as FAIL with a note saying so, and no results screen appears.
    /// </summary>
    private async Task RunSessionAsync(FoCustomer customer)
    {
        _lastCustomer = customer;
        _currentSessionId = null;
        ResetProgressUi();
        FormContainer.Visibility = Visibility.Collapsed;
        ResultsContainer.Visibility = Visibility.Collapsed;
        ProgressContainer.Visibility = Visibility.Visible;
        StartStepPulse(Step1Circle);

        using var cts = new CancellationTokenSource();
        _runCts = cts;
        TestRun? run = null;
        try
        {
            CurrentActivityText.Text = "Menyimpan data lokasi...";
            customer.Id = await _api.UpsertCustomerAsync(customer);

            var startedAt = DateTime.Now;
            _currentSessionId = await _api.CreateTestSessionAsync(new FoTestSession
            {
                CertificationId = $"TEST-{startedAt:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
                CustomerId = customer.Id,
                TechnicianId = SessionManager.CurrentUser?.Id ?? 0,
                TestDate = startedAt,
                StartTime = startedAt,
                OverallStatus = TestOverallStatus.Fail,
            });

            run = new TestRun(_api, _currentSessionId.Value, cts.Token);
            await new NetworkTestService().RunAllAsync(run, DiagnosticLogsPanel, CreateProgress(), customer.PackageMbps);

            await CloseSessionAsync(run.SessionId, run.OverallStatus, notes: null);
            ShowResults(run);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Serilog.Log.Information("Test session {SessionId} cancelled by the technician", _currentSessionId);
            if (_currentSessionId is { } sessionId)
            {
                await CloseSessionAsync(sessionId, TestOverallStatus.Fail, "Pengujian dibatalkan oleh teknisi sebelum selesai.");
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Test execution failed");
            CurrentActivityText.Text = "Pengujian gagal";
            SubTaskDetailText.Text = ex is ApiException ? ex.Message : $"Terjadi kesalahan: {ex.Message}";
            EndTestBtnText.Text = "TUTUP";
            if (_currentSessionId is { } sessionId && run is not null)
            {
                await CloseSessionAsync(sessionId, run.OverallStatus, $"Pengujian terhenti: {ex.Message}");
            }
        }
        finally
        {
            _runCts = null;
            StartStepPulse(null);
        }
    }

    /// <summary>Writes a session's final status; a failure is logged and shown, not thrown.</summary>
    private async Task CloseSessionAsync(int sessionId, TestOverallStatus status, string? notes)
    {
        try
        {
            await _api.UpdateSessionStatusAsync(sessionId, status, DateTime.Now, notes);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to update the status of session {SessionId}", sessionId);
            UnsavedStatusError = ex.Message;
        }
    }

    /// <summary>Why the session's final status did not reach the server, for the results screen.</summary>
    private string? UnsavedStatusError { get; set; }

    private void ResetProgressUi()
    {
        UnsavedStatusError = null;
        DiagnosticLogsPanel.Children.Clear();
        Line1.Value = 0; Line2.Value = 0;
        AnimateProgressBar(Line1, 0);
        AnimateProgressBar(Line2, 0);

        foreach (var (circle, icon, text) in new[] { (Step2Circle, Step2Icon, Step2Text), (Step3Circle, Step3Icon, Step3Text) })
        {
            circle.Background = (Brush)FindResource("AppBackgroundBrush");
            circle.BorderBrush = (Brush)FindResource("BorderLightBrush");
            icon.Foreground = (Brush)FindResource("TextLightBrush");
            text.Foreground = (Brush)FindResource("TextLightBrush");
        }

        CurrentActivityText.Text = "Menyiapkan pengujian...";
        TaskCountText.Text = "Task 1 of 7";
        SubTaskProgressBar.Value = 0;
        AnimateProgressBar(SubTaskProgressBar, 0);
        SubTaskDetailText.Text = "Menyiapkan mesin diagnostik...";
        SaveReportBtn.Visibility = Visibility.Collapsed;
        EndTestBtnText.Text = "BATAL";
    }

    private IProgress<(int percent, string status)> CreateProgress() => new Progress<(int percent, string status)>(p =>
    {
        // Progress posts asynchronously: an update arriving after the run ended must not restart the stepper.
        if (!IsRunning) return;
        SessionManager.RecordActivity();

        var parts = p.status.Split('|');
        if (parts.Length != 3)
        {
            SubTaskDetailText.Text = p.status;
            return;
        }

        TaskCountText.Text = parts[0];
        CurrentActivityText.Text = parts[1];
        SubTaskDetailText.Text = parts[2];
        AnimateProgressBar(SubTaskProgressBar, p.percent);

        if (!int.TryParse(parts[0].Replace("Task ", "").Split(' ')[0], out var taskNum)) return;

        if (taskNum <= 5)
        {
            StartStepPulse(Step1Circle);
            AnimateProgressBar(Line1, taskNum / 5.0 * 100);
            return;
        }

        StartStepPulse(taskNum == 6 ? Step2Circle : Step3Circle);
        AnimateProgressBar(Line1, 100);
        AnimateProgressBar(Line2, taskNum == 6 ? 50 : 100);
        MarkStepActive(Step2Circle, Step2Icon, Step2Text);
        if (taskNum == 7) MarkStepActive(Step3Circle, Step3Icon, Step3Text);
    });

    private void MarkStepActive(Border circle, PackIcon icon, TextBlock text)
    {
        circle.Background = (Brush)FindResource("PrimaryBrush");
        circle.BorderBrush = (Brush)FindResource("PrimaryBrush");
        icon.Foreground = Brushes.White;
        text.Foreground = (Brush)FindResource("PrimaryBrush");
    }

    private bool ValidateForm()
    {
        if (_testMode == "pop")
        {
            if (PopComboBox.SelectedItem is not PopOption)
            {
                MessageBox.Show("Silakan pilih POP yang tersedia.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        else
        {
            _enableRealTimeValidation = true;

            bool isNameEmpty = string.IsNullOrWhiteSpace(FullNameBox.Text);
            bool isPackageEmpty = string.IsNullOrWhiteSpace(PackageMbpsBox.Text);
            bool isAddressEmpty = string.IsNullOrWhiteSpace(AddressBox.Text);

            var borderBrush = (SolidColorBrush)FindResource("BorderLightBrush");
            var failBrush = (SolidColorBrush)FindResource("FailRedBrush");

            // Reset borders first
            FullNameBox.BorderBrush = borderBrush;
            PackageMbpsBox.BorderBrush = borderBrush;
            AddressBox.BorderBrush = borderBrush;

            if (isNameEmpty || isPackageEmpty || isAddressEmpty)
            {
                if (isNameEmpty)
                {
                    FullNameBox.BorderBrush = failBrush;
                    if (NameWarningText != null)
                    {
                        NameWarningText.Text = "* Nama pelanggan wajib diisi.";
                        NameWarningText.Visibility = Visibility.Visible;
                    }
                }
                if (isAddressEmpty)
                {
                    AddressBox.BorderBrush = failBrush;
                    if (AddressWarningText != null)
                    {
                        AddressWarningText.Text = "* Alamat pelanggan wajib diisi.";
                        AddressWarningText.Visibility = Visibility.Visible;
                    }
                }
                if (isPackageEmpty)
                {
                    PackageMbpsBox.BorderBrush = failBrush;
                    if (PackageWarningText != null)
                    {
                        PackageWarningText.Text = "* Kecepatan paket wajib diisi.";
                        PackageWarningText.Visibility = Visibility.Visible;
                    }
                }
                return false;
            }

            if (!int.TryParse(PackageMbpsBox.Text, out var pkg) || pkg <= 0)
            {
                PackageMbpsBox.BorderBrush = failBrush;
                if (PackageWarningText != null)
                {
                    PackageWarningText.Text = "* Kecepatan harus berupa angka positif (Mbps).";
                    PackageWarningText.Visibility = Visibility.Visible;
                }
                return false;
            }
        }

        return true;
    }

    private void FullNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_enableRealTimeValidation || FullNameBox == null) return;

        bool isEmpty = string.IsNullOrWhiteSpace(FullNameBox.Text);
        FullNameBox.BorderBrush = isEmpty
            ? (SolidColorBrush)FindResource("FailRedBrush")
            : (SolidColorBrush)FindResource("BorderLightBrush");

        if (NameWarningText != null)
            NameWarningText.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PackageMbpsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_enableRealTimeValidation || PackageMbpsBox == null || PackageWarningText == null) return;

        bool isEmpty = string.IsNullOrWhiteSpace(PackageMbpsBox.Text);
        bool isValidNumber = int.TryParse(PackageMbpsBox.Text, out var pkg) && pkg > 0;

        if (isEmpty)
        {
            PackageMbpsBox.BorderBrush = (SolidColorBrush)FindResource("FailRedBrush");
            PackageWarningText.Text = "* Kecepatan paket wajib diisi.";
            PackageWarningText.Visibility = Visibility.Visible;
        }
        else if (!isValidNumber)
        {
            PackageMbpsBox.BorderBrush = (SolidColorBrush)FindResource("FailRedBrush");
            PackageWarningText.Text = "* Kecepatan harus berupa angka positif (Mbps).";
            PackageWarningText.Visibility = Visibility.Visible;
        }
        else
        {
            PackageMbpsBox.BorderBrush = (SolidColorBrush)FindResource("BorderLightBrush");
            PackageWarningText.Visibility = Visibility.Collapsed;
        }
    }

    private void AddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_enableRealTimeValidation || AddressBox == null) return;

        bool isEmpty = string.IsNullOrWhiteSpace(AddressBox.Text);
        AddressBox.BorderBrush = isEmpty
            ? (SolidColorBrush)FindResource("FailRedBrush")
            : (SolidColorBrush)FindResource("BorderLightBrush");

        if (AddressWarningText != null)
            AddressWarningText.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveReportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSessionId.HasValue)
        {
            NavigationService?.Navigate(new ReportPage(_currentSessionId.Value));
        }
    }

    /// <summary>"Batal" while running cancels the run (after a yes); afterwards it closes the screen.</summary>
    private void EndTestBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsRunning)
        {
            var confirm = MessageBox.Show("Hentikan pengujian yang sedang berjalan? Sesi akan dicatat sebagai dibatalkan (FAIL).",
                "Batalkan Pengujian", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            CurrentActivityText.Text = "Membatalkan...";
            SubTaskDetailText.Text = "Menghentikan pengujian yang sedang berjalan...";
            _runCts?.Cancel();
        }

        StartStepPulse(null);
        ProgressContainer.Visibility = Visibility.Collapsed;
        LocationContainer.Visibility = Visibility.Visible;
    }

    private void NewTestFromResults_Click(object sender, RoutedEventArgs e)
    {
        ResultsContainer.Visibility = Visibility.Collapsed;
        LocationContainer.Visibility = Visibility.Visible;
    }

    // ── Results Display ───────────────────────────────────────────────────────

    /// <summary>
    /// The results of the run just finished, as measured here — the same list and verdict that were
    /// sent — with a warning naming any result the server did not store.
    /// </summary>
    private void ShowResults(TestRun run)
    {
        var results = run.Results.ToList();
        var passCount = results.Count(r => r.Status == TestStatus.Pass);
        var status = run.OverallStatus;

        ProgressContainer.Visibility = Visibility.Collapsed;
        ResultsContainer.Visibility = Visibility.Visible;
        ResultCardsPanel.Children.Clear();

        ResultSummaryText.Text = $"{results.Count} pengujian dilakukan — {DateTime.Now:dd MMM yyyy, HH:mm}";
        ResultScoreText.Text = $"{passCount}/{results.Count} test lulus";
        (ResultStatusText.Text, ResultStatusText.Foreground) = status switch
        {
            TestOverallStatus.Pass => ("PASS", (Brush)FindResource("PassGreenBrush")),
            TestOverallStatus.Partial => ("PARTIAL", (Brush)FindResource("WarnYellowBrush")),
            _ => ("FAIL", (Brush)FindResource("FailRedBrush")),
        };

        var problems = new List<string>();
        if (run.Unsaved.Count > 0)
        {
            problems.Add($"{run.Unsaved.Count} hasil tidak tersimpan di server: {string.Join(", ", run.Unsaved)}. Laporan dari server tidak akan memuatnya; ulangi pengujian bila perlu.");
        }
        if (UnsavedStatusError is { } statusError)
        {
            problems.Add($"Status akhir sesi tidak tersimpan ({statusError}); di riwayat sesi ini bisa tampil FAIL.");
        }
        UnsavedWarningText.Text = string.Join(Environment.NewLine, problems);
        UnsavedWarning.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        SaveReportBtn.Visibility = Visibility.Visible;
        RetryFailedBtn.Visibility = status == TestOverallStatus.Pass ? Visibility.Collapsed : Visibility.Visible;

        PopulateResultsWithExpanders(results);
    }

    /// <summary>
    /// Tests the same site again in a new session: the earlier session stays in the history as it
    /// was, and the new one holds one complete, clean set of results (the API keeps every result it
    /// is sent, so re-running into the old session would mix old failures with new passes).
    /// </summary>
    private async void RetryFailedBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCustomer is null || IsRunning) return;

        var confirm = MessageBox.Show("Jalankan ulang seluruh pengujian untuk lokasi ini sebagai sesi baru? Sesi sebelumnya tetap tersimpan di riwayat.",
            "Uji Ulang", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RetryFailedBtn.IsEnabled = false;
        try
        {
            await RunSessionAsync(_lastCustomer);
        }
        finally
        {
            RetryFailedBtn.IsEnabled = true;
        }
    }

    private void PopulateResultsWithExpanders(List<FoTestResult> results)
    {
        var networkTypes = new[] { TestTypes.PingGateway, TestTypes.PingDns, TestTypes.NslookupNasional, TestTypes.NslookupInternasional, TestTypes.PingDomainLokal };
        var appTypes = new[] { TestTypes.BrowsingTest, TestTypes.StreamingTest, TestTypes.SocialMediaTest };
        var bandwidthTypes = new[] { TestTypes.SpeedtestFast, TestTypes.SpeedtestOokla };

        var netResults = results.Where(r => networkTypes.Contains(r.TestType)).ToList();
        var appResults = results.Where(r => appTypes.Contains(r.TestType)).ToList();
        var bwResults = results.Where(r => bandwidthTypes.Contains(r.TestType)).ToList();

        if (netResults.Any())
            ResultCardsPanel.Children.Add(CreateExpanderGroup("Network & Routing Tests", netResults, true));

        if (appResults.Any())
            ResultCardsPanel.Children.Add(CreateExpanderGroup("Application Health", appResults, false));

        if (bwResults.Any())
            ResultCardsPanel.Children.Add(CreateExpanderGroup("Bandwidth Capacity", bwResults, false));
    }

    private Expander CreateExpanderGroup(string groupName, List<FoTestResult> groupResults, bool expandDefault)
    {
        var passCount = groupResults.Count(r => r.Status == TestStatus.Pass);
        var totalCount = groupResults.Count;
        var hasFailures = passCount < totalCount;

        var headerBrush = hasFailures ? (Brush)FindResource("FailRedBrush") : (Brush)FindResource("PassGreenBrush");

        var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        headerStack.Children.Add(new TextBlock 
        { 
            Text = groupName, 
            FontSize = 16, 
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextDarkBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 250 // Align badges strictly
        });
        
        var badgeBorder = new Border
        {
            Background = hasFailures 
                ? new SolidColorBrush(Color.FromArgb(0x15, 0xEF, 0x44, 0x44))
                : new SolidColorBrush(Color.FromArgb(0x15, 0x22, 0xC5, 0x5E)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        badgeBorder.Child = new TextBlock
        {
            Text = $"{passCount}/{totalCount} Passed",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = headerBrush
        };
        headerStack.Children.Add(badgeBorder);

        var cardsPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 16) };
        foreach (var r in groupResults)
        {
            cardsPanel.Children.Add(BuildResultCard(r));
        }

        return new Expander
        {
            Header = headerStack,
            Content = cardsPanel,
            IsExpanded = hasFailures || expandDefault, // Otomatis kebuka jika gagal/network
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Brush)FindResource("BorderLightBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private Border BuildResultCard(FoTestResult result)
    {
        var isPassed = result.Status == TestStatus.Pass;
        var statusBrush = isPassed
            ? (Brush)FindResource("PassGreenBrush")
            : (Brush)FindResource("FailRedBrush");
        var bgTint = isPassed
            ? new SolidColorBrush(Color.FromArgb(0x0D, 0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Color.FromArgb(0x0D, 0xEF, 0x44, 0x44));

        var card = new Border
        {
            Background = (Brush)FindResource("CardBackgroundBrush"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 12),
            BorderBrush = (Brush)FindResource("BorderLightBrush"),
            BorderThickness = new Thickness(1),
        };

        var outerStack = new StackPanel();

        // ── Header row: icon + name + status badge + chevron
        var headerGrid = new Grid { Cursor = System.Windows.Input.Cursors.Hand };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon
        var iconBorder = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
            Background = bgTint, VerticalAlignment = VerticalAlignment.Center,
        };
        iconBorder.Child = new PackIcon
        {
            Kind = GetTestIcon(result.TestType), Width = 20, Height = 20,
            Foreground = statusBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(iconBorder, 0);
        headerGrid.Children.Add(iconBorder);

        // Test name + target
        var namePanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        namePanel.Children.Add(new TextBlock
        {
            Text = FormatTestTypeName(result.TestType),
            FontSize = 15, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextDarkBrush"),
        });
        if (!string.IsNullOrEmpty(result.Target))
        {
            namePanel.Children.Add(new TextBlock
            {
                Text = result.Target, FontSize = 11,
                Foreground = (Brush)FindResource("TextLightBrush"),
            });
        }
        Grid.SetColumn(namePanel, 1);
        headerGrid.Children.Add(namePanel);

        // Status chip
        var statusChip = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 5, 12, 5),
            Background = bgTint, VerticalAlignment = VerticalAlignment.Center,
        };
        statusChip.Child = new TextBlock
        {
            Text = isPassed ? "PASS" : "FAIL",
            FontSize = 12, FontWeight = FontWeights.Bold, Foreground = statusBrush,
        };
        Grid.SetColumn(statusChip, 2);
        headerGrid.Children.Add(statusChip);

        // Chevron toggle icon
        var chevron = new PackIcon
        {
            Kind = PackIconKind.ChevronDown,
            Width = 22, Height = 22,
            Foreground = (Brush)FindResource("TextLightBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(0),
        };
        Grid.SetColumn(chevron, 3);
        headerGrid.Children.Add(chevron);

        outerStack.Children.Add(headerGrid);

        // ── Detail data rows (collapsed by default)
        Border? detailBorder = null;
        if (result.ResultData is not null)
        {
            detailBorder = new Border
            {
                Background = (Brush)FindResource("AppBackgroundBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 12, 0, 0),
                Visibility = Visibility.Collapsed,
            };

            var detailPanel = new StackPanel();
            PopulateDetailRows(detailPanel, result);
            detailBorder.Child = detailPanel;
            outerStack.Children.Add(detailBorder);
        }

        // ── Click handler to toggle detail visibility
        var detail = detailBorder;
        headerGrid.MouseLeftButtonDown += (_, _) =>
        {
            if (detail is null) return;
            var rt = (RotateTransform)chevron.RenderTransform;
            if (detail.Visibility == Visibility.Collapsed)
            {
                detail.Visibility = Visibility.Visible;
                rt.Angle = 180;
            }
            else
            {
                detail.Visibility = Visibility.Collapsed;
                rt.Angle = 0;
            }
        };

        card.Child = outerStack;
        return card;
    }

    private void PopulateDetailRows(StackPanel panel, FoTestResult result)
    {
        var rows = ResultFormatter.Rows(result.TestType, result.ResultData?.RootElement);
        if (rows.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Data tidak tersedia", FontSize = 12,
                Foreground = (Brush)FindResource("TextLightBrush"), FontStyle = FontStyles.Italic,
            });
            return;
        }

        foreach (var (label, value) in rows)
        {
            AddDetailRow(panel, label, value);
        }
    }

    private void AddDetailRow(StackPanel panel, string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelTb = new TextBlock
        {
            Text = label, FontSize = 12, Foreground = (Brush)FindResource("TextLightBrush"),
        };
        Grid.SetColumn(labelTb, 0);
        row.Children.Add(labelTb);

        var valueTb = new TextBlock
        {
            Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextDarkBrush"),
        };
        Grid.SetColumn(valueTb, 1);
        row.Children.Add(valueTb);

        panel.Children.Add(row);
    }

    private static string FormatTestTypeName(string testType) => ResultFormatter.TestName(testType);

    private static PackIconKind GetTestIcon(string testType) => testType switch
    {
        TestTypes.PingGateway or TestTypes.PingDns or TestTypes.PingDomainLokal => PackIconKind.LanConnect,
        TestTypes.NslookupNasional or TestTypes.NslookupInternasional => PackIconKind.Dns,
        TestTypes.BrowsingTest => PackIconKind.Web,
        TestTypes.StreamingTest => PackIconKind.Video,
        TestTypes.SocialMediaTest => PackIconKind.AccountGroup,
        TestTypes.SpeedtestFast or TestTypes.SpeedtestOokla => PackIconKind.Speedometer,
        _ => PackIconKind.TestTube,
    };

    private void AnimateProgressBar(ProgressBar progressBar, double toValue)
    {
        var animation = new DoubleAnimation
        {
            To = toValue,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        progressBar.BeginAnimation(RangeBase.ValueProperty, animation);
    }

    private void StartStepPulse(Border? circle)
    {
        StopStepPulse(Step1Circle);
        StopStepPulse(Step2Circle);
        StopStepPulse(Step3Circle);

        if (circle == null) return;

        if (circle.RenderTransform is not ScaleTransform)
        {
            circle.RenderTransform = new ScaleTransform(1, 1);
            circle.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var scaleTransform = (ScaleTransform)circle.RenderTransform;

        var anim = new DoubleAnimation(1.0, 1.12, TimeSpan.FromMilliseconds(600))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);

        var borderAnim = new DoubleAnimation(1.0, 0.5, TimeSpan.FromMilliseconds(600))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        circle.BeginAnimation(UIElement.OpacityProperty, borderAnim);
    }

    private void StopStepPulse(Border circle)
    {
        if (circle.RenderTransform is ScaleTransform scaleTransform)
        {
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            circle.RenderTransform = null;
        }
        circle.BeginAnimation(UIElement.OpacityProperty, null);
        circle.Opacity = 1.0;
    }
}
