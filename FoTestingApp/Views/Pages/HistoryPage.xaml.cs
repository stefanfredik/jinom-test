using System.Windows;
using System.Windows.Controls;
using FoTestingApp.Services;

namespace FoTestingApp.Views.Pages;

public partial class HistoryPage : Page
{
    private readonly ApiService _api = new();

    public HistoryPage()
    {
        InitializeComponent();
        StatusFilter.SelectedIndex = 0;
        Loaded += async (_, _) => await LoadSessionsAsync();
    }

    private async Task LoadSessionsAsync()
    {
        var from = FromDatePicker.SelectedDate;
        var to = ToDatePicker.SelectedDate;
        var statusItem = StatusFilter.SelectedItem as ComboBoxItem;
        string? status = statusItem?.Content?.ToString() == "Semua" ? null : statusItem?.Content?.ToString();

        var sessions = await _api.GetSessionsAsync(from, to, null, status);
        SessionsGrid.ItemsSource = sessions;
    }

    private async void FilterBtn_Click(object sender, RoutedEventArgs e) =>
        await LoadSessionsAsync();
}
