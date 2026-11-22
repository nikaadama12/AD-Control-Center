using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ADControlCenter.App.Views;

public partial class ShellWindow : Window
{
    public ShellWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowChrome.Apply(this);
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        SizeChanged += (_, _) => Rail.Width = new(ActualWidth < 1120 ? 177 : 214);
    }
}

public sealed class SameValue : IMultiValueConverter
{
    public object Convert(object[] values, Type type, object parameter, CultureInfo culture) => values.Length == 2 && Equals(values[0], values[1]);
    public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
