using System.Windows;
using System.Windows.Media;
using FoTestingApp.Services.Core;

namespace FoTestingApp.Controls.Charts;

/// <summary>
/// A row per measurement on one shared scale: a 4 px-rounded bar in the status colour (pass /
/// fail), the limit as a mark across it, the value written beside it. The status is never colour
/// alone: a failed row's value is written in the error colour with "gagal". Hover a row for its
/// value and limit in words.
/// </summary>
public sealed class BulletChart : ChartBase
{
    private const double RowHeight = 32;
    private IReadOnlyList<BulletItem> _items = [];

    public IReadOnlyList<BulletItem> Items
    {
        get => _items;
        set
        {
            _items = value;
            Height = Math.Max(1, _items.Count) * RowHeight + 8;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_items.Count == 0) return;

        var labels = _items.Select(i => Label(i.Label, "OnSurfaceBrush", BodyText)).ToList();
        var labelWidth = Math.Min(Math.Max(80, labels.Max(l => l.Width) + 12), ActualWidth * 0.35);
        const double valueWidth = 96;
        var barLeft = labelWidth;
        var barWidth = Math.Max(20, ActualWidth - labelWidth - valueWidth);

        var max = NiceMax(_items.SelectMany(i => new[] { i.Value ?? 0, i.Limit ?? 0 }).Max() * 1.1);
        double X(double v) => barLeft + barWidth * Math.Min(v, max) / max;

        int? hovered = Hover is { } h && h.Y >= 4 ? (int)((h.Y - 4) / RowHeight) : null;

        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var top = 4 + i * RowHeight;
            var mid = top + RowHeight / 2;

            if (hovered == i)
            {
                dc.DrawRoundedRectangle(Token("SurfaceContainerHighBrush"), null, new Rect(0, top, ActualWidth, RowHeight), 4, 4);
            }

            var label = labels[i];
            label.MaxTextWidth = labelWidth - 12;
            label.MaxLineCount = 1;
            label.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(label, new Point(4, mid - label.Height / 2));

            // Track, then the measured bar from zero.
            dc.DrawRoundedRectangle(Token("SurfaceContainerHighestBrush"), null, new Rect(barLeft, mid - 6, barWidth, 12), 4, 4);
            if (item.Value is { } value && value > 0)
            {
                var fill = item.Passed ? "StatusOnlineBrush" : "StatusOfflineBrush";
                dc.DrawRoundedRectangle(Token(fill), null, new Rect(barLeft, mid - 6, Math.Max(4, X(value) - barLeft), 12), 4, 4);
            }

            if (item.Limit is { } limit && limit > 0)
            {
                var x = X(limit);
                dc.DrawLine(Line("OnSurfaceBrush", 2), new Point(x, mid - 10), new Point(x, mid + 10));
            }

            var valueText = item.Value is { } v2
                ? Label($"{Number(v2)} {item.Unit}{(item.Passed ? string.Empty : " · gagal")}", item.Passed ? "OnSurfaceBrush" : "ErrorBrush", BodyText, medium: true)
                : Label("tidak terukur", "ErrorBrush", BodyText, medium: true);
            dc.DrawText(valueText, new Point(barLeft + barWidth + 8, mid - valueText.Height / 2));
        }

        if (hovered is { } row && row >= 0 && row < _items.Count && Hover is { } point)
        {
            var item = _items[row];
            var lines = new List<string> { item.Label };
            lines.Add(item.Value is { } v ? $"Terukur {Number(v)} {item.Unit}" : "Tidak terukur");
            if (item.Limit is { } limit && limit > 0)
            {
                lines.Add(item.HigherIsBetter ? $"Minimal {Number(limit)} {item.Unit}" : $"Batas {Number(limit)} {item.Unit}");
            }
            lines.Add(item.Passed ? "Lulus" : "Gagal");
            DrawTooltip(dc, point, [.. lines]);
        }
    }
}
