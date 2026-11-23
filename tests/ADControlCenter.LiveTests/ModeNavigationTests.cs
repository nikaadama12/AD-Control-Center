using ADControlCenter.App.Presentation;
using ADControlCenter.Core;
using ADControlCenter.Core.Admin;
using ADControlCenter.Core.Live;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Security;

namespace ADControlCenter.LiveTests;

public sealed partial class AdministrationTests
{
    private static readonly string[] Destinations = [
        "Dashboard", "Users", "Computers", "Groups", "Organizational Units", "Bulk Operations", "Reports",
        "Audit Logs", "Connection Manager", "Onboarding", "Account Administration", "Administration History", "Settings"
    ];
    private static readonly string[] DemoWrites = [
        "New", "Edit", "Enable", "Disable", "Unlock", "ResetPassword", "Import", "Apply", "Clear", "Reset", "SaveSettings",
        "Delete", "AddMember", "RemoveMember"
    ];
    private sealed class ScopedBackend : IAdminBackend
    {
        public Guid AccountId { get; } = Guid.NewGuid();
        public Guid GroupId { get; } = Guid.NewGuid();
        public bool IdentityVerified { get; set; } = true;
        public string Actor => "fixture operator";
        public int Submissions { get; private set; }
        public AdminCatalog Catalog() => new(LabPolicy.Domain, [LabPolicy.Root],
            [new AdminPerson(AccountId, Employee.Empty with { Username = "fixture", DisplayName = "Live fixture" },
                LabPolicy.Root, false, []) { WritableAttributes = new HashSet<string> { "title" } }],
            [new(GroupId, "Fixture staff", "Global security", [])], new Dictionary<string, Guid[]>(), []);
        public void RefreshAccess()
        {
            if (!IdentityVerified) throw new DirectoryException("Operator membership changed.");
        }
        public PreparedChange Prepare(AdminCommand command, SecureString? secret) =>
            new("fixture", "before", "after", () => { Submissions++; return AccountId; });
    }
    private static LabPolicy FixturePolicy(ScopedBackend? backend = null) => new()
    {
        OperatorId = Guid.NewGuid(), ApprovedOperatorGroups = ["S-1-5-21-1-2-3-1400"],
        ApprovedUsers = backend == null ? [] : [backend.AccountId], ApprovedGroups = backend == null ? [] : [backend.GroupId]
    };
    private static async Task ConnectFixture(ShellViewModel shell)
    {
        shell.Navigate.Execute("Connection Manager");
        var form = (ConnectionPage)shell.Page;
        form.UserPrincipalName = "fixture@ad.lab.test";
        using var secret = Secret();
        await shell.ConnectAsync(form, secret, false);
        Assert.AreEqual("LIVE READ-ONLY MODE", shell.ModeLabel);
    }
    private static void VisitSidebar(ShellViewModel shell)
    {
        CollectionAssert.AreEqual(Destinations, shell.Navigation.Select(n => n.Name).ToArray());
        foreach (var item in shell.Navigation)
        {
            Assert.IsTrue(shell.Navigate.CanExecute(item), item.Name);
            shell.Navigate.Execute(item);
            Assert.AreEqual(item, shell.Current);
            Assert.AreEqual(item.Name + " ready", shell.Status);
            if (item.Name == "Bulk Operations" && shell.IsLive) Assert.IsInstanceOfType<SectionNotice>(shell.Page);
            else Assert.IsNotInstanceOfType<SectionNotice>(shell.Page, item.Name);
        }
    }

    [STATestMethod]
    public async Task EveryDestinationOpensInAllThreeModesAndReconnectStartsReadOnly()
    {
        var backend = new ScopedBackend();
        var policy = FixturePolicy(backend);
        var shell = new ShellViewModel(_demo, _demo, new Interaction(), new FakeConnection(backend),
            policyLoader: () => policy, liveAudit: _audit);
        VisitSidebar(shell);
        shell.Navigate.Execute("Organizational Units");
        Assert.AreEqual(6, ((LiveRecordsPage)shell.Page).Result.Total);
        await ConnectFixture(shell);
        VisitSidebar(shell);
        shell.Navigate.Execute("Onboarding");
        Assert.AreEqual(0, ((LiveRecordsPage)shell.Page).Result.Total);
        Assert.IsTrue(shell.ShowAuthorization);
        await shell.EnableAdministrationAsync();
        Assert.AreEqual("LIVE ADMINISTRATION MODE", shell.ModeLabel);
        Assert.IsFalse(shell.ShowAuthorization);
        CollectionAssert.AreEqual(new[] { LabPolicy.Root }, ((OnboardingPage)shell.Page).Locations);
        VisitSidebar(shell);
        shell.Navigate.Execute("Account Administration");
        var account = (AccountAdminPage)shell.Page;
        account.User = account.Users.Single();
        Assert.AreEqual(backend.AccountId, account.User.Id);
        Assert.AreEqual(backend.GroupId, account.Picks.Single().Group.Id);
        Assert.IsTrue(account.Form.CanEditTitle);
        Assert.IsFalse(account.Form.CanEditFirstName);
        Assert.IsFalse(account.CanSetEnabled);
        shell.Run.Execute("Disconnect");
        Assert.AreEqual(128, ((DashboardPage)shell.Page).Total);
        await ConnectFixture(shell);
        Assert.AreEqual(0, ((DashboardPage)shell.Page).Total);
        Assert.AreEqual(0, backend.Submissions);
        shell.CloseSession();
    }

    [STATestMethod]
    public async Task ReadOnlyPagesRejectDirectWritesAndKeepDemoDataAndPreferencesIsolated()
    {
        var ui = new Interaction { Approve = false };
        var backend = new ScopedBackend();
        var shell = new ShellViewModel(_demo, _demo, ui, new FakeConnection(backend), liveAudit: _audit);
        shell.Navigate.Execute("Account Administration");
        var stale = (AccountAdminPage)shell.Page;
        stale.User = stale.Users.First();
        stale.Enabled = !stale.User.Enabled;
        await ConnectFixture(shell);
        var original = _demo.Read();
        foreach (var item in shell.Navigation)
        {
            shell.Navigate.Execute(item);
            foreach (var command in DemoWrites)
            {
                Assert.IsFalse(shell.Run.CanExecute(command), item.Name + "/" + command);
                shell.Run.Execute(command);
            }
        }
        Assert.IsInstanceOfType<LiveAppearancePage>(shell.Page);
        ((LiveAppearancePage)shell.Page).Theme = "Light";
        shell.Run.Execute("ApplyAppearance");
        shell.Navigate.Execute("Administration History");
        Assert.AreEqual(0, ((AdminHistoryPage)shell.Page).Entries.Count);
        Assert.IsTrue(shell.Run.CanExecute("EnableAdministration"));
        await shell.EnableAdministrationAsync();
        Assert.IsFalse(shell.IsAdministration);
        ui.Approve = true;
        await stale.SaveAsync();
        StringAssert.Contains(stale.Message, "no longer active");
        Assert.AreEqual(original.Revision, _demo.Read().Revision);
        Assert.AreEqual(original.Settings, _demo.Settings);
        shell.Run.Execute("Disconnect");
        await stale.SaveAsync();
        StringAssert.Contains(stale.Message, "no longer active");
        Assert.AreEqual(original.Revision, _demo.Read().Revision);
        Assert.AreEqual(0, backend.Submissions);
        shell.CloseSession();
    }

    [STATestMethod]
    public async Task ChangedPolicyReplacesOpenEditorAndRequiresFreshConfirmation()
    {
        var policy = FixturePolicy();
        var ui = new Interaction();
        var shell = new ShellViewModel(_demo, _demo, ui, new FakeConnection(new ScopedBackend()),
            policyLoader: () => policy, liveAudit: _audit);
        await ConnectFixture(shell);
        shell.Navigate.Execute("Onboarding");
        await shell.EnableAdministrationAsync();
        Assert.IsInstanceOfType<OnboardingPage>(shell.Page);
        policy = FixturePolicy();
        shell.CheckAdministrationPolicy();
        Assert.AreEqual("LIVE READ-ONLY MODE", shell.ModeLabel);
        Assert.AreEqual(ObjectKind.OrganizationalUnits, ((LiveRecordsPage)shell.Page).Kind);
        Assert.IsTrue(shell.ShowAuthorization);
        ui.Approve = false;
        await shell.EnableAdministrationAsync();
        Assert.IsFalse(shell.IsAdministration);
        shell.CloseSession();
    }

    [STATestMethod]
    public async Task DeniedOperatorStaysOnReadOnlyManagementPage()
    {
        var denied = new ScopedBackend { IdentityVerified = false };
        var policy = FixturePolicy(denied);
        var shell = new ShellViewModel(_demo, _demo, new Interaction(), new FakeConnection(denied),
            policyLoader: () => policy, liveAudit: _audit);
        await ConnectFixture(shell);
        shell.Navigate.Execute("Account Administration");
        await shell.EnableAdministrationAsync();
        Assert.AreEqual("LIVE READ-ONLY MODE", shell.ModeLabel);
        Assert.AreEqual(ObjectKind.Users, ((LiveRecordsPage)shell.Page).Kind);
        StringAssert.Contains(shell.Status, "membership changed");
        Assert.AreEqual(0, denied.Submissions);
        shell.CloseSession();
    }

    [TestMethod]
    [DataRow("policy")]
    [DataRow("permissions")]
    [DataRow("session")]
    [DataRow("unreadable")]
    public void PreparedWriteCannotBeSubmittedAfterApprovalIsLost(string reason)
    {
        var backend = new ScopedBackend();
        using var session = new LiveSession(new FakeConnection(backend));
        session.Open(new(LabPolicy.Controller, Protection.Kerberos), new(), false, default);
        var policy = FixturePolicy();
        bool readable = true;
        var grant = session.EnableAdministration(() => readable ? policy : throw new IOException());
        var command = new AdminCommand(AdminAction.SetEnabled, backend.AccountId);
        var reviewed = grant.Prepare(command, null);
        switch (reason)
        {
            case "policy": policy = FixturePolicy(); break;
            case "unreadable": readable = false; break;
            case "permissions":
                backend.IdentityVerified = false;
                Assert.ThrowsExactly<DirectoryException>(() => session.Refresh(default));
                break;
            case "session": session.DisableAdministration(); break;
        }
        Assert.ThrowsExactly<DirectoryException>(() => reviewed.Commit());
        Assert.ThrowsExactly<DirectoryException>(() => grant.Prepare(command, null));
        Assert.IsFalse(session.AdministrationCurrent);
        Assert.AreEqual(0, backend.Submissions);
    }

    [TestMethod]
    public void WorkflowRejectsUnadvertisedFieldStateAndMembershipBeforePreparingAnyWrite()
    {
        var backend = new ScopedBackend();
        var workflow = new AdminWorkflow(backend, _audit);
        var user = workflow.Catalog().Users.Single();
        Assert.ThrowsExactly<DirectoryException>(() => workflow.Update(user, user.Profile with { FirstName = "Fixture" }, false, []));
        Assert.ThrowsExactly<DirectoryException>(() => workflow.Update(user, user.Profile, true, []));
        Assert.ThrowsExactly<DirectoryException>(() => workflow.Update(user, user.Profile, false, [Guid.NewGuid()]));
        var previousSelection = user with { Groups = new[] { Guid.NewGuid() } };
        Assert.ThrowsExactly<DirectoryException>(() => workflow.Update(previousSelection, user.Profile, false, []));
        Assert.AreEqual(0, backend.Submissions);
        Assert.IsTrue(workflow.Update(user, user.Profile with { Title = "Analyst" }, false, []).Success);
        Assert.AreEqual(1, backend.Submissions);
    }
}
