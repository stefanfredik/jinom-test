using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Xps.Packaging;
using FoTestingApp.Models;
using FoTestingApp.Services;
using Microsoft.Win32;
using Serilog;

namespace FoTestingApp.Views.Pages;

public partial class ReportPage : Page
{
    private readonly ApiService _api = new();
    private readonly ReportService _report = new();
    private int? _sessionId;

    public ReportPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadSessionsAsync();
    }

    /// <summary>Constructor dipanggil dari NewTestPage setelah sesi selesai.</summary>
    public ReportPage(int sessionId) : this()
    {
        _sessionId = sessionId;
    }

    private async Task LoadSessionsAsync()
    {
        var sessions = await _api.GetSessionsAsync();
        SessionComboBox.ItemsSource = sessions;
        SessionComboBox.DisplayMemberPath = "CertificationId";

        if (_sessionId.HasValue)
        {
            var match = sessions.FirstOrDefault(s => s.Id == _sessionId);
            if (match is not null) { SessionComboBox.SelectedItem = match; }
        }
    }

    private async void PreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        var session = await GetSelectedSessionWithResultsAsync();
        if (session is null) { return; }

        try
        {
            ReportStatus.Text = "⏳ Membuat preview PDF...";
            var tempPath = Path.Combine(Path.GetTempPath(), $"{session.CertificationId}.pdf");
            await Task.Run(() => _report.GeneratePdf(session, tempPath));

            // Buka PDF dengan default viewer (Adobe Reader / Edge PDF viewer)
            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            ReportStatus.Text = $"✅ PDF dibuka di viewer: {tempPath}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Preview PDF failed");
            ReportStatus.Text = $"❌ Gagal: {ex.Message}";
        }
    }

    private async void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var session = await GetSelectedSessionWithResultsAsync();
        if (session is null) { return; }

        var dialog = new SaveFileDialog
        {
            Title = "Simpan Laporan PDF",
            Filter = "PDF Files (*.pdf)|*.pdf",
            FileName = $"Laporan_{session.CertificationId}_{session.TestDate:yyyyMMdd}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        if (dialog.ShowDialog() != true) { return; }

        try
        {
            ReportStatus.Text = "⏳ Mengekspor PDF...";
            await Task.Run(() => _report.GeneratePdf(session, dialog.FileName));
            ReportStatus.Text = $"✅ Laporan disimpan ke: {dialog.FileName}";
            Log.Information("PDF exported: {Path}", dialog.FileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export PDF failed");
            ReportStatus.Text = $"❌ Gagal ekspor: {ex.Message}";
        }
    }

    private async void PrintBtn_Click(object sender, RoutedEventArgs e)
    {
        var session = await GetSelectedSessionWithResultsAsync();
        if (session is null) { return; }

        try
        {
            ReportStatus.Text = "⏳ Menyiapkan dokumen untuk cetak...";

            // Simpan ke temp PDF, lalu kirim ke printer via Acrobat/Edge
            var tempPath = Path.Combine(Path.GetTempPath(), $"{session.CertificationId}_print.pdf");
            await Task.Run(() => _report.GeneratePdf(session, tempPath));

            // Buka dengan /p flag untuk print langsung
            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                Verb = "print",
                UseShellExecute = true,
                CreateNoWindow = true,
            });

            ReportStatus.Text = "✅ Dikirim ke printer.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Print PDF failed");
            ReportStatus.Text = $"❌ Gagal cetak: {ex.Message}";
        }
    }

    private async Task<FoTestSession?> GetSelectedSessionWithResultsAsync()
    {
        if (SessionComboBox.SelectedItem is not FoTestSession session)
        {
            MessageBox.Show("Pilih sesi pengujian terlebih dahulu.", "Pilih Sesi",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        // Load hasil test untuk sesi ini
        session.Results = await _api.GetResultsBySessionAsync(session.Id);

        // Load customer & technician jika belum ada
        if (session.Customer is null || session.Technician is null)
        {
            var sessions = await _api.GetSessionsAsync();
            var full = sessions.FirstOrDefault(s => s.Id == session.Id);
            if (full is not null)
            {
                session.Customer = full.Customer;
                session.Technician = full.Technician;
            }
        }

        return session;
    }
}
