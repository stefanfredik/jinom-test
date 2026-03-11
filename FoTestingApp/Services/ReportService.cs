using FoTestingApp.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;

namespace FoTestingApp.Services;

/// <summary>
/// Menghasilkan laporan PDF sertifikasi FO menggunakan QuestPDF.
/// </summary>
public class ReportService
{
    static ReportService()
    {
        // QuestPDF community license (gratis untuk non-commercial)
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

                page.Header().Element(ComposeHeader);
                page.Content().Element(c => ComposeContent(c, session));
                page.Footer().Element(ComposeFooter);
            });
        });
    }

    // ── Header ──────────────────────────────────────────────────────────────────

    private static void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(10).BorderBottom(1).BorderColor(Colors.Blue.Medium).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("JINOM AI").FontSize(18).Bold().FontColor(Color.FromHex("#1E5AA0"));
                col.Item().Text("Laporan Hasil Pengujian & Sertifikasi Jaringan Fiber Optik")
                   .FontSize(11).FontColor(Colors.Grey.Darken2);
            });

            row.ConstantItem(120).AlignRight().Column(col =>
            {
                col.Item().Text("FO Testing & Commissioning").FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().Text($"Tanggal: {DateTime.Now:dd MMMM yyyy}").FontSize(9);
            });
        });
    }

    // ── Content ─────────────────────────────────────────────────────────────────

    private static void ComposeContent(IContainer container, FoTestSession session)
    {
        container.PaddingTop(10).Column(col =>
        {
            // Certification ID badge
            col.Item().Background(Color.FromHex("#1E5AA0")).Padding(8).Row(row =>
            {
                row.RelativeItem().Text($"ID SERTIFIKASI: {session.CertificationId}")
                   .FontColor(Colors.White).Bold().FontSize(11);
                row.ConstantItem(80).AlignRight().Text($"Status: {session.OverallStatus}")
                   .FontColor(session.OverallStatus == TestOverallStatus.Pass ? Colors.Green.Lighten3 : Colors.Red.Lighten3)
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
                           c.Item().Row(r => InfoRow(r, "Tipe Site", session.Customer.SiteType.ToUpper()));
                           if (!string.IsNullOrEmpty(session.Customer.SiteId))
                               c.Item().Row(r => InfoRow(r, "Site ID", session.Customer.SiteId));
                           c.Item().Row(r => InfoRow(r, "Alamat", session.Customer.Address));
                           c.Item().Row(r => InfoRow(r, "Paket", $"{session.Customer.PackageMbps} Mbps"));
                       }
                   });

                // Right: Session Info
                row.RelativeItem().PaddingLeft(8).Column(c =>
                {
                    c.Item().Text("DATA PENGUJIAN").Bold().FontSize(9).FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(4);
                    c.Item().Row(r => InfoRow(r, "Teknisi", session.Technician?.Name ?? "-"));
                    c.Item().Row(r => InfoRow(r, "Tanggal", session.TestDate.ToString("dd/MM/yyyy HH:mm")));
                    c.Item().Row(r => InfoRow(r, "Catatan", session.Notes ?? "-"));
                });
            });

            col.Item().PaddingTop(12);

            // Results Table
            col.Item().Text("HASIL PENGUJIAN").Bold().FontSize(10).FontColor(Color.FromHex("#1E5AA0"));
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
                header.Cell().Background(Color.FromHex("#1E5AA0")).Padding(5)
                      .Text("No").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Color.FromHex("#1E5AA0")).Padding(5)
                      .Text("Jenis Pengujian").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Color.FromHex("#1E5AA0")).Padding(5)
                      .Text("Target").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Color.FromHex("#1E5AA0")).Padding(5)
                      .Text("Detail Hasil").FontColor(Colors.White).Bold().FontSize(9);
                header.Cell().Background(Color.FromHex("#1E5AA0")).Padding(5)
                      .Text("Status").FontColor(Colors.White).Bold().FontSize(9);
            });

            var i = 0;
            foreach (var result in results)
            {
                i++;
                var bg = i % 2 == 0 ? Color.FromHex("#F4F6FA") : Colors.White;
                var statusColor = result.Status == TestStatus.Pass ? Colors.Green.Darken2 : Colors.Red.Darken2;
                var statusText = result.Status == TestStatus.Pass ? "PASS ✓" : "FAIL ✗";
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
            row.RelativeItem().Text("Jinom AI | Dokumen Sertifikasi FO Testing & Commissioning | Rahasia")
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

    private static string FormatTestTypeName(string testType) => testType switch
    {
        TestTypes.PingGateway => "Ping Gateway ISP",
        TestTypes.PingDns => "Ping DNS Server",
        TestTypes.NslookupNasional => "NSLookup Nasional",
        TestTypes.NslookupInternasional => "NSLookup Internasional",
        TestTypes.PingDomainLokal => "Ping Domain Lokal",
        TestTypes.BrowsingTest => "Pengujian Browsing",
        TestTypes.StreamingTest => "Pengujian Streaming",
        TestTypes.SocialMediaTest => "Pengujian Social Media",
        TestTypes.SpeedtestFast => "Speedtest Fast.com",
        TestTypes.SpeedtestOokla => "Speedtest Ookla",
        _ => testType,
    };

    private static string ExtractDetailText(FoTestResult result)
    {
        if (result.ResultData is null) { return "-"; }

        try
        {
            var root = result.ResultData.RootElement;

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
                    $"\u2193 {root.GetProperty("download_mbps").GetDouble()} Mbps",

                TestTypes.SpeedtestOokla =>
                    $"↓ {root.GetProperty("download_mbps").GetDouble()} Mbps | ↑ {root.GetProperty("upload_mbps").GetDouble()} Mbps",

                _ => "-",
            };
        }
        catch
        {
            return "-";
        }
    }
}
