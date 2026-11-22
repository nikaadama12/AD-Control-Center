using System.Windows;
using ADControlCenter.Core;
using ADControlCenter.App.Presentation;
using ADControlCenter.App.Views;

namespace ADControlCenter.App;

public partial class App : Application
{
    private DemoDirectoryService? _provider;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string folder = Environment.GetEnvironmentVariable("ADCC_DEMO_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AD Control Center");
        try
        {
            _provider = new DemoDirectoryService(folder);
            var ui = new Interaction();
            ui.ApplyTheme(_provider.Settings.Theme);
            var shell = new ShellViewModel(_provider, _provider, ui);
            MainWindow = new ShellWindow { DataContext = shell };
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open AD Control Center.\n\n{ex.Message}\n\nWorkspace: {folder}", "AD Control Center", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (e.ApplicationExitCode == 0)
                _provider?.Record("Workspace closed", "northstar.example", "Success", "Operator closed the application.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"The final audit entry could not be saved.\n\n{ex.Message}", "AD Control Center", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _provider?.Dispose(); }
        base.OnExit(e);
    }
}
