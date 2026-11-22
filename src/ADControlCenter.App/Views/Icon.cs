using System.Windows;
using System.Windows.Media;

namespace ADControlCenter.App.Views;

public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(Icon), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(Icon), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    private static readonly Dictionary<string, Geometry> Shapes = new Dictionary<string, string>
    {
        ["Logo"] = "M3,8 L12,3 21,8 12,13 Z M3,12 L12,17 21,12 M3,16 L12,21 21,16",
        ["Dashboard"] = "M3,3 H10 V12 H3 Z M14,3 H21 V8 H14 Z M14,12 H21 V21 H14 Z M3,16 H10 V21 H3 Z",
        ["Users"] = "M16,8 A4,4 0 1 1 8,8 A4,4 0 1 1 16,8 M4,21 V19 C4,13 20,13 20,19 V21",
        ["Computers"] = "M3,4 H21 V16 H3 Z M12,16 V20 M7,21 H17",
        ["Groups"] = "M13,8 A3.5,3.5 0 1 1 6,8 A3.5,3.5 0 1 1 13,8 M2,20 V18 C2,12 17,12 17,18 V20 M16,5 C21,5 21,11 17,12 M18,14 Q22,15 22,19",
        ["Bulk"] = "M3,15 V20 H21 V15 M12,3 V15 M7,10 L12,15 17,10",
        ["Reports"] = "M5,3 H15 L20,8 V21 H5 Z M14,3 V9 H20 M8,17 V14 M12,17 V11 M16,17 V13",
        ["Audit"] = "M6,4 H19 V21 H4 V4 H6 M8,2 H15 V6 H8 Z M8,11 H15 M8,15 H15",
        ["Settings"] = "M10,2 H14 L15,5 18,6 21,6 23,10 20,12 20,14 22,17 19,20 16,19 14,20 13,22 9,22 8,19 5,18 3,18 1,14 4,12 4,10 2,7 5,4 8,5 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12",
        ["History"] = "M3,11 A9,9 0 1 1 5,18 M2,5 V11 H8 M12,7 V12 L16,14",
        ["Refresh"] = "M4,10 A8,8 0 0 1 18,6 L20,8 M20,3 V8 H15 M20,14 A8,8 0 0 1 6,18 L4,16 M4,21 V16 H9",
        ["Directory"] = "M3,5 H10 L12,8 H21 V20 H3 Z",
        ["Arrow"] = "M5,12 H19 M14,7 L19,12 14,17",
        ["Shield"] = "M12,2 L20,6 V12 Q20,18 12,22 Q4,18 4,12 V6 Z M8,12 L11,15 16,9",
        ["Search"] = "M16,10 A6,6 0 1 1 4,10 A6,6 0 1 1 16,10 M15,15 L21,21",
        ["Calendar"] = "M3,5 H21 V21 H3 Z M7,2 V8 M17,2 V8 M3,10 H21"
    }.ToDictionary(p => p.Key, p => Geometry.Parse(p.Value));
    protected override void OnRender(DrawingContext dc)
    {
        if (!Shapes.TryGetValue(Kind, out var geometry)) return;
        double size = Math.Min(ActualWidth, ActualHeight);
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 24, size / 24));
        var pen = new Pen(Ink, Kind == "Logo" ? 2.2 : 1.65) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, geometry); dc.Pop(); dc.Pop();
    }
}
