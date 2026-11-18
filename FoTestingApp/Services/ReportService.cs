using FoTestingApp.Models;
using FoTestingApp.Services.Core;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;

namespace FoTestingApp.Services;

/// <summary>
/// Menghasilkan laporan PDF pengujian FO menggunakan QuestPDF.
/// </summary>
public class ReportService
{
    static ReportService()
    {
        // QuestPDF Community licence: free for organisations under USD 1M annual revenue — check
        // eligibility, or buy a Professional licence, before relying on it.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Hasilkan PDF dan simpan ke path yang diberikan.
    /// </summary>
    public void GeneratePdf(FoTestSession session, string outputPath)
    {
        var document = BuildDocument(session);
        document.GeneratePdf(outputPath);
    }

    /// <summary>
    /// Hasilkan PDF dan kembalikan sebagai byte array (untuk preview/print).
    /// </summary>
    public byte[] GeneratePdfBytes(FoTestSession session)
    {
        return BuildDocument(session).GeneratePdf();
    }

    private IDocument BuildDocument(FoTestSession session)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));

                page.Header().Element(c => ComposeHeader(c, session));
                page.Content().Element(c => ComposeContent(c, session));
                page.Footer().Element(ComposeFooter);
            });
        });
    }

    // ── Header ──────────────────────────────────────────────────────────────────

    private static void ComposeHeader(IContainer container, FoTestSession session)
    {
        container.PaddingBottom(10).BorderBottom(1).BorderColor(Colors.Green.Medium).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("JINOM TEST").FontSize(18).Bold().FontColor(Color.FromHex("#16a34a"));
                col.Item().Text("Laporan Hasil Pengujian Jaringan Fiber Optik")
                   .FontSize(11).FontColor(Colors.Grey.Darken2);
            });

            row.ConstantItem(120).AlignRight().Column(col =>
            {
                col.Item().Text("Jinom Test").FontSize(9).FontColor(Colors.Grey.Darken1);
                // The test's date; the print date is only a footnote.
                col.Item().Text($"Tanggal uji: {session.TestDate:dd MMMM yyyy}").FontSize(9);
                col.Item().Text($"Dicetak: {DateTime.Now:dd MMM yyyy HH:mm}").FontSize(7).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    // ── Content ─────────────────────────────────────────────────────────────────

    private static void ComposeContent(IContainer container, FoTestSession session)
    {
        container.PaddingTop(10).Column(col =>
        {
            // Test ID badge
            col.Item().Background(Color.FromHex("#16a34a")).Padding(8).Row(row =>
            {
                row.RelativeItem().Text($"ID PENGUJIAN: {session.CertificationId}")
                   .FontColor(Colors.White).Bold().FontSize(11);
                row.ConstantItem(120).AlignRight().Text($"Status: {session.OverallStatus.ToString().ToUpperInvariant()}")
                   .FontColor(session.OverallStatus switch
                   {
                       TestOverallStatus.Pass => Colors.Green.Lighten3,
                       TestOverallStatus.Partial => Colors.Yellow.Lighten2,
                       _ => Colors.Red.Lighten3,
                   })
                   .Bold().FontSize(11);
            });

            col.Item().PaddingTop(10);

            // Customer & Technician Info
            col.Item().Row(row =>
            {
                // Left: Customer Info
                row.RelativeItem().BorderRight(1).BorderColor(Colors.Grey.Lighten2).PaddingRight(8)
                   .Column(c =>
                   {
                       c.Item().Text("DATA PELANGGAN").Bold().FontSize(9).FontColor(Colors.Grey.Darken2);
                       c.Item().PaddingTop(4);
                       if (session.Customer is not null)
                       {
                           c.Item().Row(r => InfoRow(r, "Nama", session.Customer.FullName));
                           c.Item().Row(r => InfoRow(r, "Tipe Site", session.Customer.SiteType.ToUpperInvariant()));
                           if (!string.IsNullOrEmpty(session.Customer.SiteId))
                               c.Item().Row(r => InfoRow(r, "Site ID", session.Customer.SiteId));
                           c.Item().Row(r => InfoRow(r, "Alamat", string.IsNullOrWhiteSpace(session.Customer.Address) ? "-" : session.Customer.Address));
                           c.Item().Row(r => InfoRow(r, "Paket", session.Customer.PackageText));
                       }
                   });

                // Right: Session Info
                row.RelativeItem().PaddingLeft(8).Column(c =>
                {
                    c.Item().Text("DATA PENGUJIAN").Bold().FontSize(9).FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(4);
                    c.Item().Row(r => InfoRow(r, "Teknisi", session.Technician?.Name ?? "-"));
                    c.Item().Row(r => InfoRow(r, "Mulai", (session.StartTime ?? session.TestDate).ToString("dd/MM/yyyy HH:mm")));
                    if (session.EndTime is { } end)
                        c.Item().Row(r => InfoRow(r, "Selesai", end.ToString("dd/MM/yyyy HH:mm")));
                    c.Item().Row(r => InfoRow(r, "Hasil", $"{session.Results.Count(x => x.Status == TestStatus.Pass)} dari {session.Results.Count} lulus"));
                    c.Item().Row(r => InfoRow(r, "Catatan", session.Notes ?? "-"));
                });
            });

            col.Item().PaddingTop(12);

            // Results Table
            col.Item().Text("HASIL PENGUJIAN").Bold().FontSize(10).FontColor(Color.FromHex("#16a34a"));
            col.Item().PaddingTop(4);
            col.Item().Element(c => ComposeResultsTable(c, session.Results));

            col.Item().PaddingTop(16);

            // Signature section
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Catatan Tambahan:").Bold().FontSize(9);
                    c.Item().PaddingTop(32).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
                });

                row.ConstantItem(20);

                row.ConstantItem(160).Column(c =>
                {
                    c.Item().Text("Tanda Tangan Teknisi:").Bold().FontSize(9);
                    c.Item().PaddingTop(32).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
                    c.Item().PaddingTop(4).Text(session.Technician?.Name ?? "").FontSize(9).Italic();
                    c.Item().Text(session.TestDate.ToString("dd MMMM yyyy")).FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        });
    }

    private static void InfoRow(RowDescriptor row, string label, string value)
    {
        row.ConstantItem(90).Text($"{label}:").FontSize(9).FontColor(Colors.Grey.Darken2);
        row.RelativeItem().Text(value).FontSize(9);
    }

    private static void ComposeResultsTable(IContainer container, List<FoTestResult> results)
    {
        if (results.Count == 0)
        {
            container.Padding(8).Text("Belum ada hasil pengujian tersimpan untuk sesi ini.").Italic().FontSize(9);
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(30);   // No
                cols.RelativeColumn(3);    // Jenis Test
                cols.RelativeColumn(3);    // Target
                cols.RelativeColumn(4);    // Detail Hasil
                cols.ConstantColumn(55);   // Status
            });

            // Header
            table.Header(header =>
            {
                header.Cell().Background(Color.FromHex("#16a34a")).Padding(5)
                      .Text("No").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Color.FromHex("#16a34a")).Padding(5)
                      .Text("Jenis Pengujian").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Color.FromHex("#16a34a")).Padding(5)
                      .Text("Target").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Color.FromHex("#16a34a")).Padding(5)
                      .Text("Detail Hasil").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Color.FromHex("#16a34a")).Padding(5)
                      .Text("Status").FontColor(Colors.White).Bold().FontSize(9);
            });

            var i = 0;
            foreach (var result in results)
            {
                i++;
                var bg = i % 2 == 0 ? Color.FromHex("#F4F6FA") : Colors.White;
                var statusColor = result.Status == TestStatus.Pass ? Colors.Green.Darken2 : Colors.Red.Darken2;
                // Plain words: Arial has no check mark glyph.
                var statusText = result.Status == TestStatus.Pass ? "PASS" : "FAIL";
                var detail = ExtractDetailText(result);

                table.Cell().Background(bg).Padding(5).Text(i.ToString()).FontSize(9);
                table.Cell().Background(bg).Padding(5).Text(FormatTestTypeName(result.TestType)).FontSize(9);
                table.Cell().Background(bg).Padding(5).Text(result.Target).FontSize(9);
                table.Cell().Background(bg).Padding(5).Text(detail).FontSize(9);
                table.Cell().Background(bg).Padding(5).AlignCenter()
                     .Text(statusText).Bold().FontSize(9).FontColor(statusColor);
            }
        });
    }

    // ── Footer ──────────────────────────────────────────────────────────────────

    private static void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).BorderTop(1).BorderColor(Colors.Grey.Lighten2).Row(row =>
        {
            row.RelativeItem().Text("Jinom Test | Laporan Pengujian Jaringan | Rahasia")
               .FontSize(8).FontColor(Colors.Grey.Darken1);
            row.ConstantItem(80).AlignRight().Text(text =>
            {
                text.Span("Halaman ").FontSize(8).FontColor(Colors.Grey.Darken1);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" dari ").FontSize(8).FontColor(Colors.Grey.Darken1);
                text.TotalPages().FontSize(8);
            });
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string FormatTestTypeName(string testType) => ResultFormatter.TestName(testType);

    private static string ExtractDetailText(FoTestResult result)
    {
        var rows = ResultFormatter.Rows(result.TestType, result.ResultData?.RootElement);
        return rows.Count == 0 ? "-" : string.Join("\n", rows.Select(r => $"{r.Label}: {r.Value}"));
    }
}
