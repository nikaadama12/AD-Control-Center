using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ADControlCenter.App.Presentation;
namespace ADControlCenter.App.Views;

public partial class OnboardingView : UserControl
{
    public OnboardingView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is OnboardingPage old) old.PropertyChanged -= StageChanged;
            if (e.NewValue is OnboardingPage page) page.PropertyChanged += StageChanged;
        };
        Unloaded += (_, _) => Secret.Clear();
    }
    private void StageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OnboardingPage.Stage) && DataContext is OnboardingPage { Stage: not 4 }) Secret.Clear();
    }
    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OnboardingPage { Stage: 4 } page) return;
        using var value = Secret.SecurePassword;
        page.SetPassword(value);
    }
}
