using System.Windows;
using System.Windows.Media;

namespace FoTestingApp.Controls.Charts;

/// <summary>
/// The round-trip time of every echo of one ping test, in order: a 2 px line over the replies, each
/// lost reply (RTO) marked on the top edge in the offline colour (never a zero that reads as fast),
/// the pass limit as a dashed line. Hover shows the echo's number and its time.
///
/// Live during a run (<see cref="Reset"/>, then <see cref="Add"/> per reply) or from a stored result
/// (<see cref="SetSamples"/>).
/// </summary>
public sealed class LatencyChart : ChartBase
{
    private readonly List<double?> _samples = [];
    private int _expected;
    private double? _threshold;

    public LatencyChart()
    {
        Height = 140;
        MinWidth = 200;
    }

    /// <summary>Starts an empty chart for <paramref name="expected"/> echoes, so the x axis does not grow while it fills.</summary>
    public void Reset(int expected, double? thresholdMs)
    {
        _samples.Clear();
        _expected = Math.Max(1, expected);
        _threshold = thresholdMs;
        InvalidateVisual();
    }

    /// <summary>One echo: its time in ms, or null when it was lost.</summary>
    public void Add(double? rttMs)
    {
        _samples.Add(rttMs);
        InvalidateVisual();
    }

    public void SetSamples(IEnumerable<double?> samples, double? thresholdMs)
    {
        _samples.Clear();
        _samples.AddRange(samples);
        _expected = Math.Max(1, _samples.Count);
        _threshold = thresholdMs;
        InvalidateVisual();
    }

    public int Count => _samples.Count;

    protected override void OnRender(DrawingContext dc)
    {
        // Hit-testing over the whole area, so hover works between the points too.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        const double left = 40, top = 12, right = 8, bottom = 20;
        var plot = new Rect(left, top, Math.Max(10, ActualWidth - left - right), Math.Max(10, ActualHeight - top - bottom));

        var replies = _samples.Where(s => s is not null).Select(s => s!.Value).ToList();
        var max = NiceMax(Math.Max(replies.DefaultIfEmpty(0).Max(), (_threshold ?? 0) * 1.2));

        // Recessive grid: three lines with their values, no frame.
        var grid = Line("OutlineVariantBrush", 1);
        for (var i = 0; i <= 2; i++)
        {
            var value = max * i / 2;
            var y = plot.Bottom - plot.Height * i / 2;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var text = Label($"{Number(value)} ms");
            dc.DrawText(text, new Point(plot.Left - 6 - text.Width, y - text.Height / 2));
        }

        var count = Math.Max(_expected, _samples.Count);
        double X(int index) => count <= 1 ? plot.Left + plot.Width / 2 : plot.Left + plot.Width * index / (count - 1);
        double Y(double ms) => plot.Bottom - plot.Height * Math.Min(ms, max) / max;

        var first = Label("1");
        dc.DrawText(first, new Point(plot.Left - first.Width / 2, plot.Bottom + 4));
        var last = Label(count.ToString("N0"));
        dc.DrawText(last, new Point(plot.Right - last.Width, plot.Bottom + 4));

        if (_threshold is { } limit && limit > 0)
        {
            var y = Y(limit);
            dc.DrawLine(Line("StatusWarningBrush", 1, dashed: true), new Point(plot.Left, y), new Point(plot.Right, y));
            var text = Label($"batas {Number(limit)} ms", "StatusWarningBrush");
            dc.DrawText(text, new Point(plot.Right - text.Width, y - text.Height - 1));
        }

        // The replies: a line broken where an echo was lost, so a gap is never drawn as a value.
        var line = Line("PrimaryBrush", 2);
        var rto = Line("StatusOfflineBrush", 2);
        StreamGeometry? geometry = null;
        StreamGeometryContext? ctx = null;
        var open = false;
        for (var i = 0; i < _samples.Count; i++)
        {
            if (_samples[i] is { } ms)
            {
                if (geometry is null)
                {
                    geometry = new StreamGeometry();
                    ctx = geometry.Open();
                }
                var point = new Point(X(i), Y(ms));
                if (!open)
                {
                    ctx!.BeginFigure(point, false, false);
                    open = true;
                }
                else
                {
                    ctx!.LineTo(point, true, true);
                }
            }
            else
            {
                open = false;
                dc.DrawLine(rto, new Point(X(i), plot.Top - 4), new Point(X(i), plot.Top + 4));
            }
        }
        ctx?.Close();
        if (geometry is not null)
        {
            geometry.Freeze();
            dc.DrawGeometry(null, line, geometry);
        }

        // A lone reply (or very few) still shows: a dot on each when there is room.
        if (_samples.Count <= 40)
        {
            var dot = Token("PrimaryBrush");
            for (var i = 0; i < _samples.Count; i++)
            {
                if (_samples[i] is { } ms) dc.DrawEllipse(dot, null, new Point(X(i), Y(ms)), 2.5, 2.5);
            }
        }

        if (_samples.Count == 0)
        {
            var waiting = Label("Menunggu balasan...", size: BodyText);
            dc.DrawText(waiting, new Point(plot.Left + (plot.Width - waiting.Width) / 2, plot.Top + (plot.Height - waiting.Height) / 2));
            return;
        }

        // Hover: the nearest echo, a crosshair and its value.
        if (Hover is { } hover && plot.Contains(hover with { Y = plot.Top + 1 }))
        {
            var index = count <= 1 ? 0 : (int)Math.Round((hover.X - plot.Left) / plot.Width * (count - 1));
            if (index >= 0 && index < _samples.Count)
            {
                var x = X(index);
                dc.DrawLine(Line("OutlineBrush", 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
                if (_samples[index] is { } ms)
                {
                    var p = new Point(x, Y(ms));
                    dc.DrawEllipse(Token("PrimaryBrush"), new Pen(Token("SurfaceBrush"), 2), p, 5, 5);
                    DrawTooltip(dc, p, $"{Number(ms)} ms", $"Ping ke-{index + 1}");
                }
                else
                {
                    DrawTooltip(dc, new Point(x, plot.Top + 12), "RTO", $"Ping ke-{index + 1} tidak dibalas");
                }
            }
        }
    }
}
