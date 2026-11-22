using System.Windows;
using System.Windows.Controls;
namespace ADControlCenter.App.Views;

public partial class EmployeeFields : UserControl
{
    public static readonly DependencyProperty ReadOnlyIdentityProperty = DependencyProperty.Register(nameof(ReadOnlyIdentity), typeof(bool), typeof(EmployeeFields));
    public bool ReadOnlyIdentity { get => (bool)GetValue(ReadOnlyIdentityProperty); set => SetValue(ReadOnlyIdentityProperty, value); }
    public EmployeeFields() => InitializeComponent();
}
