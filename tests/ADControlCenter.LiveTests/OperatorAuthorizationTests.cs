using ADControlCenter.Core;
using ADControlCenter.Core.Admin;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADControlCenter.LiveTests;

[TestClass]
public sealed class OperatorAuthorizationTests
{
    private const string Domain = "S-1-5-21-1-2-3";
    private const string Role = Domain + "-1400";
    private const string DomainUsers = Domain + "-513";

    private static LabPolicy Policy(params string[] roles) =>
        new() { OperatorId = Guid.NewGuid(), ApprovedOperatorGroups = roles };

    [TestMethod]
    public void VerifiedRoleCanAuthorizeOnlyTheConfiguredPrincipal()
    {
        var policy = Policy(Role);
        policy.Validate();
        LabPolicy.RequireBoundActor("u:" + Domain + "-1500", Domain + "-1500", @"LAB\fixture");
        policy.RequireOperatorMembership([DomainUsers, Role], Domain, 513, [Role]);
        Assert.ThrowsExactly<DirectoryException>(() =>
            LabPolicy.RequireBoundActor(@"u:FOREIGN\fixture", Domain + "-1500", @"LAB\fixture"));
        Assert.ThrowsExactly<DirectoryException>(() => policy.RequireGroup(Guid.NewGuid()));
    }

    [TestMethod]
    public void RoleCannotAlsoBeAnApprovedMembershipTarget()
    {
        var id = Guid.NewGuid();
        var policy = new LabPolicy { OperatorId = Guid.NewGuid(), ApprovedGroups = [id], ApprovedOperatorGroups = [Role] };
        Assert.ThrowsExactly<DirectoryException>(() => policy.RequireSeparateGroup(id, Role));
        policy.RequireSeparateGroup(id, Domain + "-1401");
    }

    [TestMethod]
    public void RemovedMemberIsRejectedEvenWithPreviouslyValidToken()
    {
        var policy = Policy(Role);
        string[] token = [DomainUsers, Role];
        policy.RequireOperatorMembership(token, Domain, 513, [Role]);
        Assert.ThrowsExactly<DirectoryException>(() => policy.RequireOperatorMembership(token, Domain, 513, []));
        Assert.ThrowsExactly<DirectoryException>(() => policy.RequireOperatorMembership([DomainUsers], Domain, 513, [Role]));
    }

    [TestMethod]
    [DataRow("S-1-5-32-544")]
    [DataRow(Domain + "-512")]
    [DataRow(Domain + "-0001400")]
    [DataRow(Domain + "-4294967296")]
    [DataRow(Domain + "-1400 ")]
    [DataRow("ADControl-Lab-Operators")]
    [DataRow("")]
    public void InvalidRoleEntryIsRejected(string role) =>
        Assert.ThrowsExactly<DirectoryException>(Policy(role).Validate);

    [TestMethod]
    public void OperatorListRequiresDistinctEntriesAndAnExplicitMatch()
    {
        Assert.ThrowsExactly<DirectoryException>(Policy(Role, Role).Validate);
        Assert.ThrowsExactly<DirectoryException>(() => Policy().RequireOperatorMembership([DomainUsers], Domain, 513, []));
        Assert.ThrowsExactly<DirectoryException>(() => Policy(Role).RequireOperatorMembership([], Domain, 513, []));
        Assert.ThrowsExactly<DirectoryException>(() => Policy(Role).RequireOperatorMembership([Role], Domain, 513, [Role]));
    }

    [TestMethod]
    public void ForeignOperatorOrGroupCannotBeAuthorized()
    {
        const string foreign = "S-1-5-21-4-5-6-1400";
        Assert.ThrowsExactly<DirectoryException>(() => LabPolicy.RequireDomainPrincipal(foreign, Domain));
        Assert.ThrowsExactly<DirectoryException>(() => Policy(foreign).RequireOperatorMembership([DomainUsers, foreign], Domain, 513, [foreign]));
    }

    [TestMethod]
    [DataRow(Domain + "-512")]
    [DataRow("S-1-5-32-544")]
    [DataRow("S-1-5-21-4-5-6-1400")]
    [DataRow(Domain + "-1501")]
    public void ExtraTokenSidStillDeniesAdministration(string extra) =>
        Assert.ThrowsExactly<DirectoryException>(() => Policy(Role).RequireOperatorMembership([DomainUsers, Role, extra], Domain, 513, [Role]));
}
