using System.Text.Json;
using ADControlCenter.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADControlCenter.Tests;

[TestClass]
public sealed class DirectoryTests
{
    private string _folder = null!;
    private DemoDirectoryService _service = null!;

    [TestInitialize]
    public void Start()
    {
        _folder = Path.Combine(Path.GetTempPath(), "adcc-tests", Guid.NewGuid().ToString("N"));
        _service = new DemoDirectoryService(_folder);
    }

    [TestCleanup]
    public void Finish()
    {
        _service.Dispose();
        Directory.Delete(_folder, true);
    }

    [TestMethod]
    public void SamplesContainConsistentInventoryAndMemberships()
    {
        var d = _service.Read();
        Assert.AreEqual(128, d.Users.Count);
        Assert.AreEqual(84, d.Computers.Count);
        Assert.AreEqual(12, d.Groups.Count);
        Assert.AreEqual(4, d.Users.Count(u => u.Status == "Locked"));
        Assert.AreEqual(10, d.Computers.Count(c => c.IsStale));
        Assert.IsTrue(d.Groups.SelectMany(g => g.Members).All(id => d.Users.Any(u => u.Id == id)));
    }

    [TestMethod]
    public void EditsAndSimulationsPersistAcrossReopen()
    {
        _service.SaveUser(null, new(" Fictional Person ", " Demo.Test ", " Design "));
        var id = _service.Read().Users.Single(u => u.Username == "demo.test").Id;
        _service.PerformAccountAction(id, AccountAction.Disable);
        _service.SaveUser(id, new("New Name", "demo.test", "Research"));
        Assert.IsFalse(_service.Read().Users.Single(u => u.Id == id).Enabled);
        _service.PerformAccountAction(id, AccountAction.Enable);
        _service.PerformAccountAction(id, AccountAction.ResetPassword);
        var locked = _service.Read().Users.First(u => u.Locked);
        _service.PerformAccountAction(locked.Id, AccountAction.Unlock);
        _service.Dispose();
        _service = new(_folder);
        var edited = _service.Read().Users.Single(u => u.Id == id);
        Assert.AreEqual("New Name", edited.DisplayName);
        Assert.IsTrue(edited.Enabled);
        Assert.IsNotNull(edited.LastPasswordSimulation);
        Assert.IsFalse(_service.Read().Users.Single(u => u.Id == locked.Id).Locked);
        Assert.AreEqual("DEMO unlock", _service.Read().Audit[0].Action);
    }

    [TestMethod]
    [DataRow("", "valid", "Design")]
    [DataRow("Name", "bad name", "Design")]
    [DataRow("Name", "valid", " ")]
    [DataRow("Name", "AVERY.VALE", "Design")]
    public void InvalidUsersDoNotChangeState(string name, string username, string unit)
    {
        var before = _service.Read();
        Assert.ThrowsExactly<DirectoryException>(() => _service.SaveUser(null, new(name, username, unit)));
        Assert.AreSame(before, _service.Read());
    }

    [TestMethod]
    public void MembershipCanBeAddedAndRemovedWithoutDuplicates()
    {
        var users = _service.Read().Users;
        _service.SaveGroup(null, "Test readers", "Fictional group", [users[0].Id, users[1].Id, users[0].Id]);
        var group = _service.Read().Groups.Single(g => g.Name == "Test readers");
        Assert.AreEqual(2, group.MemberCount);
        _service.SaveGroup(group.Id, "Test readers", "Changed", [users[1].Id]);
        CollectionAssert.AreEqual(new[] { users[1].Id }, _service.Read().Groups.Single(g => g.Id == group.Id).Members.ToArray());
        Assert.ThrowsExactly<DirectoryException>(() => _service.SaveGroup(group.Id, "Test readers", "", [Guid.NewGuid()]));
        Assert.ThrowsExactly<DirectoryException>(() => _service.SaveGroup(null, "TEST READERS", "", []));
    }

    [TestMethod]
    public void QuotedReorderedCsvPreviewsThenAppliesOnce()
    {
        string csv = "\uFEFFUsername,Department,DisplayName\r\nmira.test,Operations,\"Test, Mira\"\r\n";
        var preview = _service.PreviewImport(csv, "test.csv");
        Assert.AreEqual(128, _service.Read().Users.Count);
        _service.Record("Page viewed", "Reports", "Success", "Navigation");
        _service.ApplyImport(preview);
        Assert.AreEqual("Test, Mira", _service.Read().Users.Single(u => u.Username == "mira.test").DisplayName);
        Assert.ThrowsExactly<DirectoryException>(() => _service.ApplyImport(preview));
        Assert.AreEqual(129, _service.Read().Users.Count);
    }

    [TestMethod]
    public void DuplicateAndMalformedRowsBlockWholeBatch()
    {
        var preview = _service.PreviewImport("DisplayName,Username,Department\nA,new.user,IT\nB,NEW.USER,IT\nC,c\nD,avery.vale,Finance", "errors.csv");
        Assert.AreEqual(3, preview.Rows.Count(r => r.Error.Length > 0));
        Assert.IsFalse(preview.CanApply);
        Assert.ThrowsExactly<DirectoryException>(() => _service.ApplyImport(preview));
        Assert.AreEqual(128, _service.Read().Users.Count);
    }

    [TestMethod]
    [DataRow("Name,Username,Department\na,b,c")]
    [DataRow("DisplayName,Username,Department")]
    [DataRow("DisplayName,Username,Department\n\"unfinished,x,y")]
    public void InvalidFilesAreRejected(string csv) =>
        Assert.ThrowsExactly<DirectoryException>(() => _service.PreviewImport(csv, "test.csv"));

    [TestMethod]
    public void ModifiedPreviewIsRevalidatedWithoutPartialWrites()
    {
        var fake = new ImportPreview([new(2, new("New", "new.user", "IT"), ""), new(3, new("Other", "avery.vale", "IT"), "")], _service.Read().Revision, "test.csv");
        Assert.ThrowsExactly<DirectoryException>(() => _service.ApplyImport(fake));
        Assert.IsFalse(_service.Read().Users.Any(u => u.Username == "new.user"));
    }

    [TestMethod]
    public void InterruptedWriteKeepsPriorSnapshotAndFile()
    {
        var before = _service.Read();
        string file = Path.Combine(_folder, "directory.json");
        string original = File.ReadAllText(file);
        using var held = new FileStream(file + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        Assert.ThrowsExactly<IOException>(() => _service.Clear());
        Assert.AreSame(before, _service.Read());
        Assert.AreEqual(original, File.ReadAllText(file));
    }

    [TestMethod]
    public void WorkspaceCannotHaveTwoWriters()
    {
        Assert.ThrowsExactly<IOException>(() => { using var other = new DemoDirectoryService(_folder); });
    }

    [TestMethod]
    public void ConcurrentCreatesHaveOneWinner()
    {
        int successes = 0;
        Parallel.For(0, 6, i =>
        {
            try { _service.SaveUser(null, new("Thread " + i, "same.name", "Design")); Interlocked.Increment(ref successes); }
            catch (DirectoryException) { }
        });
        Assert.AreEqual(1, successes);
        Assert.AreEqual(129, _service.Read().Users.Count);
    }

    [TestMethod]
    public void ClearAndRestorePreserveSettingsAndAudit()
    {
        var settings = new Preferences("Light", Path.Combine(_folder, "exports"));
        _service.SaveSettings(settings);
        _service.Clear();
        var empty = _service.Read();
        Assert.AreEqual(0, empty.Users.Count + empty.Computers.Count + empty.Groups.Count);
        _service.Reset();
        var d = _service.Read();
        Assert.AreEqual(settings, d.Settings);
        Assert.AreEqual(128, d.Users.Count);
        Assert.IsTrue(d.Audit.Any(a => a.Action == "Directory cleared"));
        Assert.AreEqual("Samples restored", d.Audit[0].Action);
        Assert.IsTrue(d.Audit.All(a => a.Timestamp != default && a.Actor.Length > 0 && a.Target.Length > 0 && a.Outcome.Length > 0 && a.Details.Length > 0));
    }

    [TestMethod]
    public void InvalidPreferencesAreRejected()
    {
        Assert.ThrowsExactly<DirectoryException>(() => _service.SaveSettings(new("Unknown", _folder)));
        Assert.ThrowsExactly<DirectoryException>(() => _service.SaveSettings(new("Dark", "relative")));
    }

    [TestMethod]
    [DataRow(ReportKind.Users, ReportFormat.Csv)]
    [DataRow(ReportKind.Users, ReportFormat.Json)]
    [DataRow(ReportKind.Users, ReportFormat.Html)]
    [DataRow(ReportKind.Computers, ReportFormat.Csv)]
    [DataRow(ReportKind.Computers, ReportFormat.Json)]
    [DataRow(ReportKind.Computers, ReportFormat.Html)]
    public void ExportsUseRequestedDatasetAndFormat(ReportKind kind, ReportFormat format)
    {
        _service.SaveUser(null, new("=1+1", "formula.test", "<unit>"));
        string file = _service.Export(kind, format);
        Assert.IsTrue(File.Exists(file));
        Assert.AreEqual("." + format.ToString().ToLowerInvariant(), Path.GetExtension(file));
        string text = File.ReadAllText(file);
        Assert.IsTrue(text.Contains(kind == ReportKind.Users ? "avery.vale" : "NS-SRV-001"));
        if (format == ReportFormat.Json)
        {
            using var json = JsonDocument.Parse(text);
            Assert.AreEqual(kind == ReportKind.Users ? 129 : 84, json.RootElement.GetArrayLength());
        }
        if (format == ReportFormat.Html && kind == ReportKind.Users) StringAssert.Contains(text, "&lt;unit&gt;");
        if (format == ReportFormat.Csv && kind == ReportKind.Users) StringAssert.Contains(text, "\"'=1+1\"");
        Assert.AreEqual("Report exported", _service.Read().Audit[0].Action);
    }

    [TestMethod]
    public void CorruptStoreIsPreservedForRecovery()
    {
        _service.Dispose();
        var file = Path.Combine(_folder, "directory.json");
        File.WriteAllText(file, "{bad");
        Assert.ThrowsExactly<JsonException>(() => { using var broken = new DemoDirectoryService(_folder); });
        Assert.AreEqual("{bad", File.ReadAllText(file));
    }
}
