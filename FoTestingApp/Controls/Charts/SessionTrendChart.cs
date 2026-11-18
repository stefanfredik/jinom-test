using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FoTestingApp.Models;

namespace FoTestingApp.Controls.Charts;

/// <summary>
/// Sessions over time, one bar per day (per week beyond nine weeks), stacked by verdict — PASS at
/// the base, then PARTIAL, then FAIL — with a 2 px gap between segments and a rounded top. Hover a
/// bar for its counts; click it to narrow the history to that period (<see cref="PeriodSelected"/>).
/// The legend sits beside the chart in the page (icon + word per status).
/// </summary>
public sealed class SessionTrendChart : ChartBase
{
    private sealed record Bucket(DateTime Start, DateTime End, int Pass, int Partial, int Fail)
    {
        public int Total => Pass + Partial + Fail;
    }

    private List<Bucket> _buckets = [];
    private bool _weekly;

    /// <summary>A bar was clicked: its first and last day.</summary>
    public event Action<DateTime, DateTime>? PeriodSelected;

    public SessionTrendChart()
    {
        Height = 168;
    }

    public bool IsEmpty => _buckets.All(b => b.Total == 0);

    public void SetSessions(IEnumerable<FoTestSession> sessions, DateTime? from, DateTime? to)
    {
        var list = sessions.ToList();
        var first = (from ?? (list.Count > 0 ? list.Min(s => s.TestDate) : DateTime.Today.AddDays(-13))).Date;
        var last = (to ?? (list.Count > 0 ? list.Max(s => s.TestDate) : DateTime.Today)).Date;
        if (last < first) (first, last) = (last, first);
        // At least two weeks, so a single busy day does not fill the whole width.
        if ((last - first).TotalDays < 13) first = last.AddDays(-13);

        _weekly = (last - first).TotalDays > 62;
        var step = _weekly ? 7 : 1;
        if (_weekly) first = first.AddDays(-(((int)first.DayOfWeek + 6) % 7)); // weeks start on Monday

        _buckets = [];
        for (var start = first; start <= last; start = start.AddDays(step))
        {
            var end = start.AddDays(step - 1);
            var inBucket = list.Where(s => s.TestDate.Date >= start && s.TestDate.Date <= end).ToList();
            _buckets.Add(new Bucket(start, end,
                inBucket.Count(s => s.OverallStatus == TestOverallStatus.Pass),
                inBucket.Count(s => s.OverallStatus == TestOverallStatus.Partial),
                inBucket.Count(s => s.OverallStatus == TestOverallStatus.Fail)));
        }

        InvalidateVisual();
    }

    private Rect Plot => new(28, 8, Math.Max(10, ActualWidth - 36), Math.Max(10, ActualHeight - 30));

    private int? BucketAt(Point point)
    {
        var plot = Plot;
        if (_buckets.Count == 0 || point.X < plot.Left || point.X > plot.Right) return null;
        var index = (int)((point.X - plot.Left) / (plot.Width / _buckets.Count));
        return index >= 0 && index < _buckets.Count ? index : null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = BucketAt(e.GetPosition(this)) is { } i && _buckets[i].Total > 0 ? Cursors.Hand : null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (BucketAt(e.GetPosition(this)) is { } i && _buckets[i].Total > 0)
        {
            PeriodSelected?.Invoke(_buckets[i].Start, _buckets[i].End);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_buckets.Count == 0) return;

        var plot = Plot;
        var max = Math.Max(1, NiceMax(_buckets.Max(b => b.Total)));
        if (max < 4) max = 4;

        var grid = Line("OutlineVariantBrush", 1);
        for (var i = 0; i <= 2; i++)
        {
            var y = plot.Bottom - plot.Height * i / 2;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var text = Label(Number(max * i / 2));
            dc.DrawText(text, new Point(plot.Left - 6 - text.Width, y - text.Height / 2));
        }

        var slot = plot.Width / _buckets.Count;
        var barWidth = Math.Clamp(slot * 0.6, 2, 28);
        var hovered = Hover is { } h ? BucketAt(h) : null;

        for (var i = 0; i < _buckets.Count; i++)
        {
            var bucket = _buckets[i];
            var x = plot.Left + slot * i + (slot - barWidth) / 2;

            if (hovered == i)
            {
                dc.DrawRoundedRectangle(Token("SurfaceContainerHighBrush"), null, new Rect(plot.Left + slot * i, plot.Top, slot, plot.Height), 4, 4);
            }

            var segments = new[] { (bucket.Pass, "StatusOnlineBrush"), (bucket.Partial, "StatusWarningBrush"), (bucket.Fail, "StatusOfflineBrush") }
                .Where(s => s.Item1 > 0).ToList();
            var baseY = plot.Bottom;
            for (var s = 0; s < segments.Count; s++)
            {
                var (count, brush) = segments[s];
                var height = plot.Height * count / max;
                var isTop = s == segments.Count - 1;
                // A 2 px surface gap between stacked segments.
                var rect = new Rect(x, baseY - height, barWidth, Math.Max(1, height - (isTop ? 0 : 2)));
                dc.DrawGeometry(Token(brush), null, isTop ? TopRounded(rect, Math.Min(4, barWidth / 2)) : new RectangleGeometry(rect));
                baseY -= height;
            }
        }

        // Dates under the first, middle and last bar.
        foreach (var i in new[] { 0, _buckets.Count / 2, _buckets.Count - 1 }.Distinct())
        {
            var text = Label(_buckets[i].Start.ToString(_weekly ? "d MMM" : "d MMM"));
            var center = plot.Left + slot * i + slot / 2;
            dc.DrawText(text, new Point(Math.Clamp(center - text.Width / 2, 0, ActualWidth - text.Width), plot.Bottom + 6));
        }

        if (hovered is { } index && Hover is { } point)
        {
            var b = _buckets[index];
            var title = _weekly ? $"{b.Start:d MMM} – {b.End:d MMM yyyy}" : b.Start.ToString("dddd, d MMM yyyy");
            var lines = b.Total == 0
                ? new[] { title, "Tidak ada sesi" }
                : new[] { title, $"{b.Total} sesi", $"PASS {b.Pass} · PARTIAL {b.Partial} · FAIL {b.Fail}", "Klik untuk melihat sesinya" };
            DrawTooltip(dc, point, lines);
        }
    }

    /// <summary>A bar segment whose top corners are rounded and whose base is square on the axis.</summary>
    private static Geometry TopRounded(Rect r, double radius)
    {
        radius = Math.Min(radius, r.Height);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left, r.Bottom), true, true);
            c.LineTo(new Point(r.Left, r.Top + radius), false, false);
            c.ArcTo(new Point(r.Left + radius, r.Top), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r.Right - radius, r.Top), false, false);
            c.ArcTo(new Point(r.Right, r.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r.Right, r.Bottom), false, false);
        }
        g.Freeze();
        return g;
    }
}
