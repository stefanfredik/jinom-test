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
    /// <summary>The app's light theme tokens (Themes/Tokens.Light.xaml), so the certificate matches the app.</summary>
    private static class Brand
    {
        public static readonly Color Primary = Color.FromHex("#006C49");
        public static readonly Color SurfaceLow = Color.FromHex("#F2F4F0");
        public static readonly Color OnSurfaceVariant = Color.FromHex("#404943");
        public static readonly Color OutlineVariant = Color.FromHex("#C0C9C1");
        public static readonly Color Pass = Color.FromHex("#146C2E");
        public static readonly Color Fail = Color.FromHex("#BA1A1A");
        public static readonly Color PassContainer = Color.FromHex("#B4F1BD");
        public static readonly Color OnPassContainer = Color.FromHex("#002109");
        public static readonly Color PartialContainer = Color.FromHex("#FFDEA1");
        public static readonly Color OnPartialContainer = Color.FromHex("#261900");
        public static readonly Color FailContainer = Color.FromHex("#FFDAD6");
        public static readonly Color OnFailContainer = Color.FromHex("#410002");
    }

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
        container.PaddingBottom(10).BorderBottom(1).BorderColor(Brand.Primary).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("JINOM TEST").FontSize(18).Bold().FontColor(Brand.Primary);
                col.Item().Text("Laporan Hasil Pengujian Jaringan Fiber Optik")
                   .FontSize(11).FontColor(Brand.OnSurfaceVariant);
            });

            row.ConstantItem(120).AlignRight().Column(col =>
            {
                col.Item().Text("Jinom Test").FontSize(9).FontColor(Brand.OnSurfaceVariant);
                // The test's date; the print date is only a footnote.
                col.Item().Text($"Tanggal uji: {session.TestDate:dd MMMM yyyy}").FontSize(9);
                col.Item().Text($"Dicetak: {DateTime.Now:dd MMM yyyy HH:mm}").FontSize(7).FontColor(Brand.OnSurfaceVariant);
            });
        });
    }

    // ── Content ─────────────────────────────────────────────────────────────────

    private static void ComposeContent(IContainer container, FoTestSession session)
    {
        container.PaddingTop(10).Column(col =>
        {
            // Test ID badge
            col.Item().Background(Brand.Primary).Padding(8).Row(row =>
            {
                row.RelativeItem().Text($"ID PENGUJIAN: {session.CertificationId}")
                   .FontColor(Colors.White).Bold().FontSize(11);
                // The status as a chip, in the app's status container colours.
                var (chip, onChip) = session.OverallStatus switch
                {
                    TestOverallStatus.Pass => (Brand.PassContainer, Brand.OnPassContainer),
                    TestOverallStatus.Partial => (Brand.PartialContainer, Brand.OnPartialContainer),
                    _ => (Brand.FailContainer, Brand.OnFailContainer),
                };
                row.AutoItem().AlignRight().AlignMiddle().Background(chip).PaddingVertical(2).PaddingHorizontal(8)
                   .Text($"Status: {session.OverallStatus.ToString().ToUpperInvariant()}")
                   .FontColor(onChip).Bold().FontSize(10);
            });

            col.Item().PaddingTop(10);

            // Customer & Technician Info
            col.Item().Row(row =>
            {
                // Left: Customer Info
                row.RelativeItem().BorderRight(1).BorderColor(Brand.OutlineVariant).PaddingRight(8)
                   .Column(c =>
                   {
                       c.Item().Text("DATA PELANGGAN").Bold().FontSize(9).FontColor(Brand.OnSurfaceVariant);
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
                    c.Item().Text("DATA PENGUJIAN").Bold().FontSize(9).FontColor(Brand.OnSurfaceVariant);
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
            col.Item().Text("HASIL PENGUJIAN").Bold().FontSize(10).FontColor(Brand.Primary);
            col.Item().PaddingTop(4);
            col.Item().Element(c => ComposeResultsTable(c, session.Results));

            col.Item().PaddingTop(16);

            // Signature section
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Catatan Tambahan:").Bold().FontSize(9);
                    c.Item().PaddingTop(32).BorderBottom(1).BorderColor(Brand.OutlineVariant);
                });

                row.ConstantItem(20);

                row.ConstantItem(160).Column(c =>
                {
                    c.Item().Text("Tanda Tangan Teknisi:").Bold().FontSize(9);
                    c.Item().PaddingTop(32).BorderBottom(1).BorderColor(Brand.OutlineVariant);
                    c.Item().PaddingTop(4).Text(session.Technician?.Name ?? "").FontSize(9).Italic();
                    c.Item().Text(session.TestDate.ToString("dd MMMM yyyy")).FontSize(8).FontColor(Brand.OnSurfaceVariant);
                });
            });
        });
    }

    private static void InfoRow(RowDescriptor row, string label, string value)
    {
        row.ConstantItem(90).Text($"{label}:").FontSize(9).FontColor(Brand.OnSurfaceVariant);
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
                header.Cell().Background(Brand.Primary).Padding(5)
                      .Text("No").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Brand.Primary).Padding(5)
                      .Text("Jenis Pengujian").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Brand.Primary).Padding(5)
                      .Text("Target").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Brand.Primary).Padding(5)
                      .Text("Detail Hasil").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Brand.Primary).Padding(5)
                      .Text("Status").FontColor(Colors.White).Bold().FontSize(9);
            });

            var i = 0;
            foreach (var result in results)
            {
                i++;
                var bg = i % 2 == 0 ? Brand.SurfaceLow : Colors.White;
                var statusColor = result.Status == TestStatus.Pass ? Brand.Pass : Brand.Fail;
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
        container.PaddingTop(6).BorderTop(1).BorderColor(Brand.OutlineVariant).Row(row =>
        {
            row.RelativeItem().Text("Jinom Test | Laporan Pengujian Jaringan | Rahasia")
               .FontSize(8).FontColor(Brand.OnSurfaceVariant);
            row.ConstantItem(80).AlignRight().Text(text =>
            {
                text.Span("Halaman ").FontSize(8).FontColor(Brand.OnSurfaceVariant);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" dari ").FontSize(8).FontColor(Brand.OnSurfaceVariant);
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
