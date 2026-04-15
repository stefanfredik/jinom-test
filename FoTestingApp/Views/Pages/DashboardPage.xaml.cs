using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FoTestingApp.Models;
using FoTestingApp.Services;
using MaterialDesignThemes.Wpf;
using Serilog;

namespace FoTestingApp.Views.Pages;

public partial class DashboardPage : Page
{
    private readonly ApiService _api = new();

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadSessionsAsync();
    }

    private async Task LoadSessionsAsync()
    {
        try
        {
            var from = FromDatePicker.SelectedDate;
            var to = ToDatePicker.SelectedDate;
            var statusItem = StatusFilter.SelectedItem as ComboBoxItem;
            var statusText = statusItem?.Content?.ToString();
            string? status = statusText == "Semua" ? null : statusText;

            Log.Information("Loading sessions with from={From}, to={To}, status={Status}", from, to, status);

            var sessions = await _api.GetSessionsAsync(from, to, null, status);
            Log.Information("Got {Count} sessions from API", sessions.Count);

            SessionsGrid.ItemsSource = sessions;

            TotalSessionCount.Text = sessions.Count.ToString();
            TotalPassCount.Text = sessions.Count(s => s.OverallStatus == TestOverallStatus.Pass).ToString();
            TotalFailCount.Text = sessions.Count(s => s.OverallStatus == TestOverallStatus.Fail).ToString();
            
            Log.Information("Dashboard stats updated");
        }
        catch (Exception ex)
        {
            // Log tangkapan atau tampilkan jika perlu
            Log.Error(ex, "Error in LoadSessionsAsync");
        }
    }

    private async void FilterBtn_Click(object sender, RoutedEventArgs e) =>
        await LoadSessionsAsync();

    private async void SessionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionsGrid.SelectedItem is not FoTestSession session) return;
        await ShowSessionDetail(session);
    }

    private async Task ShowSessionDetail(FoTestSession session)
    {
        DetailTitle.Text = session.Customer?.FullName ?? "Detail";
        DetailSubtitle.Text = $"{session.CertificationId} — {session.TestDate:dd MMM yyyy HH:mm}";

        DetailResultsPanel.Children.Clear();

        var results = await _api.GetResultsBySessionAsync(session.Id);

        if (results.Count == 0)
        {
            DetailResultsPanel.Children.Add(new TextBlock
            {
                Text = "Tidak ada data hasil pengujian.",
                Foreground = (Brush)FindResource("TextLightBrush"),
                FontSize = 13,
                Margin = new Thickness(0, 16, 0, 0),
            });
        }
        else
        {
            foreach (var result in results)
            {
                DetailResultsPanel.Children.Add(CreateResultCard(result));
            }
        }

        // Show panel
        DetailColumn.Width = new GridLength(360);
        DetailPanel.Visibility = Visibility.Visible;
    }

    private void CloseDetail_Click(object sender, RoutedEventArgs e)
    {
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailColumn.Width = new GridLength(0);
        SessionsGrid.SelectedItem = null;
    }

    private Border CreateResultCard(FoTestResult result)
    {
        var isPassed = result.Status == TestStatus.Pass;
        var statusColor = isPassed
            ? (Brush)FindResource("PassGreenBrush")
            : (Brush)FindResource("FailRedBrush");
        var typeName = FormatTestTypeName(result.TestType);
        var iconKind = GetTestIcon(result.TestType);
        var detail = ExtractDetailText(result);

        var card = new Border
        {
            Background = (Brush)FindResource("AppBackgroundBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon
        var iconBorder = new Border
        {
            Width = 36, Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = isPassed ? new SolidColorBrush(Color.FromArgb(0x1A, 0x22, 0xC5, 0x5E))
                                  : new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new PackIcon { Kind = iconKind, Width = 18, Height = 18, Foreground = statusColor,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        iconBorder.Child = icon;
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        // Text
        var textPanel = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(new TextBlock
        {
            Text = typeName, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextDarkBrush"),
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = detail, FontSize = 11,
            Foreground = (Brush)FindResource("TextLightBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });
        // Show target if available
        if (!string.IsNullOrEmpty(result.Target))
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = result.Target, FontSize = 10,
                Foreground = (Brush)FindResource("TextLightBrush"),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        // Status chip
        var statusBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 3),
            Background = isPassed ? new SolidColorBrush(Color.FromArgb(0x1A, 0x22, 0xC5, 0x5E))
                                  : new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        statusBorder.Child = new TextBlock
        {
            Text = isPassed ? "PASS" : "FAIL",
            FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = statusColor,
        };
        Grid.SetColumn(statusBorder, 2);
        grid.Children.Add(statusBorder);

        card.Child = grid;
        return card;
    }

    private static string FormatTestTypeName(string testType) => testType switch
    {
        TestTypes.PingGateway => "Ping Gateway",
        TestTypes.PingDns => "Ping DNS",
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

    private static string ExtractDetailText(FoTestResult result)
    {
        if (result.ResultData is null) return "-";

        try
        {
            var root = result.ResultData.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                var doc = JsonDocument.Parse(root.GetString() ?? "{}");
                root = doc.RootElement;
            }

            return result.TestType switch
            {
                TestTypes.PingGateway or TestTypes.PingDns or TestTypes.PingDomainLokal =>
                    $"Avg: {root.GetProperty("avg_ms").GetInt64()}ms | Max: {root.GetProperty("max_ms").GetInt64()}ms | RTO: {root.GetProperty("rto").GetInt32()}",

                TestTypes.NslookupNasional or TestTypes.NslookupInternasional =>
                    string.Join(", ", root.GetProperty("domains").EnumerateObject()
                        .Select(d => $"{d.Name}: {d.Value.GetString()}")),

                TestTypes.BrowsingTest =>
                    string.Join(" | ", root.GetProperty("results").EnumerateObject()
                        .Select(r => $"{new Uri(r.Name).Host}: {r.Value.GetDouble():F1}s")),

                TestTypes.StreamingTest or TestTypes.SocialMediaTest =>
                    string.Join(" | ", root.GetProperty("results").EnumerateObject()
                        .Select(r => $"{new Uri(r.Name).Host}: {r.Value.GetString()}")),

                TestTypes.SpeedtestFast =>
                    $"↓ Download: {root.GetProperty("download_mbps").GetDouble():F1} Mbps",

                TestTypes.SpeedtestOokla =>
                    $"↓ {root.GetProperty("download_mbps").GetDouble():F1} Mbps | ↑ {root.GetProperty("upload_mbps").GetDouble():F1} Mbps",

                _ => "-",
            };
        }
        catch
        {
            return "-";
        }
    }
}
