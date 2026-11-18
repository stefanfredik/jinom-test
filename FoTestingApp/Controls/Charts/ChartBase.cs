using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FoTestingApp.Helpers;

namespace FoTestingApp.Controls.Charts;

/// <summary>
/// What every chart shares (DESIGN.md §8): colours from the theme tokens only, repainted when the
/// theme changes; type at the label sizes of the type scale; the hovered point and a plain M3
/// tooltip (inverse surface) drawn next to it. Charts draw themselves (OnRender): no chart library,
/// nothing to style twice.
/// </summary>
public abstract class ChartBase : FrameworkElement
{
    /// <summary>M3 label-small / body-small, the only sizes a chart writes in.</summary>
    protected const double SmallText = 11;
    protected const double BodyText = 12;

    /// <summary>The pointer over the chart, or null when it is elsewhere.</summary>
    protected Point? Hover { get; private set; }

    protected ChartBase()
    {
        SnapsToDevicePixels = true;
        Focusable = false;
        Loaded += (_, _) => ThemeService.Changed += InvalidateVisual;
        Unloaded += (_, _) => ThemeService.Changed -= InvalidateVisual;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Hover = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Hover = null;
        InvalidateVisual();
    }

    /// <summary>A theme brush by token (e.g. "PrimaryBrush"); transparent if the token is missing.</summary>
    protected Brush Token(string key) => TryFindResource(key) as Brush ?? Brushes.Transparent;

    protected Pen Line(string key, double thickness = 1, bool dashed = false)
    {
        var pen = new Pen(Token(key), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (dashed)
        {
            pen.DashStyle = new DashStyle([4, 3], 0);
        }
        return pen;
    }

    protected FormattedText Label(string text, string brushKey = "OnSurfaceVariantBrush", double size = SmallText, bool medium = false)
    {
        var family = TryFindResource("AppFont") as FontFamily ?? new FontFamily("Segoe UI");
        var typeface = new Typeface(family, FontStyles.Normal, medium ? FontWeights.Medium : FontWeights.Normal, FontStretches.Normal);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, Token(brushKey),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    /// <summary>
    /// A plain tooltip (inverse surface, 4 px corners) beside a point, kept inside the chart: the
    /// value of what the pointer is on, one or more lines.
    /// </summary>
    protected void DrawTooltip(DrawingContext dc, Point anchor, params string[] lines)
    {
        if (lines.Length == 0) return;

        var texts = lines.Select((l, i) => Label(l, "InverseOnSurfaceBrush", BodyText, medium: i == 0)).ToArray();
        var width = texts.Max(t => t.Width) + 16;
        var height = texts.Sum(t => t.Height) + 12;

        var x = anchor.X + 12;
        if (x + width > ActualWidth) x = anchor.X - 12 - width;
        x = Math.Max(0, x);
        var y = Math.Clamp(anchor.Y - height / 2, 0, Math.Max(0, ActualHeight - height));

        dc.DrawRoundedRectangle(Token("InverseSurfaceBrush"), null, new Rect(x, y, width, height), 4, 4);
        var lineY = y + 6;
        foreach (var text in texts)
        {
            dc.DrawText(text, new Point(x + 8, lineY));
            lineY += text.Height;
        }
    }

    /// <summary>"Nice" upper bound for an axis: 1, 2 or 5 × 10ⁿ at or above the value.</summary>
    protected static double NiceMax(double value)
    {
        if (value <= 0) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1, 2, 2.5, 5, 10 })
        {
            if (step * magnitude >= value) return step * magnitude;
        }
        return 10 * magnitude;
    }

    protected static string Number(double value) =>
        value >= 100 || Math.Abs(value - Math.Round(value)) < 0.05 ? value.ToString("N0") : value.ToString("N1");
}
