using System.Diagnostics;
using System.IO;
using FoTestingApp.Models;
using FoTestingApp.Services;
using Microsoft.Win32;
using Serilog;

namespace FoTestingApp.Helpers;

/// <summary>
/// "Unduh laporan": a session's certificate as a PDF, saved where the technician chooses. The
/// session is read fresh from the server (customer, technician, every result), so the PDF holds
/// what was stored, not what this screen happens to show. One way, from the history and from the
/// results of a run just finished.
/// </summary>
public static class ReportDownload
{
    private static readonly ApiService Api = new();
    private static readonly ReportService Report = new();

    /// <returns>The saved file, or null when the technician cancelled or it failed (already said).</returns>
    public static async Task<string?> SaveAsync(int sessionId, string? certificationId, DateTime testDate)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Unduh laporan PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"Laporan_{certificationId ?? sessionId.ToString()}_{testDate:yyyyMMdd}.pdf",
            InitialDirectory = DownloadsFolder(),
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        try
        {
            var session = await Api.GetSessionAsync(sessionId)
                ?? throw new ApiException("Sesi pengujian tidak ditemukan di server.");
            if (session.Results.Count == 0)
            {
                session.Results = await Api.GetResultsBySessionAsync(sessionId);
            }

            await Task.Run(() => Report.GeneratePdf(session, dialog.FileName));
            Log.Information("Report of session {SessionId} saved to {Path}", sessionId, dialog.FileName);

            var path = dialog.FileName;
            Ui.Notify($"Laporan disimpan: {Path.GetFileName(path)}", "Buka", () => Open(path));
            return path;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving the report of session {SessionId} failed", sessionId);
            await Ui.AlertAsync("Laporan gagal diunduh", ex.Message);
            return null;
        }
    }

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Opening {Path} failed", path);
            Ui.Notify("Tidak ada aplikasi untuk membuka PDF di laptop ini.");
        }
    }

    /// <summary>The user's Downloads folder, else Documents.</summary>
    private static string DownloadsFolder()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
