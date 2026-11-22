using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ADControlCenter.App.Presentation;

namespace ADControlCenter.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();
}

public sealed class RingChart : FrameworkElement
{
    public RingChart() => DataContextChanged += (_, _) => InvalidateVisual();
    protected override void OnRender(DrawingContext dc)
    {
        if (DataContext is not DashboardPage page) return;
        var center = new Point(60, 60);
        dc.DrawEllipse(null, new Pen((Brush)FindResource("Hover"), 11), center, 48, 48);
        if (page.Total == 0) return;
        double angle = -Math.PI / 2;
        Point At(double a) => new(60 + Math.Cos(a) * 48, 60 + Math.Sin(a) * 48);
        foreach (var item in page.Health.Where(i => i.Count > 0))
        {
            double span = 2 * Math.PI * item.Count / page.Total;
            var pen = new Pen((Brush)new BrushConverter().ConvertFromString(item.Color)!, 11);
            if (item.Count == page.Total) dc.DrawEllipse(null, pen, center, 48, 48);
            else
            {
                double gap = Math.Min(.026, span / 4);
                var arc = new StreamGeometry();
                using (var ctx = arc.Open())
                {
                    ctx.BeginFigure(At(angle + gap), false, false);
                    ctx.ArcTo(At(angle + span - gap), new Size(48, 48), 0, span - 2 * gap > Math.PI, SweepDirection.Clockwise, true, false);
                }
                dc.DrawGeometry(null, pen, arc);
            }
            angle += span;
        }
    }
}
