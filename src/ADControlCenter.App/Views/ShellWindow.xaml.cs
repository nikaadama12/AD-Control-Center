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
        bool waitingToClose = false;
        Closing += (_, e) =>
        {
            if (DataContext is not Presentation.ShellViewModel { Busy: true } shell) return;
            e.Cancel = true;
            if (waitingToClose) return;
            waitingToClose = true;
            shell.CancelOperation();
            System.ComponentModel.PropertyChangedEventHandler? finished = null;
            finished = (_, change) =>
            {
                if (change.PropertyName != nameof(shell.Busy) || shell.Busy) return;
                shell.PropertyChanged -= finished;
                Dispatcher.BeginInvoke(Close);
            };
            shell.PropertyChanged += finished;
        };
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
