using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services;
using MaterialDesignThemes.Wpf;

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

            var testId = $"TEST-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";
            var session = new FoTestSession
            {
                CertificationId = testId,
                CustomerId = customerId,
                TechnicianId = SessionManager.CurrentUser?.Id ?? 0,
                TestDate = DateTime.Now,
                StartTime = DateTime.Now,
                OverallStatus = TestOverallStatus.Fail,
            };

            _currentSessionId = await _api.CreateTestSessionAsync(session);

            await RunAllTestsAsync(_currentSessionId.Value, customer.PackageMbps);

            // Transition to results view
            await ShowResultsAsync(_currentSessionId.Value);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Test execution failed");
            CurrentActivityText.Text = "Diagnostics Failed";
            EndTestBtnText.Text = "TUTUP";
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

    private void NewTestFromResults_Click(object sender, RoutedEventArgs e)
    {
        ResultsContainer.Visibility = Visibility.Collapsed;
        LandingContainer.Visibility = Visibility.Visible;
    }

    // ── Results Display ───────────────────────────────────────────────────────

    private async Task ShowResultsAsync(int sessionId)
    {
        var results = await _api.GetResultsBySessionAsync(sessionId);

        ProgressContainer.Visibility = Visibility.Collapsed;
        ResultsContainer.Visibility = Visibility.Visible;
        ResultCardsPanel.Children.Clear();

        var passCount = results.Count(r => r.Status == TestStatus.Pass);
        var totalCount = results.Count;

        ResultSummaryText.Text = $"{totalCount} pengujian dilakukan — {DateTime.Now:dd MMM yyyy, HH:mm}";
        ResultScoreText.Text = $"{passCount}/{totalCount} test passed";

        if (passCount == totalCount)
        {
            ResultStatusText.Text = "PASS";
            ResultStatusText.Foreground = (Brush)FindResource("PassGreenBrush");
        }
        else if (passCount > 0)
        {
            ResultStatusText.Text = "PARTIAL";
            ResultStatusText.Foreground = (Brush)FindResource("WarnYellowBrush");
        }
        else
        {
            ResultStatusText.Text = "FAIL";
            ResultStatusText.Foreground = (Brush)FindResource("FailRedBrush");
        }

        SaveReportBtn.Visibility = Visibility.Visible;

        foreach (var result in results)
        {
            ResultCardsPanel.Children.Add(BuildResultCard(result));
        }
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
        if (result.ResultData is null) return;

        try
        {
            var root = result.ResultData.RootElement;

            switch (result.TestType)
            {
                case TestTypes.PingGateway or TestTypes.PingDns or TestTypes.PingDomainLokal:
                    AddDetailRow(panel, "Rata-rata", $"{root.GetProperty("avg_ms").GetInt64()} ms");
                    AddDetailRow(panel, "Minimum", $"{root.GetProperty("min_ms").GetInt64()} ms");
                    AddDetailRow(panel, "Maksimum", $"{root.GetProperty("max_ms").GetInt64()} ms");
                    AddDetailRow(panel, "RTO (Packet Loss)", root.GetProperty("rto").GetInt32().ToString());
                    break;

                case TestTypes.NslookupNasional or TestTypes.NslookupInternasional:
                    foreach (var domain in root.GetProperty("domains").EnumerateObject())
                    {
                        AddDetailRow(panel, domain.Name, domain.Value.GetString() ?? "-");
                    }
                    break;

                case TestTypes.BrowsingTest:
                    foreach (var r in root.GetProperty("results").EnumerateObject())
                    {
                        var keyName = r.Name.StartsWith("http") ? new Uri(r.Name).Host : r.Name.Replace("_", " ");
                        AddDetailRow(panel, keyName, $"{r.Value.GetDouble():F2} detik");
                    }
                    break;

                case TestTypes.StreamingTest or TestTypes.SocialMediaTest:
                    foreach (var r in root.GetProperty("results").EnumerateObject())
                    {
                        var keyName = r.Name.StartsWith("http") ? new Uri(r.Name).Host : r.Name.Replace("_", " ");
                        AddDetailRow(panel, keyName, r.Value.GetString() ?? "-");
                    }
                    break;

                case TestTypes.SpeedtestFast:
                    AddDetailRow(panel, "Download", $"{root.GetProperty("download_mbps").GetDouble():F1} Mbps");
                    break;

                case TestTypes.SpeedtestOokla:
                    AddDetailRow(panel, "Download", $"{root.GetProperty("download_mbps").GetDouble():F1} Mbps");
                    AddDetailRow(panel, "Upload", $"{root.GetProperty("upload_mbps").GetDouble():F1} Mbps");
                    if (root.TryGetProperty("ping_ms", out var ping))
                        AddDetailRow(panel, "Ping", $"{ping.GetDouble():F0} ms");
                    if (root.TryGetProperty("server", out var server))
                        AddDetailRow(panel, "Server", server.GetString() ?? "-");
                    break;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to parse result data for {TestType}", result.TestType);
            panel.Children.Add(new TextBlock
            {
                Text = "Data tidak tersedia", FontSize = 12,
                Foreground = (Brush)FindResource("TextLightBrush"), FontStyle = FontStyles.Italic,
            });
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

    private static string FormatTestTypeName(string testType) => testType switch
    {
        TestTypes.PingGateway => "Ping Gateway ISP",
        TestTypes.PingDns => "Ping DNS Server",
        TestTypes.NslookupNasional => "NSLookup Nasional",
        TestTypes.NslookupInternasional => "NSLookup Internasional",
        TestTypes.PingDomainLokal => "Ping Domain Lokal",
        TestTypes.BrowsingTest => "Browsing Test",
        TestTypes.StreamingTest => "Streaming Test",
        TestTypes.SocialMediaTest => "Social Media Test",
        TestTypes.SpeedtestFast => "Speedtest Fast.com",
        TestTypes.SpeedtestOokla => "Speedtest Ookla",
        _ => testType,
    };

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
}
