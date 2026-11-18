using System.Windows;
using System.Windows.Media;

namespace FoTestingApp.Controls.Charts;

/// <summary>
/// How many tests of a run passed, as a ring: the passed share in the online colour, the rest in
/// the offline colour, a 2 px gap between them, and "7/9" with "lulus" in the middle so the number,
/// not the colour, carries it. Hover names both counts.
/// </summary>
public sealed class ScoreRing : ChartBase
{
    private int _passed;
    private int _total;

    public ScoreRing()
    {
        Width = 112;
        Height = 112;
    }

    public void SetScore(int passed, int total)
    {
        _total = Math.Max(0, total);
        _passed = Math.Clamp(passed, 0, _total);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        const double thickness = 10;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Min(ActualWidth, ActualHeight) / 2 - thickness / 2 - 1;

        if (_total == 0 || _passed == 0 || _passed == _total)
        {
            var key = _total == 0 ? "SurfaceContainerHighestBrush" : _passed == _total ? "StatusOnlineBrush" : "StatusOfflineBrush";
            dc.DrawEllipse(null, new Pen(Token(key), thickness), center, radius, radius);
        }
        else
        {
            // Two arcs from 12 o'clock, a small gap at each joint.
            const double gapDeg = 4;
            var passSweep = 360.0 * _passed / _total;
            dc.DrawGeometry(null, Line("StatusOnlineBrush", thickness), Arc(center, radius, gapDeg / 2, passSweep - gapDeg / 2));
            dc.DrawGeometry(null, Line("StatusOfflineBrush", thickness), Arc(center, radius, passSweep + gapDeg / 2, 360 - gapDeg / 2));
        }

        var score = Label($"{_passed}/{_total}", "OnSurfaceBrush", 22, medium: true);
        var caption = Label("lulus");
        var blockHeight = score.Height + caption.Height;
        dc.DrawText(score, new Point(center.X - score.Width / 2, center.Y - blockHeight / 2));
        dc.DrawText(caption, new Point(center.X - caption.Width / 2, center.Y - blockHeight / 2 + score.Height));

        if (Hover is { } point && _total > 0)
        {
            DrawTooltip(dc, point, $"{_passed} lulus", $"{_total - _passed} gagal");
        }
    }

    private static Geometry Arc(Point c, double r, double fromDeg, double toDeg)
    {
        Point At(double deg)
        {
            var rad = (deg - 90) * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(fromDeg), false, false);
            ctx.ArcTo(At(toDeg), new Size(r, r), 0, toDeg - fromDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}
