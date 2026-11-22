using System.Windows.Controls;
using System.Windows;
using ADControlCenter.App.Presentation;
using ADControlCenter.Core.Live;
namespace ADControlCenter.App.Views;

public partial class LiveRecordsView : UserControl
{
    public LiveRecordsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not LiveRecordsPage page) return;
            Account.Visibility = page.IsHierarchy ? Visibility.Collapsed : Visibility.Visible;
            Platform.Visibility = page.Kind == ObjectKind.Computers ? Visibility.Visible : Visibility.Collapsed;
            State.Visibility = page.Kind is ObjectKind.Computers or ObjectKind.Users ? Visibility.Visible : Visibility.Collapsed;
        };
    }
}
