using System.Windows.Controls;
using FoTestingApp.Services;

namespace FoTestingApp.Views.Pages;

public partial class DashboardPage : Page
{
    private readonly DatabaseService _db = new();

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadDashboardAsync();
    }

    private async System.Threading.Tasks.Task LoadDashboardAsync()
    {
        var sessions = await _db.GetSessionsAsync();
        RecentSessionsGrid.ItemsSource = sessions;

        TotalSessionCount.Text = sessions.Count.ToString();
        TotalPassCount.Text = sessions.Count(s => s.OverallStatus == Models.TestOverallStatus.Pass).ToString();
        TotalFailCount.Text = sessions.Count(s => s.OverallStatus == Models.TestOverallStatus.Fail).ToString();
    }
}
