using ADControlCenter.App.Presentation;
using ADControlCenter.Core;
using ADControlCenter.Core.Admin;
using ADControlCenter.Core.Live;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Net;
using System.Security;
using System.Text.Json;

namespace ADControlCenter.LiveTests;

[TestClass]
public sealed partial class AdministrationTests
{
    private string _folder = null!;
    private DemoDirectoryService _demo = null!;
    private MemoryAudit _audit = null!;
    private Stub _backend = null!;
    private AdminWorkflow _work = null!;
    private Employee Person => new("Tess", "Fixture", "Tess Fixture", "tess.fixture", "tess.fixture@northstar.example", "IT", "Analyst", "tess@example.test", "Disposable fixture");
    private Hire Plan => new(Person, "Engineering", [_demo.Catalog().Groups[0].Id], true, true);
    private static readonly string FixtureMarker = Guid.NewGuid().ToString("N");
    private static SecureString Secret()
    {
        var secret = new SecureString();
        foreach (char c in FixtureMarker) secret.AppendChar(c);
        return secret;
    }
    private sealed class MemoryAudit : IAdminAudit
    {
        public bool Broken;
        public List<AdminEvent> Entries { get; } = [];
        public void Append(AdminEvent e) { if (Broken) throw new IOException(); Entries.Add(e); }
        public IReadOnlyList<AdminEvent> History() => Entries;
    }
    private sealed class Stub(IAdminBackend inner) : IAdminBackend
    {
        public AdminAction? Deny, Unknown;
        public Guid? DeniedGroup;
        public List<AdminAction> Submitted { get; } = [];
        public string Actor => "Fixture Operator";
        public AdminCatalog Catalog() => inner.Catalog();
        public PreparedChange Prepare(AdminCommand c, SecureString? p)
        {
            var change = inner.Prepare(c, p);
            return change with
            {
                Commit = () =>
            {
                Submitted.Add(c.Action);
                if (c.Action == Deny || c.Group == DeniedGroup) throw new DirectoryException("The controller denied the delegated operation.");
                if (c.Action == Unknown) { change.Commit(); throw new IOException(FixtureMarker); }
                return change.Commit();
            }
            };
        }
    }
    private sealed class Interaction : IInteraction
    {
        public bool Approve = true;
        public string Preview = "", Info = "";
        public bool Confirm(string title, string description, string accept) { Preview = description; return Approve; }
        public void Inform(string title, string description) => Info = description;
        public bool EditUser(DirectoryUser? user, Action<UserInput> save) => throw new AssertFailedException();
        public bool EditGroup(DirectoryGroup? group, IReadOnlyList<DirectoryUser> users, Func<string, string, IReadOnlyList<Guid>, bool> save) => throw new AssertFailedException();
        public string? ChooseCsv() => throw new AssertFailedException();
        public string? ChooseFolder(string current) => throw new AssertFailedException();
        public void ApplyTheme(string theme) { }
    }
    [TestInitialize]
    public void Start()
    {
        _folder = Directory.CreateTempSubdirectory("adcc-admin-").FullName;
        _demo = new(_folder); _backend = new(_demo); _audit = new(); _work = new(_backend, _audit);
    }
    [TestCleanup] public void Finish() { _demo.Dispose(); Directory.Delete(_folder, true); }
    private void Fill(EmployeeForm form)
    {
        var p = Person; form.FirstName = p.FirstName; form.LastName = p.LastName; form.DisplayName = p.DisplayName; form.Username = p.Username;
        form.Upn = p.Upn; form.Department = p.Department; form.Title = p.Title; form.Email = p.Email; form.Description = p.Description;
    }
    [STATestMethod]
    public async Task WizardValidatesEachStepAndShowsReviewedResults()
    {
        var ui = new Interaction();
        using var page = new OnboardingPage(_work, false, ui, f => Task.FromResult(f()));
        page.Advance(); Assert.AreEqual(1, page.Stage);
        Fill(page.Form); page.Advance(); Assert.AreEqual(2, page.Stage);
        page.Location = "Unapproved"; page.Advance(); Assert.AreEqual(2, page.Stage);
        page.Location = Plan.Location; page.Advance(); Assert.AreEqual(3, page.Stage);
        page.Template.Execute(null); Assert.IsTrue(page.Groups.Any(g => g.Selected && g.Reason.Contains("IT")));
        page.Advance(); Assert.AreEqual(4, page.Stage);
        page.Advance(); Assert.AreEqual(4, page.Stage);
        using var password = Secret(); page.SetPassword(password); page.Enabled = true;
        page.Advance(); Assert.AreEqual(5, page.Stage);
        StringAssert.Contains(page.Review, Person.Upn);
        Assert.IsFalse(page.Review.Contains(FixtureMarker));
        await page.SubmitAsync(); Assert.AreEqual(6, page.Stage);
        Assert.IsTrue(page.Result!.Success);
        page.Details.Execute(null); StringAssert.Contains(ui.Info, Person.DisplayName);
        var user = _demo.Read().Users.Single(u => u.Id == page.Result.User);
        Assert.AreEqual(Person, user.EmployeeInfo); Assert.IsTrue(user.ChangeAtLogon && user.Enabled);
        Assert.IsTrue(_demo.Read().Groups.Any(g => g.Members.Contains(user.Id)));
        Assert.IsFalse(File.ReadAllText(Path.Combine(_folder, "directory.json")).Contains(FixtureMarker));
        Assert.IsFalse(page.Result.Summary.Contains(Person.Username));
    }
    [STATestMethod]
    public void GoingBackFromPasswordStepDiscardsSecret()
    {
        using var page = new OnboardingPage(_work, false, new Interaction(), f => Task.FromResult(f()));
        Fill(page.Form); page.Advance(); page.Location = Plan.Location; page.Advance(); page.Advance();
        using var password = Secret(); page.SetPassword(password);
        page.Back.Execute(null); page.Advance(); page.Advance();
        Assert.AreEqual(4, page.Stage);
        Assert.AreEqual(0, _backend.Submitted.Count);
    }

    [STATestMethod]
    public async Task CancelledWizardClearsPasswordAndDoesNotSubmit()
    {
        var ui = new Interaction { Approve = false }; using var page = new OnboardingPage(_work, true, ui, f => Task.FromResult(f()));
        Fill(page.Form); page.Advance(); page.Location = Plan.Location; page.Advance(); page.Advance();
        using var password = Secret(); page.SetPassword(password); page.Advance(); await page.SubmitAsync();
        Assert.AreEqual(4, page.Stage); page.Advance(); Assert.AreEqual(4, page.Stage); Assert.AreEqual(0, _backend.Submitted.Count);
        StringAssert.Contains(ui.Preview, "LIVE ADMINISTRATION");
    }
    [TestMethod]
    [DataRow("name")]
    [DataRow("sam")]
    [DataRow("upn")]
    [DataRow("email")]
    [DataRow("department")]
    [DataRow("title")]
    [DataRow("duplicate")]
    [DataRow("ou")]
    [DataRow("group")]
    public void InvalidRequestsAreRejectedBeforeSubmission(string field)
    {
        var hire = Plan; var p = Person;
        hire = field switch
        {
            "name" => hire with { Profile = p with { FirstName = "" } },
            "sam" => hire with { Profile = p with { Username = "a,b" } },
            "upn" => hire with { Profile = p with { Upn = "test@other.test" } },
            "email" => hire with { Profile = p with { Email = "bad" } },
            "department" => hire with { Profile = p with { Department = "" } },
            "title" => hire with { Profile = p with { Title = "" } },
            "duplicate" => hire with { Profile = p with { Username = _demo.Read().Users[0].Username.ToUpperInvariant() } },
            "ou" => hire with { Location = "Unapproved" },
            _ => hire with { Groups = [Guid.NewGuid()] }
        };
        using var secret = Secret();
        Assert.ThrowsExactly<DirectoryException>(() => _work.Onboard(hire, secret)); Assert.AreEqual(0, _backend.Submitted.Count);
    }
    [TestMethod]
    [DataRow(AdminAction.CreateDisabled)]
    [DataRow(AdminAction.TemporaryPassword)]
    [DataRow(AdminAction.AddMember)]
    [DataRow(AdminAction.AccountSettings)]
    public void DeniedStageStopsAndRecordsPartialCompletion(AdminAction denied)
    {
        _backend.Deny = denied; using var password = Secret(); var result = _work.Onboard(Plan, password);
        Assert.IsFalse(result.Success); Assert.AreEqual(1, result.Steps.Count(s => s.Outcome == "Failed"));
        Assert.AreEqual(denied, _backend.Submitted.Last());
        Assert.AreEqual(denied == AdminAction.CreateDisabled ? 128 : 129, _demo.Read().Users.Count);
        Assert.IsFalse(_demo.Read().Users.Any(u => u.Username == Person.Username && u.Enabled));
        Assert.AreEqual(2 * _backend.Submitted.Count, _audit.Entries.Count);
        Assert.IsTrue(_audit.Entries.All(e => e.Utc.Offset == TimeSpan.Zero && e.Actor == "Fixture Operator" && e.OperationId == result.OperationId));
        Assert.IsFalse(JsonSerializer.Serialize(_audit.Entries).Contains(FixtureMarker));
    }
    [TestMethod]
    public void UncertainMembershipIsNeverRetriedOrReportedAsSuccess()
    {
        _backend.Unknown = AdminAction.AddMember; using var password = Secret(); var result = _work.Onboard(Plan, password);
        Assert.AreEqual("Unconfirmed", result.Steps[2].Outcome); Assert.AreEqual("Skipped", result.Steps.Last().Outcome);
        Assert.AreEqual(1, _backend.Submitted.Count(a => a == AdminAction.AddMember));
        Assert.IsFalse(JsonSerializer.Serialize(_audit.Entries).Contains(FixtureMarker));
    }
    [TestMethod]
    public void SecondGroupDenialPreservesFirstMembershipAndKeepsAccountDisabled()
    {
        var groups = _demo.Catalog().Groups.Take(2).ToArray();
        _backend.DeniedGroup = groups[1].Id;
        using var password = Secret();
        var result = _work.Onboard(Plan with { Groups = groups.Select(g => g.Id).ToArray() }, password);
        CollectionAssert.AreEqual(new[] { "Success", "Success", "Success", "Failed", "Skipped" },
            result.Steps.Select(s => s.Outcome).ToArray());
        var user = _demo.Catalog().Users.Single(u => u.Id == result.User);
        CollectionAssert.AreEqual(new[] { groups[0].Id }, user.Groups.ToArray());
        Assert.IsFalse(user.Enabled);
    }

    [TestMethod]
    public void InitialLdapRequestCreatesOnlyADisabledUserWithReviewedFields()
    {
        var profile = Person with { Upn = "tess.fixture@ad.lab.test", Email = "" };
        string dn = "CN=tess.fixture," + LabPolicy.Root;
        var request = LdapAdministration.CreateDisabledRequest(dn, profile);
        var attributes = request.Attributes.Cast<System.DirectoryServices.Protocols.DirectoryAttribute>()
            .ToDictionary(a => a.Name, a => (string)a[0]);
        Assert.AreEqual(dn, request.DistinguishedName);
        Assert.AreEqual("user", attributes["objectClass"]);
        Assert.AreEqual("514", attributes["userAccountControl"]);
        Assert.AreEqual(profile.Username, attributes["cn"]);
        Assert.AreEqual(profile.Upn, attributes["userPrincipalName"]);
        Assert.IsFalse(attributes.ContainsKey("unicodePwd") || attributes.ContainsKey("mail"));
    }

    [TestMethod]
    public void UnavailableAuditPreventsWrite()
    {
        _audit.Broken = true; using var password = Secret(); var result = _work.Onboard(Plan, password);
        Assert.IsFalse(result.Success); Assert.AreEqual(0, _backend.Submitted.Count);
    }
    [STATestMethod]
    public async Task AccountEditorComparesAndConfirmsAddsRemovalsEditsAndState()
    {
        using var pw = Secret(); var result = _work.Onboard(Plan, pw); var ui = new Interaction();
        var page = new AccountAdminPage(_work, false, ui, f => Task.FromResult(f()));
        page.Query = Person.Department; page.User = page.Users.Single(u => u.Id == result.User);
        var remove = page.Picks.First(g => g.Original); var add = page.Picks.First(g => !g.Original);
        remove.Selected = false; add.Selected = true; page.Enabled = false; page.Form.Title = "Senior analyst";
        StringAssert.Contains(page.MembershipChanges, remove.Group.Name); StringAssert.Contains(page.MembershipChanges, add.Group.Name);
        ui.Approve = false; int before = _backend.Submitted.Count; await page.SaveAsync(); Assert.AreEqual(before, _backend.Submitted.Count);
        ui.Approve = true; await page.SaveAsync(); Assert.IsTrue(page.Result!.Success);
        Assert.AreEqual("Senior analyst", page.User!.Profile.Title); Assert.IsFalse(page.User.Enabled);
        CollectionAssert.AreEqual(new[] { add.Group.Id }, page.User.Groups.ToArray());
        Assert.IsTrue(_audit.Entries.Any(e => e.Operation == "EditAttributes" && e.Before.Contains("Analyst") && e.After.Contains("Senior analyst")));
    }
    [TestMethod]
    public void DuplicateUpnAndStaleProposalsFail()
    {
        using var password = Secret(); var result = _work.Onboard(Plan, password);
        Assert.ThrowsExactly<DirectoryException>(() => _work.Onboard(Plan with { Profile = Person with { Username = "other.test" } }, password));
        var user = _demo.Catalog().Users.Single(u => u.Id == result.User);
        Assert.IsTrue(_work.Update(user, user.Profile with { Title = "Changed" }, true, user.Groups).Success);
        Assert.IsFalse(_work.Update(user, user.Profile with { Title = "Another" }, true, user.Groups).Success);
    }
    [TestMethod]
    public void ExistingUserWithMissingOnboardingAttributesCanBeEdited()
    {
        var user = _demo.Catalog().Users.First();
        Assert.AreEqual("", user.Profile.Title);
        Assert.IsTrue(_work.Update(user, user.Profile with { Description = "Fixture update" }, user.Enabled, user.Groups).Success);
        Assert.ThrowsExactly<DirectoryException>(() => AdminValidation.EditedFields(user.Profile, user.Profile with { Username = "renamed" }));
    }

    [TestMethod]
    public void LocalTemplateConfigurationIsApplied()
    {
        var group = _demo.Catalog().Groups.Last();
        File.WriteAllText(Path.Combine(_folder, "demo-templates.json"), JsonSerializer.Serialize(new Dictionary<string, string[]> { { "Support", [group.Name] } }));
        CollectionAssert.AreEqual(new[] { group.Id }, _demo.Catalog().Templates["Support"]);
    }

    [TestMethod]
    [DataRow("CN=Users,DC=ad,DC=lab,DC=test")]
    [DataRow("OU=AppTesting,DC=other,DC=test")]
    [DataRow("OU=OtherOU=AppTesting,DC=ad,DC=lab,DC=test")]
    [DataRow("OU=Bad\\,OU=AppTesting,DC=ad,DC=lab,DC=test")]
    [DataRow("CN=Builtin,OU=AppTesting,DC=ad,DC=lab,DC=test")]
    public void PolicyCannotExpandTheHardLabBoundary(string dn)
    {
        var policy = new LabPolicy { OperatorId = Guid.NewGuid(), ChildOus = [dn] };
        Assert.ThrowsExactly<DirectoryException>(policy.Validate); Assert.IsFalse(policy.AllowsOu(dn));
    }
    [TestMethod]
    public void OnlyApprovedChildOusAndTemplateGroupsAreAccepted()
    {
        string child = "OU=Disposable," + LabPolicy.Root; var id = Guid.NewGuid();
        var policy = new LabPolicy { OperatorId = Guid.NewGuid(), ChildOus = [child], ApprovedGroups = [id], Templates = new() { { "IT", [id] } } };
        policy.Validate(); Assert.IsTrue(policy.AllowsOu(child.ToLowerInvariant())); Assert.IsTrue(policy.AllowsOu(LabPolicy.Root));
        Assert.IsFalse(policy.AllowsOu("OU=Nested," + child));
        policy.Templates["HR"] = [Guid.NewGuid()]; Assert.ThrowsExactly<DirectoryException>(policy.Validate);
    }
    [TestMethod]
    [DataRow("Domain Admins", "S-1-5-21-1-2-3-512", false, false, false)]
    [DataRow("Enterprise Admins", "S-1-5-21-1-2-3-519", false, false, false)]
    [DataRow("Localized", "S-1-5-32-544", false, false, false)]
    [DataRow("Privileged", "S-1-5-21-1-2-3-1234", true, false, false)]
    [DataRow("System", "S-1-5-21-1-2-3-1234", false, true, false)]
    [DataRow("History", "S-1-5-21-1-2-3-1234", false, false, true)]
    public void ProtectedPrincipalsAreRejected(string name, string sid, bool protectedObject, bool critical, bool history) =>
        Assert.ThrowsExactly<DirectoryException>(() => LabPolicy.RequireOrdinary(name, sid, protectedObject, critical, history));

    [TestMethod]
    public void RejectsNestedDistributionAndPrivilegedEffectiveMemberships()
    {
        Assert.ThrowsExactly<DirectoryException>(() => LabPolicy.RequireGroupType(unchecked((int)0x80000002), true));
        Assert.ThrowsExactly<DirectoryException>(() => LabPolicy.RequireGroupType(2, false));
        const string domain = "S-1-5-21-1-2-3";
        LabPolicy.RequireMemberships([domain + "-513"], domain, 513, []);
        Assert.ThrowsExactly<DirectoryException>(() => LabPolicy.RequireMemberships([domain + "-513", domain + "-512"], domain, 513, []));
        Assert.ThrowsExactly<DirectoryException>(() => LabPolicy.RequireMemberships([], domain, 513, []));
        Assert.ThrowsExactly<DirectoryException>(() => LabPolicy.RequireMemberships([domain + "-513"], domain, 512, []));
    }

    [TestMethod]
    public void BoundIdentityMustMatchConfiguredDirectoryPrincipal()
    {
        const string sid = "S-1-5-21-1-2-3-1001", principal = @"LAB\fixture";
        LabPolicy.RequireBoundActor("u:" + sid, sid, principal);
        LabPolicy.RequireBoundActor(@"u:lab\FIXTURE", sid, principal);
        Assert.ThrowsExactly<DirectoryException>(() => LabPolicy.RequireBoundActor("u:someone-else", sid, principal));
        Assert.ThrowsExactly<DirectoryException>(() => LabPolicy.RequireBoundActor("u:", sid, ""));
    }

    [TestMethod]
    public void OnlyEnrolledUsersAndAllowlistedGroupsCanBeTargeted()
    {
        var existing = Guid.NewGuid(); var group = Guid.NewGuid(); var created = Guid.NewGuid();
        var policy = new LabPolicy { OperatorId = Guid.NewGuid(), ApprovedUsers = [existing], ApprovedGroups = [group] };
        HashSet<Guid> session = [created];
        policy.RequireUser(existing, session); policy.RequireUser(created, session); policy.RequireGroup(group);
        Assert.ThrowsExactly<DirectoryException>(() => policy.RequireUser(policy.OperatorId, session));
        Assert.ThrowsExactly<DirectoryException>(() => policy.RequireUser(created, new HashSet<Guid>()));
        Assert.ThrowsExactly<DirectoryException>(() => policy.RequireGroup(Guid.NewGuid()));
    }

    [TestMethod]
    public void LiveJournalPersistsTransitionsAndBlocksGitBeforeWriting()
    {
        var path = Path.Combine(_folder, "private", "audit.jsonl");
        var audit = new FileAdminAudit(path, live: true);
        using var password = Secret();
        var result = new AdminWorkflow(_backend, audit).Onboard(Plan, password);
        Assert.IsTrue(result.Success);
        var entries = new FileAdminAudit(path, live: true).History();
        Assert.AreEqual(result.Steps.Count * 2, entries.Count);
        Assert.AreEqual("Success", entries[0].Outcome);
        Assert.IsFalse(File.ReadAllText(path).Contains(FixtureMarker));
        Directory.CreateDirectory(Path.Combine(_folder, ".git"));
        var other = Path.Combine(_folder, "work", "audit.jsonl");
        Assert.ThrowsExactly<DirectoryException>(() => new FileAdminAudit(other, live: true).Append(entries[0]));
        Assert.IsFalse(File.Exists(other));
    }

    [TestMethod]
    public void AdministrationRefusesOtherControllers()
    {
        using var session = new LiveSession(new FakeConnection(_backend));
        session.Open(new Controller("other.example.test", Protection.Kerberos), new(), false, default);
        Assert.ThrowsExactly<DirectoryException>(() => session.EnableAdministration(new LabPolicy { OperatorId = Guid.NewGuid(), ApprovedOperatorGroups = ["S-1-5-21-1-2-3-1400"] }));
    }

    private sealed class FakeConnection(IAdminBackend backend) : ILiveReader, IAdministrationSource, ILiveConnector
    {
        public string NamingContext => LabPolicy.Base;
        public Func<bool>? Active;
        public ILiveReader Open(Controller controller, NetworkCredential credential) => this;
        public IReadOnlyList<LiveObject> Read(CancellationToken token) => [];
        public IAdminBackend Activate(LabPolicy policy, Func<IReadOnlyList<LiveObject>> snapshot, Func<bool> active) { Active = active; return backend; }
        public void Dispose() { }
    }
    [TestMethod]
    public void LegacyPolicyCannotActivateOrRetainAnEarlierSessionCapability()
    {
        var transport = new FakeConnection(_backend);
        using var session = new LiveSession(transport);
        session.Open(new Controller(LabPolicy.Controller, Protection.Kerberos), new(), false, default);
        var legacy = new LabPolicy { OperatorId = Guid.NewGuid() };
        legacy.Validate();
        Assert.ThrowsExactly<DirectoryException>(() => session.EnableAdministration(legacy));
        Assert.IsNull(transport.Active);
        session.EnableAdministration(new LabPolicy { OperatorId = legacy.OperatorId, ApprovedOperatorGroups = ["S-1-5-21-1-2-3-1400"] });
        var prior = transport.Active!;
        Assert.ThrowsExactly<DirectoryException>(() => session.EnableAdministration(legacy));
        Assert.IsFalse(prior());
        Assert.IsTrue(session.Connected);
    }

    [TestMethod]
    public void AdministrationIsExplicitAndConnectionBound()
    {
        var transport = new FakeConnection(_backend); using var session = new LiveSession(transport);
        var controller = new Controller(LabPolicy.Controller, Protection.Kerberos);
        var policy = new LabPolicy { OperatorId = Guid.NewGuid(), ApprovedOperatorGroups = ["S-1-5-21-1-2-3-1400"] };
        session.Open(controller, new(), false, default); Assert.IsNull(transport.Active);
        session.EnableAdministration(policy); var first = transport.Active!; Assert.IsTrue(first());
        session.DisableAdministration(); Assert.IsFalse(first());
        session.EnableAdministration(policy); var second = transport.Active!; session.Dispose(); Assert.IsFalse(second());
        session.Open(controller, new(), false, default); Assert.IsFalse(second());
    }
}
