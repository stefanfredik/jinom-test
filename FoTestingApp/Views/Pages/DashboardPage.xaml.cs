using System.Windows;
using System.Windows.Controls;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services;
using FoTestingApp.Services.Core;
using MaterialDesignThemes.Wpf;
using Serilog;

namespace FoTestingApp.Views.Pages;

/// <summary>
/// The history and its reports: counts by status, sessions per day (click a day to see it),
/// filters that apply as they change, the sessions with a report download on each row, and one
/// session's results beside the list. A failed load or a list the server capped is said, never
/// shown as zero; a late answer for an earlier filter or row is dropped.
/// </summary>
public partial class DashboardPage : Page
{
    private readonly ApiService _api = new();

    /// <summary>The newest list and detail asked for; an older answer arriving late is ignored.</summary>
    private int _listRequest;
    private int _detailRequest;
    private FoTestSession? _shownSession;

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            ThemeService.Changed += RepaintStatuses;
            await LoadSessionsAsync();
        };
        Unloaded += (_, _) => ThemeService.Changed -= RepaintStatuses;
    }

    /// <summary>The status column's colours come from converters: after a theme switch, re-run them.</summary>
    private void RepaintStatuses() => SessionsGrid.Items.Refresh();

    private string? SelectedStatus() =>
        new[] { StatusAll }.Concat(LogicalTreeHelper.GetChildren(StatusAll.Parent).OfType<RadioButton>())
            .FirstOrDefault(r => r.IsChecked == true)?.Tag as string is { Length: > 0 } status ? status : null;

    private async Task LoadSessionsAsync()
    {
        var request = ++_listRequest;
        ListNotice.Visibility = Visibility.Collapsed;

        var from = FromDatePicker.SelectedDate;
        var to = ToDatePicker.SelectedDate;
        if (from is not null && to is not null && from > to)
        {
            ShowNotice("Tanggal awal setelah tanggal akhir.", error: true);
            return;
        }

        try
        {
            var sessions = await _api.GetSessionsAsync(from, to, null, SelectedStatus());
            if (request != _listRequest) return;

            SessionsGrid.ItemsSource = sessions;
            TrendChart.SetSessions(sessions, from, to);
            TrendTitle.Text = from is not null && to is not null && (to.Value - from.Value).TotalDays > 62 ? "Sesi per minggu" : "Sesi per hari";
            EmptyState.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TotalSessionCount.Text = sessions.Count.ToString("N0");
            TotalPassCount.Text = sessions.Count(s => s.OverallStatus == TestOverallStatus.Pass).ToString("N0");
            TotalPartialCount.Text = sessions.Count(s => s.OverallStatus == TestOverallStatus.Partial).ToString("N0");
            TotalFailCount.Text = sessions.Count(s => s.OverallStatus == TestOverallStatus.Fail).ToString("N0");

            if (sessions.Count >= ApiService.SessionListLimit)
            {
                // The server sends at most this many: the counts cover only the newest of them.
                ShowNotice($"Hanya {ApiService.SessionListLimit} sesi terbaru yang tampil, dan angka di atas hanya menghitung sesi itu. Persempit rentang tanggal untuk data lengkap.", error: false);
            }
        }
        catch (Exception ex)
        {
            if (request != _listRequest) return;
            Log.Error(ex, "Loading the history failed");
            SessionsGrid.ItemsSource = null;
            EmptyState.Visibility = Visibility.Collapsed;
            TotalSessionCount.Text = TotalPassCount.Text = TotalFailCount.Text = TotalPartialCount.Text = "-";
            ShowNotice(ex is ApiException ? ex.Message : $"Riwayat gagal dimuat: {ex.Message}", error: true);
        }
    }

    private void ShowNotice(string text, bool error)
    {
        ListNotice.Text = text;
        ListNotice.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorBrush" : "OnSurfaceVariantBrush");
        ListNotice.Visibility = Visibility.Visible;
    }

    private bool _settingPeriod;

    private async void Filter_Changed(object sender, RoutedEventArgs e)
    {
        // The default chip is checked while the page is still being built; a period set from the
        // chart changes both dates and loads once.
        if (IsLoaded && !_settingPeriod) await LoadSessionsAsync();
    }

    /// <summary>A bar of the chart was clicked: the list shows that day (or week).</summary>
    private async void TrendChart_PeriodSelected(DateTime from, DateTime to)
    {
        _settingPeriod = true;
        FromDatePicker.SelectedDate = from;
        ToDatePicker.SelectedDate = to;
        _settingPeriod = false;
        await LoadSessionsAsync();
        Ui.Notify(from == to ? $"Menampilkan sesi {from:d MMMM yyyy}." : $"Menampilkan sesi {from:d MMM} – {to:d MMM yyyy}.", "Semua tanggal", () =>
        {
            _settingPeriod = true;
            FromDatePicker.SelectedDate = ToDatePicker.SelectedDate = null;
            _settingPeriod = false;
            _ = LoadSessionsAsync();
        });
    }

    /// <summary>A row's report, without opening the row.</summary>
    private async void DownloadRow_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: FoTestSession session } button)
        {
            button.IsEnabled = false;
            try
            {
                await ReportDownload.SaveAsync(session.Id, session.CertificationId, session.TestDate);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadSessionsAsync();

    private async void SessionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionsGrid.SelectedItem is FoTestSession session)
        {
            await ShowSessionDetail(session);
        }
    }

    private async Task ShowSessionDetail(FoTestSession session)
    {
        var request = ++_detailRequest;
        _shownSession = session;
        DetailTitle.Text = session.Customer?.FullName ?? "Detail sesi";
        DetailSubtitle.Text = $"{session.CertificationId} · {session.TestDate:dd MMM yyyy, HH:mm}";
        DetailStatus.Content = Ui.StatusChip(StatusVisual.Of(session.OverallStatus));
        DetailResultsPanel.Children.Clear();
        DetailCharts.Visibility = Visibility.Collapsed;
        DetailColumn.Width = new GridLength(400);
        DetailPanel.Visibility = Visibility.Visible;

        string? message = null;
        List<FoTestResult> results = [];
        try
        {
            results = await _api.GetResultsBySessionAsync(session.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Results of session {SessionId} could not be loaded", session.Id);
            message = ex is ApiException ? ex.Message : "Hasil pengujian gagal dimuat.";
        }

        if (request != _detailRequest) return;

        if (message is not null || results.Count == 0)
        {
            var text = Ui.Text(message ?? "Sesi ini belum punya hasil pengujian.", "Text.BodyMedium", message is null ? "OnSurfaceVariantBrush" : "ErrorBrush");
            text.Margin = new Thickness(16);
            DetailResultsPanel.Children.Add(text);
            return;
        }

        var latency = ChartData.Latency(results);
        var speed = ChartData.Download(results);
        DetailLatency.Items = latency;
        DetailSpeed.Items = speed;
        DetailLatencyTitle.Visibility = DetailLatency.Visibility = latency.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailSpeedTitle.Visibility = DetailSpeed.Visibility = speed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailCharts.Visibility = latency.Count + speed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var result in results)
        {
            DetailResultsPanel.Children.Add(Ui.ResultRow(
                ResultFormatter.TestName(result.TestType),
                ResultFormatter.Summary(result.TestType, result.ResultData?.RootElement, result.Status == TestStatus.Pass),
                StatusVisual.Of(result.Status),
                GetTestIcon(result.TestType)));
        }
    }

    private void CloseDetail_Click(object sender, RoutedEventArgs e)
    {
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailColumn.Width = new GridLength(0);
        SessionsGrid.SelectedItem = null;
        _shownSession = null;
    }

    private async void DetailReport_Click(object sender, RoutedEventArgs e)
    {
        if (_shownSession is not { } session) return;

        DetailReportBtn.IsEnabled = false;
        try
        {
            await ReportDownload.SaveAsync(session.Id, session.CertificationId, session.TestDate);
        }
        finally
        {
            DetailReportBtn.IsEnabled = true;
        }
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
