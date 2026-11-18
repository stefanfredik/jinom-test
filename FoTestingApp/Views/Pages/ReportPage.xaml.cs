using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Serilog;

namespace FoTestingApp.Views.Pages;

/// <summary>
/// A session's certificate as a PDF. The session is read fresh from the server (customer,
/// technician, every result); while the PDF is made the actions wait; the outcome is said under them.
/// </summary>
public partial class ReportPage : Page
{
    private readonly ApiService _api = new();
    private readonly ReportService _report = new();
    private readonly int? _sessionId;

    public ReportPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadSessionsAsync();
    }

    /// <summary>Opened from a session's results: that session is selected.</summary>
    public ReportPage(int sessionId) : this()
    {
        _sessionId = sessionId;
    }

    private enum StatusKind { Info, Success, Error }

    private void ShowStatus(string text, StatusKind kind)
    {
        var (icon, brush) = kind switch
        {
            StatusKind.Success => (PackIconKind.CheckCircleOutline, "StatusOnlineBrush"),
            StatusKind.Error => (PackIconKind.AlertCircleOutline, "ErrorBrush"),
            _ => (PackIconKind.InformationOutline, "OnSurfaceVariantBrush"),
        };
        StatusIcon.Kind = icon;
        StatusIcon.SetResourceReference(Control.ForegroundProperty, brush);
        ReportStatus.Text = text;
        ReportStatus.SetResourceReference(TextBlock.ForegroundProperty, kind == StatusKind.Error ? "ErrorBrush" : "OnSurfaceBrush");
        StatusLine.Visibility = Visibility.Visible;
    }

    private void SetWorking(bool working)
    {
        WorkingBar.Visibility = working ? Visibility.Visible : Visibility.Hidden;
        PrintBtn.IsEnabled = PreviewBtn.IsEnabled = ExportBtn.IsEnabled = SessionComboBox.IsEnabled = !working;
    }

    private async Task LoadSessionsAsync()
    {
        SetWorking(true);
        try
        {
            var sessions = await _api.GetSessionsAsync();

            // The session just tested may be older than the newest the server lists: fetch it directly.
            if (_sessionId is { } id && sessions.All(s => s.Id != id) && await _api.GetSessionAsync(id) is { } current)
            {
                sessions.Insert(0, current);
            }

            SessionComboBox.ItemsSource = sessions;
            if (_sessionId.HasValue)
            {
                SessionComboBox.SelectedItem = sessions.FirstOrDefault(s => s.Id == _sessionId);
            }
            if (sessions.Count == 0)
            {
                ShowStatus("Belum ada sesi pengujian.", StatusKind.Info);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading sessions for the report failed");
            ShowStatus(ex is ApiException ? ex.Message : $"Daftar sesi gagal dimuat: {ex.Message}", StatusKind.Error);
        }
        finally
        {
            SetWorking(false);
        }
    }

    private void SessionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionComboBox.SelectedItem is not FoTestSession session)
        {
            SessionSummary.Visibility = Visibility.Collapsed;
            return;
        }

        SummaryTitle.Text = session.Customer?.FullName ?? session.CertificationId;
        SummaryText.Text = $"{session.CertificationId} · {session.Customer?.SiteTypeText ?? "-"} · {session.TestDate:dd MMM yyyy, HH:mm}";
        SummaryStatus.Content = Ui.StatusChip(StatusVisual.Of(session.OverallStatus));
        SessionSummary.Visibility = Visibility.Visible;
        StatusLine.Visibility = Visibility.Collapsed;
    }

    private async void PreviewBtn_Click(object sender, RoutedEventArgs e) =>
        await MakePdfAsync("Membuat pratinjau...", async session =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"{session.CertificationId}.pdf");
            await Task.Run(() => _report.GeneratePdf(session, path));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return "Pratinjau dibuka di penampil PDF.";
        });

    private async void ExportBtn_Click(object sender, RoutedEventArgs e) =>
        await MakePdfAsync("Menyimpan PDF...", async session =>
        {
            var dialog = new SaveFileDialog
            {
                Title = "Simpan laporan PDF",
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"Laporan_{session.CertificationId}_{session.TestDate:yyyyMMdd}.pdf",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            };
            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            await Task.Run(() => _report.GeneratePdf(session, dialog.FileName));
            Log.Information("PDF exported: {Path}", dialog.FileName);
            return $"Laporan disimpan: {dialog.FileName}";
        });

    private async void PrintBtn_Click(object sender, RoutedEventArgs e) =>
        await MakePdfAsync("Menyiapkan dokumen untuk dicetak...", async session =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"{session.CertificationId}_print.pdf");
            await Task.Run(() => _report.GeneratePdf(session, path));
            Process.Start(new ProcessStartInfo { FileName = path, Verb = "print", UseShellExecute = true, CreateNoWindow = true });
            return "Dokumen dikirim ke printer.";
        });

    /// <summary>Loads the selected session, runs one PDF action, and says how it went.</summary>
    private async Task MakePdfAsync(string working, Func<FoTestSession, Task<string?>> action)
    {
        if (SessionComboBox.SelectedItem is not FoTestSession selected)
        {
            ShowStatus("Pilih sesi pengujian terlebih dahulu.", StatusKind.Error);
            SessionComboBox.Focus();
            return;
        }

        SetWorking(true);
        ShowStatus(working, StatusKind.Info);
        try
        {
            var session = await LoadFullSessionAsync(selected);
            var done = await action(session);
            if (done is null)
            {
                StatusLine.Visibility = Visibility.Collapsed;
                return;
            }

            ShowStatus(done, StatusKind.Success);
            Ui.Notify(done);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PDF action failed for session {SessionId}", selected.Id);
            ShowStatus(ex is ApiException ? ex.Message : $"Gagal: {ex.Message}", StatusKind.Error);
        }
        finally
        {
            SetWorking(false);
        }
    }

    /// <summary>The selected session as the server has it now: customer, technician and every result.</summary>
    private async Task<FoTestSession> LoadFullSessionAsync(FoTestSession session)
    {
        var full = await _api.GetSessionAsync(session.Id) ?? session;
        if (full.Results.Count == 0)
        {
            full.Results = await _api.GetResultsBySessionAsync(session.Id);
        }
        return full;
    }
}
