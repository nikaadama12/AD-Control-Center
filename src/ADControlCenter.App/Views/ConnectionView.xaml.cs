using System.Windows;
using System.Windows.Controls;
using ADControlCenter.App.Presentation;

namespace ADControlCenter.App.Views;

public partial class ConnectionView : UserControl
{
    public ConnectionView() => InitializeComponent();
    private async void TestClick(object sender, RoutedEventArgs e) => await Authenticate(true);
    private async void ConnectClick(object sender, RoutedEventArgs e) => await Authenticate(false);
    private async Task Authenticate(bool test)
    {
        if (DataContext is not ConnectionPage form || Window.GetWindow(this)?.DataContext is not ShellViewModel shell) return;
        using var secret = Secret.SecurePassword;
        Secret.Clear();
        await shell.ConnectAsync(form, secret, test);
    }
}
