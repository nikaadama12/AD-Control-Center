using System.IO;
using System.Security;
using System.Windows.Threading;
using ADControlCenter.App.Presentation;
using ADControlCenter.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADControlCenter.LiveTests;

public sealed partial class LiveTests
{
    [STATestMethod]
    [DataRow(false, ReportKind.Users)]
    [DataRow(true, ReportKind.Users)]
    [DataRow(false, ReportKind.CountsOnly)]
    [DataRow(true, ReportKind.CountsOnly)]
    public void ExportRequiresConfirmationBeforeCreatingAnyFile(bool live, ReportKind kind)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"adcc-confirm-{Guid.NewGuid():N}");
        string destination = Path.Combine(folder, "PrivateReports");
        var prior = SynchronizationContext.Current;
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try
        {
            using var demo = new DemoDirectoryService(folder);
            demo.SaveSettings(demo.Settings with { ExportDirectory = destination });
            var ui = new Dialogs();
            var shell = new ShellViewModel(demo, demo, ui, new Connector(), destination);
            if (live)
            {
                shell.Navigate.Execute("Connection Manager");
                var form = (ConnectionPage)shell.Page;
                form.UserPrincipalName = "fixture@example.test";
                using var secret = new SecureString();
                secret.AppendChar('x');
                var bind = shell.ConnectAsync(form, secret, false);
                var frame = new DispatcherFrame();
                bind.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                bind.GetAwaiter().GetResult();
            }
            Assert.AreEqual(live, shell.IsLive);
            shell.Navigate.Execute("Reports");
            var report = (ReportsPage)shell.Page;
            report.Selected = report.Choices.Single(c => c.Kind == kind);
            shell.Run.Execute("Export");
            Assert.AreEqual(1, ui.Confirms);
            StringAssert.Contains(ui.Prompt, report.Selected.Description);
            StringAssert.Contains(ui.Prompt, destination);
            Assert.IsFalse(Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any());
            ui.Accept = true;
            shell.Run.Execute("Export");
            Assert.AreEqual(2, ui.Confirms);
            string path = Directory.GetFiles(destination).Single();
            var lines = File.ReadAllLines(path);
            Assert.AreEqual(kind == ReportKind.CountsOnly ? 7 : live ? 122 : 129, lines.Length);
            if (kind == ReportKind.CountsOnly) Assert.AreEqual($"\"Total users\",\"{(live ? 121 : 128)}\"", lines[1]);
            if (live) shell.Run.Execute("Disconnect");
        }
        finally { SynchronizationContext.SetSynchronizationContext(prior); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
