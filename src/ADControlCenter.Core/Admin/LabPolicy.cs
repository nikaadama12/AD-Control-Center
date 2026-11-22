using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using ADControlCenter.Core.Live;

namespace ADControlCenter.Core.Admin;

public sealed class LabPolicy
{
    public const string Domain = "ad.lab.test";
    public const string Controller = "DC01.ad.lab.test";
    public const string Base = "DC=ad,DC=lab,DC=test";
    public const string Root = "OU=AppTesting," + Base;
    public Guid OperatorId { get; init; }
    public string[] ChildOus { get; init; } = [];
    public Guid[] ApprovedUsers { get; init; } = [];
    public Guid[] ApprovedGroups { get; init; } = [];
    public Dictionary<string, Guid[]> Templates { get; init; } = new();
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AD Control Center", "administration.json");
    [SupportedOSPlatform("windows")]
    private static void RequireMachineOwned(FileSystemInfo item)
    {
        if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new DirectoryException("The policy folder and file must be ordinary local paths.");
        FileSystemSecurity acl = item is DirectoryInfo directory
            ? directory.GetAccessControl() : ((FileInfo)item).GetAccessControl();
        static bool Trusted(IdentityReference? id) => id?.Value is "S-1-5-18" or "S-1-5-32-544";
        const FileSystemRights changes = FileSystemRights.Write | FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        if (!Trusted(acl.GetOwner(typeof(SecurityIdentifier))) ||
            acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                .Any(rule => rule.AccessControlType == AccessControlType.Allow &&
                    !rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly) &&
                    (rule.FileSystemRights & changes) != 0 && !Trusted(rule.IdentityReference)))
            throw new DirectoryException("Secure the administration policy and its folder for writes by SYSTEM and local Administrators only.");
    }

    [SupportedOSPlatform("windows")]
    public static LabPolicy Load()
    {
        try
        {
            var file = new FileInfo(FilePath);
            RequireMachineOwned(file.Directory!);
            RequireMachineOwned(file);
            var policy = JsonSerializer.Deserialize<LabPolicy>(File.ReadAllText(FilePath),
                new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
            if (policy == null) throw new DirectoryException("Empty administration policy.");
            policy.Validate();
            return policy;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { throw new DirectoryException($"Install a readable administration policy at {FilePath}."); }
    }
    public void Validate()
    {
        if (OperatorId == Guid.Empty || ChildOus == null || ApprovedUsers == null || ApprovedGroups == null || Templates == null ||
            ApprovedUsers.Contains(OperatorId) || ApprovedGroups.Contains(OperatorId) || ApprovedUsers.Intersect(ApprovedGroups).Any() ||
            ApprovedUsers.Concat(ApprovedGroups).Contains(Guid.Empty) ||
            ChildOus.Any(dn => !InsideRoot(dn)) || Templates.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null || p.Value.Except(ApprovedGroups).Any()))
            throw new DirectoryException("Invalid operator, OU, group or department template in the administration policy.");
    }
    private static bool InsideRoot(string? dn)
    {
        if (string.IsNullOrWhiteSpace(dn)) return false;
        while (!dn.Equals(Root, StringComparison.OrdinalIgnoreCase))
        {
            var parent = LiveObject.ParentOf(dn);
            if (parent.Length == 0 || !Regex.IsMatch(dn[..(dn.Length - parent.Length - 1)], @"^OU=[A-Za-z0-9][A-Za-z0-9 _.\-]*\z", RegexOptions.IgnoreCase)) return false;
            dn = parent;
        }
        return true;
    }
    public bool AllowsOu(string dn) => InsideRoot(dn) && (dn.Equals(Root, StringComparison.OrdinalIgnoreCase) || ChildOus.Contains(dn, StringComparer.OrdinalIgnoreCase));
    public void RequireOu(string dn)
    {
        if (!AllowsOu(dn)) throw new DirectoryException("The target is outside the approved AppTesting locations.");
    }

    internal void RequireUser(Guid id, IReadOnlySet<Guid> created)
    {
        if (id == Guid.Empty || id == OperatorId || (!created.Contains(id) && !ApprovedUsers.Contains(id)))
            throw new DirectoryException("User is not approved for administration.");
    }

    internal void RequireGroup(Guid id)
    {
        if (id == Guid.Empty || !ApprovedGroups.Contains(id))
            throw new DirectoryException("Group is not allowlisted.");
    }

    internal static void RequireBoundActor(string authorizationId, string sid, string principal)
    {
        bool Matches(string identity) => identity.Length > 0 &&
            authorizationId.Equals("u:" + identity, StringComparison.OrdinalIgnoreCase);
        if (!Matches(sid) && !Matches(principal))
            throw new DirectoryException("Authenticate with the configured delegated lab operator.");
    }

    internal static void RequireGroupType(int type, bool nested)
    {
        if (type >= 0 || (type & 1) != 0 || nested) throw new DirectoryException("Only ordinary, non-nested security groups are approved.");
    }
    internal static void RequireMemberships(IEnumerable<string> effective, string domainSid, int primaryGroup, IEnumerable<string> approvedGroupSids)
    {
        if (primaryGroup != 513) throw new DirectoryException("An ordinary Domain Users primary group is required.");
        var safe = new HashSet<string>(approvedGroupSids, StringComparer.OrdinalIgnoreCase) { domainSid + "-513", "S-1-5-32-545", "S-1-1-0", "S-1-5-11", "S-1-5-15" };
        var tokens = effective.ToArray();
        if (tokens.Length == 0 || tokens.Any(s => !safe.Contains(s))) throw new DirectoryException("Effective membership is unavailable or includes an unapproved group.");
    }
    internal static void RequireOrdinaryName(string name)
    {
        string[] protectedNames = ["Administrator", "Administrators", "Domain Admins", "Enterprise Admins", "Schema Admins",
            "DnsAdmins", "Group Policy Creator Owners", "Key Admins", "Enterprise Key Admins", "Cert Publishers", "krbtgt"];
        if (protectedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new DirectoryException("Protected principal names cannot be used in lab administration.");
    }
    internal static void RequireOrdinary(string name, string sid, bool protectedObject, bool critical, bool sidHistory)
    {
        RequireOrdinaryName(name);
        var ridText = sid.Split('-').Last();
        if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) || !uint.TryParse(ridText, out var rid) || rid < 1000 ||
            protectedObject || critical || sidHistory)
            throw new DirectoryException("Protected, built-in or privileged principals cannot participate in this operation.");
    }
}
