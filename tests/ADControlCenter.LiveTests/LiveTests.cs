using System.IO;
using System.Net;
using System.Security;
using System.Windows.Threading;
using ADControlCenter.App.Presentation;
using ADControlCenter.Core;
using ADControlCenter.Core.Live;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADControlCenter.LiveTests;

[TestClass]
public sealed partial class LiveTests
{
    private static LiveObject Row(int number) => new(ObjectKind.Users, $"Fixture {number:D3}", $"CN=Fixture {number},OU=Mock,DC=example",
        $"mock{number}", number % 2 == 0 ? "Enabled" : "Disabled", "", "", "", "", null, [], []);
    private sealed class FakeReader : ILiveReader
    {
        public string NamingContext => "DC=example";
        public bool Closed { get; private set; }
        public bool Fail { get; set; }
        public int Reads { get; private set; }
        public IReadOnlyList<LiveObject> Read(CancellationToken token)
        {
            Reads++;
            if (Fail) throw new DirectoryException("Mock read failed.");
            return Enumerable.Range(0, 121).Select(Row).ToArray();
        }
        public void Dispose() => Closed = true;
    }
    private sealed class Connector : ILiveConnector
    {
        public FakeReader Reader { get; } = new();
        public ILiveReader Open(Controller controller, NetworkCredential credential) => Reader;
    }
    private sealed class Dialogs : IInteraction
    {
        public int Confirms { get; private set; }
        public bool Accept { get; set; }
        public string Prompt { get; private set; } = "";
        public bool Confirm(string title, string details, string accept) { Confirms++; Prompt = details; return Accept; }
        public void Inform(string title, string details) { }
        public bool EditUser(DirectoryUser? user, Action<UserInput> save) => throw new AssertFailedException();
        public bool EditGroup(DirectoryGroup? group, IReadOnlyList<DirectoryUser> users, Func<string, string, IReadOnlyList<Guid>, bool> save) => throw new AssertFailedException();
        public string? ChooseCsv() => throw new AssertFailedException();
        public string? ChooseFolder(string current) => throw new AssertFailedException();
        public void ApplyTheme(string theme) { }
    }
    private static Controller Endpoint => new("dc.fixture.example", Protection.Ldaps);

    [TestMethod]
    public void AuthenticatedTestClosesWithoutLoadingObjects()
    {
        var fake = new Connector();
        using var session = new LiveSession(fake);
        session.Open(Endpoint, new(), true, default);
        Assert.IsFalse(session.Connected);
        Assert.IsTrue(fake.Reader.Closed);
        Assert.AreEqual(0, fake.Reader.Reads);
    }

    [TestMethod]
    public void FailedInitialLoadDiscardsCandidate()
    {
        var fake = new Connector();
        fake.Reader.Fail = true;
        using var session = new LiveSession(fake);
        Assert.ThrowsExactly<DirectoryException>(() => session.Open(Endpoint, new(), false, default));
        Assert.IsFalse(session.Connected);
        Assert.IsTrue(fake.Reader.Closed);
        Assert.AreEqual(0, session.Objects.Count);
    }

    [TestMethod]
    public void FailedRefreshRetainsWholeSnapshotAndDisconnectDropsIt()
    {
        var fake = new Connector();
        using var session = new LiveSession(fake);
        session.Open(Endpoint, new(), false, default);
        var old = session.Objects;
        fake.Reader.Fail = true;
        Assert.ThrowsExactly<DirectoryException>(() => session.Refresh(default));
        Assert.AreSame(old, session.Objects);
        session.Dispose();
        Assert.AreEqual(0, session.Objects.Count);
        Assert.IsTrue(fake.Reader.Closed);
    }

    [TestMethod]
    [DataRow("192.168.56.10")]
    [DataRow("dc")]
    [DataRow("ldap://dc.example:389")]
    public void RequiresDnsHostname(string host) =>
        Assert.ThrowsExactly<DirectoryException>(() => new Controller(host, Protection.Ldaps).Validate());

    [TestMethod]
    public void PagingSearchAndFiltersApplyToWholeSnapshot()
    {
        var rows = Enumerable.Range(0, 121).Select(Row).ToArray();
        var page = LiveQuery.Browse(rows, "fixture", "OU=Mock,DC=example", "Enabled", 2);
        Assert.AreEqual(61, page.Total);
        Assert.AreEqual(11, page.Items.Count);
        Assert.IsTrue(page.Items.All(o => o.Status == "Enabled"));
        Assert.AreEqual(1, LiveQuery.Browse(rows, "mock120", "", "", 1).Total);
        Assert.AreEqual("OU=Parent,DC=example", LiveObject.ParentOf(@"OU=Escaped\, Name,OU=Parent,DC=example"));
    }

    [TestMethod]
    public void LiveReportsEncodeValuesAndRefuseGitDestinations()
    {
        string folder = Path.Combine(Path.GetTempPath(), "adcc-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            LiveObject[] rows = [Row(1) with { Name = "=2+2", Email = "<private>" }];
            var csv = File.ReadAllText(ReportWriter.WriteLive(folder, rows, ReportKind.Users, ReportFormat.Csv));
            StringAssert.Contains(csv, "'=2+2");
            var html = File.ReadAllText(ReportWriter.WriteLive(folder, rows, ReportKind.Users, ReportFormat.Html));
            StringAssert.Contains(html, "&lt;private&gt;");
            Assert.ThrowsExactly<DirectoryException>(() => ReportWriter.WriteLive(folder, rows, ReportKind.Users, ReportFormat.Json));
            Directory.CreateDirectory(Path.Combine(folder, ".git"));
            Assert.ThrowsExactly<DirectoryException>(() => ReportWriter.WriteLive(Path.Combine(folder, "reports"), rows, ReportKind.Users, ReportFormat.Csv));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [STATestMethod]
    public void ShellSwitchesDataAndRejectsEveryDemoMutation()
    {
        var originalContext = SynchronizationContext.Current;
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        string folder = Path.Combine(Path.GetTempPath(), "adcc-mode-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var demo = new DemoDirectoryService(folder);
            var fake = new Connector();
            var ui = new Dialogs();
            var shell = new ShellViewModel(demo, demo, ui, fake);
            shell.Navigate.Execute("Connection Manager");
            var form = (ConnectionPage)shell.Page;
            form.Host = Endpoint.Host;
            form.UserPrincipalName = "fixture@example.test";
            using var secret = new SecureString();
            foreach (char c in Guid.NewGuid().ToString("N")) secret.AppendChar(c);
            var pending = shell.ConnectAsync(form, secret, false);
            var frame = new DispatcherFrame();
            pending.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            pending.GetAwaiter().GetResult();
            Assert.AreEqual("LIVE READ-ONLY MODE", shell.ModeLabel);
            Assert.AreEqual(121, ((DashboardPage)shell.Page).Total);
            shell.Navigate.Execute("Users");
            var page = (LiveRecordsPage)shell.Page;
            page.Selected = page.Result.Items.First();
            long revision = demo.Read().Revision;
            foreach (var action in new[] { "New", "Edit", "Enable", "Disable", "Unlock", "ResetPassword", "Import", "Apply", "Clear", "Reset", "SaveSettings" })
            {
                Assert.IsFalse(shell.Run.CanExecute(action), action);
                shell.Run.Execute(action);
            }
            shell.Navigate.Execute("Bulk Operations");
            Assert.AreSame(page, shell.Page);
            shell.Navigate.Execute("Reports");
            Assert.AreEqual(2, ((ReportsPage)shell.Page).Formats.Length);
            shell.Run.Execute("Export");
            Assert.AreEqual(1, ui.Confirms);
            Assert.AreEqual(revision, demo.Read().Revision);
            Assert.IsFalse(File.ReadAllText(Path.Combine(folder, "directory.json")).Contains("mock120"));
            shell.Run.Execute("Disconnect");
            Assert.AreEqual("DEMO MODE", shell.ModeLabel);
            Assert.AreEqual(128, ((DashboardPage)shell.Page).Total);
            Assert.IsTrue(fake.Reader.Closed);
        }
        finally { SynchronizationContext.SetSynchronizationContext(originalContext); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
