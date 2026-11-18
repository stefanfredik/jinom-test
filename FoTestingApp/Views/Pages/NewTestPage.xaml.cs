using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services;
using FoTestingApp.Services.Core;
using MaterialDesignThemes.Wpf;

namespace FoTestingApp.Views.Pages;

/// <summary>
/// A test session from start to finish: pick where (customer or POP), confirm the site's data, run
/// every test (cancellable), then the verdict and every result. Colours come from the M3 roles by
/// resource reference only, so the page follows light and dark mode.
/// </summary>
public partial class NewTestPage : Page
{
    private readonly ApiService _api = new();
    private int? _currentSessionId;

    /// <summary>The search in flight for each picker; a newer keystroke cancels the older search.</summary>
    private CancellationTokenSource? _popSearch;
    private CancellationTokenSource? _customerSearch;

    /// <summary>The run in progress: cancelled by "Batalkan" and by leaving the page, so it never goes on unseen.</summary>
    private CancellationTokenSource? _runCts;

    /// <summary>The site last tested, for "Uji ulang" (a new session for the same site).</summary>
    private FoCustomer? _lastCustomer;

    private string _testMode = "customer";
    private bool _enableRealTimeValidation;

    private bool IsRunning => _runCts is not null;

    public NewTestPage()
    {
        InitializeComponent();
        DiagnosticLogsPanel.SizeChanged += (_, _) => LogsScrollViewer.ScrollToEnd();
        Unloaded += (_, _) =>
        {
            _runCts?.Cancel();
            _popSearch?.Cancel();
            _customerSearch?.Cancel();
        };
    }

    // ── Step 1: where ─────────────────────────────────────────────────────────

    private void CustomerChoice_Click(object sender, RoutedEventArgs e) => SelectMode("customer");

    private void PopChoice_Click(object sender, RoutedEventArgs e) => SelectMode("pop");

    private void SelectMode(string mode)
    {
        _testMode = mode;
        ShowStage(FormContainer);
        ApplyTestMode();
    }

    private void BackToLocation_Click(object sender, RoutedEventArgs e) => ShowStage(LocationContainer);

    private void ShowStage(FrameworkElement stage)
    {
        foreach (var candidate in new FrameworkElement[] { LocationContainer, FormContainer, ProgressContainer, ResultsContainer })
        {
            candidate.Visibility = ReferenceEquals(candidate, stage) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ── Step 2: who ───────────────────────────────────────────────────────────

    private void ApplyTestMode()
    {
        _enableRealTimeValidation = false;
        ClearErrors();
        SelectedDetailsCard.Visibility = Visibility.Collapsed;
        FullNameBox.Text = PackageMbpsBox.Text = AddressBox.Text = NotesBox.Text = string.Empty;
        PopComboBox.ItemsSource = null;
        PopComboBox.SelectedItem = null;
        PopStatusText.Text = string.Empty;
        CustomerComboBox.SelectedItem = null;
        PlacePackageField(nameVisible: true);

        var isPop = _testMode == "pop";
        FormTitle.Text = isPop ? "Uji di POP" : "Uji di pelanggan";
        FormSubtitle.Text = isPop ? "Pilih POP yang diuji, lalu mulai." : "Cari pelanggan, periksa datanya, lalu mulai.";
        PopSelectionPanel.Visibility = isPop ? Visibility.Visible : Visibility.Collapsed;
        CustomerSelectionPanel.Visibility = isPop ? Visibility.Collapsed : Visibility.Visible;
        // A POP is chosen, not typed; it has no package.
        NameFieldPanel.Visibility = PackageFieldPanel.Visibility = AddressFieldPanel.Visibility = isPop ? Visibility.Collapsed : Visibility.Visible;
        BtnEditDetails.Visibility = isPop ? Visibility.Collapsed : Visibility.Visible;

        if (isPop)
        {
            _ = LoadAvailablePopsAsync();
        }
        else
        {
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
            PopLoadingBar.Visibility = Visibility.Visible;
            SetStatus(PopStatusText, string.IsNullOrWhiteSpace(query) ? "Memuat daftar POP..." : $"Mencari \"{query}\"...");

            var pops = await _api.GetAvailablePopsAsync(query, search.Token);
            if (search.IsCancellationRequested) return;
            PopComboBox.ItemsSource = pops;
            SetStatus(PopStatusText, pops.Count > 0 ? $"{pops.Count} POP tersedia." : "Tidak ada POP yang cocok.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load available POPs");
            SetStatus(PopStatusText, ex is ApiException ? ex.Message : "Daftar POP gagal dimuat. Coba lagi.", error: true);
            PopComboBox.ItemsSource = null;
        }
        finally
        {
            if (ReferenceEquals(_popSearch, search)) PopLoadingBar.Visibility = Visibility.Hidden;
        }
    }

    private void PopComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PopComboBox.SelectedItem is not PopOption pop)
        {
            SelectedDetailsCard.Visibility = Visibility.Collapsed;
            return;
        }

        FullNameBox.Text = pop.DisplayName;
        AddressBox.Text = pop.Address ?? string.Empty;
        SetStatus(PopStatusText, string.Empty);

        DetailsCardIcon.Kind = PackIconKind.ServerNetwork;
        DetailsNameText.Text = pop.Name;
        DetailsSiteIdText.Text = $"POP #{pop.SiteId}";
        DetailsSiteIdBadge.Visibility = Visibility.Visible;
        DetailsPackageBadge.Visibility = Visibility.Collapsed;
        DetailsAddressText.Text = pop.Address ?? "-";
        DetailsAddressPanel.Visibility = string.IsNullOrWhiteSpace(pop.Address) ? Visibility.Collapsed : Visibility.Visible;
        SelectedDetailsCard.Visibility = Visibility.Visible;
    }

    private async void PopComboBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: true, SelectedItem: null } box)
        {
            await LoadAvailablePopsAsync(box.Text);
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
            CustomerLoadingBar.Visibility = Visibility.Visible;
            SetStatus(CustomerStatusText, string.IsNullOrWhiteSpace(query) ? "Memuat daftar pelanggan..." : $"Mencari \"{query}\"...");

            var customers = await _api.SearchCustomersAsync(query, search.Token);
            if (search.IsCancellationRequested) return;
            CustomerComboBox.ItemsSource = customers;
            SetStatus(CustomerStatusText, customers.Count > 0 ? $"{customers.Count} pelanggan ditemukan." : "Pelanggan tidak ditemukan.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load customers");
            SetStatus(CustomerStatusText, ex is ApiException ? ex.Message : "Daftar pelanggan gagal dimuat. Coba lagi.", error: true);
        }
        finally
        {
            if (ReferenceEquals(_customerSearch, search)) CustomerLoadingBar.Visibility = Visibility.Hidden;
        }
    }

    private void CustomerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _enableRealTimeValidation = false;
        ClearErrors();

        if (CustomerComboBox.SelectedItem is not FoCustomer customer)
        {
            SelectedDetailsCard.Visibility = Visibility.Collapsed;
            NameFieldPanel.Visibility = PackageFieldPanel.Visibility = AddressFieldPanel.Visibility = Visibility.Visible;
            FullNameBox.Text = PackageMbpsBox.Text = AddressBox.Text = NotesBox.Text = string.Empty;
            PlacePackageField(nameVisible: true);
            return;
        }

        FullNameBox.Text = customer.FullName;
        PackageMbpsBox.Text = customer.PackageMbps > 0 ? customer.PackageMbps.ToString() : string.Empty;
        AddressBox.Text = customer.Address ?? string.Empty;
        NotesBox.Text = string.Empty;

        DetailsCardIcon.Kind = PackIconKind.Account;
        DetailsNameText.Text = customer.FullName;
        // The service number tells two customers of the same name apart.
        DetailsSiteIdText.Text = customer.ServiceNumber ?? string.Empty;
        DetailsSiteIdBadge.Visibility = string.IsNullOrWhiteSpace(customer.ServiceNumber) ? Visibility.Collapsed : Visibility.Visible;
        DetailsPackageText.Text = !string.IsNullOrWhiteSpace(customer.PackageName) ? customer.PackageName
            : customer.PackageMbps > 0 ? $"{customer.PackageMbps} Mbps" : string.Empty;
        DetailsPackageBadge.Visibility = string.IsNullOrWhiteSpace(DetailsPackageText.Text) ? Visibility.Collapsed : Visibility.Visible;
        DetailsAddressText.Text = customer.Address ?? "-";
        DetailsAddressPanel.Visibility = string.IsNullOrWhiteSpace(customer.Address) ? Visibility.Collapsed : Visibility.Visible;
        SelectedDetailsCard.Visibility = Visibility.Visible;

        // Only what billing left empty is asked for.
        NameFieldPanel.Visibility = string.IsNullOrWhiteSpace(FullNameBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        PackageFieldPanel.Visibility = string.IsNullOrWhiteSpace(PackageMbpsBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        AddressFieldPanel.Visibility = string.IsNullOrWhiteSpace(AddressBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        PlacePackageField(NameFieldPanel.Visibility == Visibility.Visible);

        if (PackageFieldPanel.Visibility == Visibility.Visible)
        {
            _enableRealTimeValidation = true;
            SetError(PackageMbpsBox, PackageWarningText, "Paket pelanggan belum tercatat. Isi kecepatannya (Mbps).");
        }
    }

    /// <summary>The package field takes the whole row when the name field is not asked for.</summary>
    private void PlacePackageField(bool nameVisible)
    {
        Grid.SetColumn(PackageFieldPanel, nameVisible ? 2 : 0);
        Grid.SetColumnSpan(PackageFieldPanel, nameVisible ? 1 : 3);
    }

    private async void CustomerComboBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: true, SelectedItem: null } box)
        {
            await LoadAvailableCustomersAsync(box.Text);
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
        NameFieldPanel.Visibility = PackageFieldPanel.Visibility = AddressFieldPanel.Visibility = Visibility.Visible;
        PlacePackageField(nameVisible: true);
        FullNameBox.Focus();
    }

    // ── Validation ─────────────────────────────────────────────────────────────

    /// <summary>A field in error: outlined in the error colour, the message under it.</summary>
    private static void SetError(Control field, TextBlock message, string? text)
    {
        if (text is null)
        {
            field.ClearValue(Control.BorderBrushProperty);
            message.Visibility = Visibility.Collapsed;
            return;
        }

        field.SetResourceReference(Control.BorderBrushProperty, "ErrorBrush");
        message.Text = text;
        message.Visibility = Visibility.Visible;
    }

    private void ClearErrors()
    {
        SetError(FullNameBox, NameWarningText, null);
        SetError(PackageMbpsBox, PackageWarningText, null);
        SetError(AddressBox, AddressWarningText, null);
    }

    /// <summary>A status line under a picker; in the error colour when something went wrong.</summary>
    private static void SetStatus(TextBlock line, string text, bool error = false)
    {
        line.Text = text;
        line.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorBrush" : "OnSurfaceVariantBrush");
    }

    private static string? NameError(string text) => string.IsNullOrWhiteSpace(text) ? "Nama pelanggan wajib diisi." : null;

    private static string? AddressError(string text) => string.IsNullOrWhiteSpace(text) ? "Alamat pelanggan wajib diisi." : null;

    private static string? PackageError(string text) =>
        string.IsNullOrWhiteSpace(text) ? "Kecepatan paket wajib diisi."
        : !int.TryParse(text.Trim(), out var mbps) || mbps <= 0 ? "Kecepatan harus angka bulat positif (Mbps)."
        : null;

    private bool ValidateForm()
    {
        if (_testMode == "pop")
        {
            if (PopComboBox.SelectedItem is PopOption) return true;
            SetStatus(PopStatusText, "Pilih POP dari daftar terlebih dahulu.", error: true);
            PopComboBox.Focus();
            return false;
        }

        _enableRealTimeValidation = true;
        var nameError = NameError(FullNameBox.Text);
        var packageError = PackageError(PackageMbpsBox.Text);
        var addressError = AddressError(AddressBox.Text);
        SetError(FullNameBox, NameWarningText, nameError);
        SetError(PackageMbpsBox, PackageWarningText, packageError);
        SetError(AddressBox, AddressWarningText, addressError);

        // A field in error that was hidden behind the details card is shown.
        if (nameError is not null) NameFieldPanel.Visibility = Visibility.Visible;
        if (packageError is not null) PackageFieldPanel.Visibility = Visibility.Visible;
        if (addressError is not null) AddressFieldPanel.Visibility = Visibility.Visible;
        PlacePackageField(NameFieldPanel.Visibility == Visibility.Visible);

        return nameError is null && packageError is null && addressError is null;
    }

    private void FullNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_enableRealTimeValidation) SetError(FullNameBox, NameWarningText, NameError(FullNameBox.Text));
    }

    private void PackageMbpsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_enableRealTimeValidation) SetError(PackageMbpsBox, PackageWarningText, PackageError(PackageMbpsBox.Text));
    }

    private void AddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_enableRealTimeValidation) SetError(AddressBox, AddressWarningText, AddressError(AddressBox.Text));
    }

    // ── Step 3: running ────────────────────────────────────────────────────────

    private async void StartTestBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsRunning)
        {
            Ui.Notify("Pengujian sebelumnya masih dihentikan. Tunggu beberapa detik lalu coba lagi.");
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
    /// session with its status, show the results. Cancelled ("Batalkan", or leaving the page), the
    /// session is closed as FAIL with a note saying so, and no results screen appears.
    /// </summary>
    private async Task RunSessionAsync(FoCustomer customer)
    {
        _lastCustomer = customer;
        _currentSessionId = null;
        ResetProgressUi();
        ShowStage(ProgressContainer);

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
            SubTaskDetailText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
            EndTestBtnText.Text = "Tutup";
            if (_currentSessionId is { } sessionId && run is not null)
            {
                await CloseSessionAsync(sessionId, run.OverallStatus, $"Pengujian terhenti: {ex.Message}");
            }
        }
        finally
        {
            _runCts = null;
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

    private enum StepState { Pending, Active, Done }

    /// <summary>A stepper step: pending (tonal, muted), active (primary), done (primary with a check).</summary>
    private static void SetStep(Border circle, PackIcon icon, TextBlock label, StepState state, PackIconKind kind)
    {
        var active = state != StepState.Pending;
        circle.SetResourceReference(Border.BackgroundProperty, active ? "PrimaryBrush" : "SurfaceContainerHighestBrush");
        icon.SetResourceReference(Control.ForegroundProperty, active ? "OnPrimaryBrush" : "OnSurfaceVariantBrush");
        label.SetResourceReference(TextBlock.ForegroundProperty, active ? "OnSurfaceBrush" : "OnSurfaceVariantBrush");
        icon.Kind = state == StepState.Done ? PackIconKind.Check : kind;
    }

    private void SetSteps(StepState network, StepState apps, StepState bandwidth)
    {
        SetStep(Step1Circle, Step1Icon, Step1Text, network, PackIconKind.LanConnect);
        SetStep(Step2Circle, Step2Icon, Step2Text, apps, PackIconKind.Web);
        SetStep(Step3Circle, Step3Icon, Step3Text, bandwidth, PackIconKind.Speedometer);
    }

    private void ResetProgressUi()
    {
        UnsavedStatusError = null;
        DiagnosticLogsPanel.Children.Clear();
        AnimateProgressBar(Line1, 0);
        AnimateProgressBar(Line2, 0);
        AnimateProgressBar(SubTaskProgressBar, 0);
        SetSteps(StepState.Active, StepState.Pending, StepState.Pending);

        CurrentActivityText.Text = "Menyiapkan pengujian...";
        TaskCountText.Text = "Task 1 of 7";
        SubTaskDetailText.Text = "Menyiapkan mesin diagnostik...";
        SubTaskDetailText.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariantBrush");
        SaveReportBtn.Visibility = Visibility.Collapsed;
        EndTestBtnText.Text = "Batalkan pengujian";
    }

    private IProgress<(int percent, string status)> CreateProgress() => new Progress<(int percent, string status)>(p =>
    {
        // Progress posts asynchronously: an update arriving after the run ended changes nothing.
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
            SetSteps(StepState.Active, StepState.Pending, StepState.Pending);
            AnimateProgressBar(Line1, taskNum / 5.0 * 100);
        }
        else if (taskNum == 6)
        {
            SetSteps(StepState.Done, StepState.Active, StepState.Pending);
            AnimateProgressBar(Line1, 100);
            AnimateProgressBar(Line2, 50);
        }
        else
        {
            var finished = p.percent >= 100;
            SetSteps(StepState.Done, StepState.Done, finished ? StepState.Done : StepState.Active);
            AnimateProgressBar(Line1, 100);
            AnimateProgressBar(Line2, 100);
        }
    });

    private static void AnimateProgressBar(ProgressBar bar, double toValue) =>
        bar.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation
        {
            To = toValue,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

    /// <summary>"Batalkan" while running cancels the run (after a yes); afterwards it closes the screen.</summary>
    private async void EndTestBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsRunning)
        {
            var stop = await Ui.ConfirmAsync("Hentikan pengujian?",
                "Pengujian yang sedang berjalan dihentikan dan sesinya dicatat sebagai dibatalkan (FAIL).",
                "Hentikan", cancelText: "Lanjutkan", destructive: true);
            if (!stop) return;

            _runCts?.Cancel();
            Ui.Notify("Pengujian dihentikan.");
        }

        ShowStage(LocationContainer);
    }

    // ── Step 4: results ────────────────────────────────────────────────────────

    private void NewTestFromResults_Click(object sender, RoutedEventArgs e) => ShowStage(LocationContainer);

    private void SaveReportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSessionId is { } id && Window.GetWindow(this) is MainShell shell)
        {
            shell.OpenReport(id);
        }
    }

    /// <summary>
    /// The results of the run just finished, as measured here — the same list and verdict that were
    /// sent — with a warning naming any result the server did not store.
    /// </summary>
    private void ShowResults(TestRun run)
    {
        var results = run.Results.ToList();
        var passCount = results.Count(r => r.Status == TestStatus.Pass);
        var status = StatusVisual.Of(run.OverallStatus);

        ShowStage(ResultsContainer);
        ResultCardsPanel.Children.Clear();

        ResultSummaryText.Text = $"{_lastCustomer?.FullName} · {results.Count} pengujian · {DateTime.Now:dd MMM yyyy, HH:mm}";
        ResultStatusText.Text = status.Label;
        ResultScoreText.Text = $"{passCount} dari {results.Count} lulus";
        ResultStatusIcon.Kind = status.Icon;
        ResultStatusBadge.SetResourceReference(Border.BackgroundProperty, status.ContainerKey);
        ResultStatusIcon.SetResourceReference(Control.ForegroundProperty, status.OnContainerKey);
        ResultStatusText.SetResourceReference(TextBlock.ForegroundProperty, status.OnContainerKey);
        ResultScoreText.SetResourceReference(TextBlock.ForegroundProperty, status.OnContainerKey);

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
        RetryFailedBtn.Visibility = run.OverallStatus == TestOverallStatus.Pass ? Visibility.Collapsed : Visibility.Visible;

        AddResultGroups(results);
    }

    /// <summary>
    /// Tests the same site again in a new session: the earlier session stays in the history as it
    /// was, and the new one holds one complete, clean set of results (the API keeps every result it
    /// is sent, so re-running into the old session would mix old failures with new passes).
    /// </summary>
    private async void RetryFailedBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCustomer is null || IsRunning) return;

        var retry = await Ui.ConfirmAsync("Uji ulang lokasi ini?",
            "Seluruh pengujian dijalankan lagi sebagai sesi baru. Sesi sebelumnya tetap tersimpan di riwayat.", "Uji ulang");
        if (!retry) return;

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

    private void AddResultGroups(List<FoTestResult> results)
    {
        var groups = new (string Name, string[] Types, bool Open)[]
        {
            ("Jaringan", [TestTypes.PingGateway, TestTypes.PingDns, TestTypes.NslookupNasional, TestTypes.NslookupInternasional, TestTypes.PingDomainLokal], true),
            ("Aplikasi", [TestTypes.BrowsingTest, TestTypes.StreamingTest, TestTypes.SocialMediaTest], false),
            ("Bandwidth", [TestTypes.SpeedtestFast, TestTypes.SpeedtestOokla], false),
        };

        foreach (var (name, types, open) in groups)
        {
            var groupResults = results.Where(r => types.Contains(r.TestType)).ToList();
            if (groupResults.Count > 0)
            {
                ResultCardsPanel.Children.Add(CreateGroup(name, groupResults, open));
            }
        }
    }

    /// <summary>A group of results in one card: name, how many passed, and each result's row.</summary>
    private static Border CreateGroup(string name, List<FoTestResult> groupResults, bool expandByDefault)
    {
        var passCount = groupResults.Count(r => r.Status == TestStatus.Pass);
        var allPassed = passCount == groupResults.Count;
        var groupStatus = allPassed ? StatusVisual.Pass : passCount > 0 ? StatusVisual.Partial : StatusVisual.Fail;

        var header = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Ui.Text(name, "Text.TitleMedium");
        title.VerticalAlignment = VerticalAlignment.Center;
        var chip = Ui.StatusChip(groupStatus with { Label = $"{passCount}/{groupResults.Count} lulus" });
        Grid.SetColumn(chip, 1);
        header.Children.Add(title);
        header.Children.Add(chip);

        var rows = new StackPanel();
        foreach (var result in groupResults)
        {
            rows.Children.Add(BuildResultRow(result));
        }

        var expander = new Expander
        {
            Header = header,
            Content = rows,
            // A group with a failure opens by itself.
            IsExpanded = !allPassed || expandByDefault,
            Background = System.Windows.Media.Brushes.Transparent,
        };

        var card = new Border { Child = expander, Margin = new Thickness(0, 0, 0, 16) };
        card.SetResourceReference(FrameworkElement.StyleProperty, "M3.Card");
        card.Padding = new Thickness(0);
        return card;
    }

    /// <summary>One result: its row (click for the measured values), status chip on the right.</summary>
    private static Border BuildResultRow(FoTestResult result)
    {
        var status = StatusVisual.Of(result.Status);
        var detailRows = ResultFormatter.Rows(result.TestType, result.ResultData?.RootElement);

        var lead = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        lead.SetResourceReference(Border.BackgroundProperty, status.ContainerKey);
        var leadIcon = new PackIcon { Kind = GetTestIcon(result.TestType), Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        leadIcon.SetResourceReference(Control.ForegroundProperty, status.OnContainerKey);
        lead.Child = leadIcon;

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Ui.Text(ResultFormatter.TestName(result.TestType), "Text.BodyLarge"));
        if (!string.IsNullOrEmpty(result.Target))
        {
            names.Children.Add(Ui.Text(result.Target, "Text.Supporting"));
        }

        var chevron = new PackIcon
        {
            Kind = PackIconKind.ChevronDown, Width = 20, Height = 20, Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = detailRows.Count > 0 ? Visibility.Visible : Visibility.Hidden,
        };
        chevron.SetResourceReference(Control.ForegroundProperty, "OnSurfaceVariantBrush");

        var head = new Grid { Background = System.Windows.Media.Brushes.Transparent, Cursor = detailRows.Count > 0 ? System.Windows.Input.Cursors.Hand : null };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusChip = Ui.StatusChip(status);
        Grid.SetColumn(names, 1);
        Grid.SetColumn(statusChip, 2);
        Grid.SetColumn(chevron, 3);
        head.Children.Add(lead);
        head.Children.Add(names);
        head.Children.Add(statusChip);
        head.Children.Add(chevron);

        var body = new StackPanel();
        body.Children.Add(head);

        if (detailRows.Count > 0)
        {
            var details = new Border { Margin = new Thickness(56, 12, 0, 0), Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(8), Visibility = Visibility.Collapsed };
            details.SetResourceReference(Border.BackgroundProperty, "SurfaceContainerLowBrush");
            var list = new StackPanel();
            foreach (var (label, value) in detailRows)
            {
                AddDetailRow(list, label, value);
            }
            details.Child = list;
            body.Children.Add(details);

            head.MouseLeftButtonUp += (_, _) =>
            {
                var open = details.Visibility != Visibility.Visible;
                details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                chevron.Kind = open ? PackIconKind.ChevronUp : PackIconKind.ChevronDown;
            };
        }

        var row = new Border { Padding = new Thickness(16, 12, 16, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = body };
        row.SetResourceReference(Border.BorderBrushProperty, "OutlineVariantBrush");
        return row;
    }

    private static void AddDetailRow(StackPanel panel, string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labelText = Ui.Text(label, "Text.Supporting");
        var valueText = Ui.Text(value, "Text.BodyMedium");
        valueText.TextAlignment = TextAlignment.Right;
        valueText.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(valueText, 1);
        row.Children.Add(labelText);
        row.Children.Add(valueText);
        panel.Children.Add(row);
    }

    private static PackIconKind GetTestIcon(string testType) => testType switch
    {
        TestTypes.PingGateway or TestTypes.PingDns or TestTypes.PingDomainLokal => PackIconKind.LanConnect,
        TestTypes.NslookupNasional or TestTypes.NslookupInternasional => PackIconKind.Dns,
        TestTypes.BrowsingTest => PackIconKind.Web,
        TestTypes.StreamingTest => PackIconKind.PlayCircleOutline,
        TestTypes.SocialMediaTest => PackIconKind.AccountGroupOutline,
        TestTypes.SpeedtestFast or TestTypes.SpeedtestOokla => PackIconKind.Speedometer,
        _ => PackIconKind.TestTube,
    };
}
