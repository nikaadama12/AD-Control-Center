using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using ADControlCenter.Core.Admin;

namespace ADControlCenter.Core.Live;

internal interface IAdministrationSource
{
    IAdminBackend Activate(LabPolicy policy, Func<IReadOnlyList<LiveObject>> snapshot, Func<bool> active);
}

[SupportedOSPlatform("windows")]
internal sealed class LdapAdministration : IAdminBackend
{
    private readonly LdapConnection _ldap;
    private readonly LabPolicy _policy;
    private readonly Func<IReadOnlyList<LiveObject>> _snapshot;
    private readonly Func<bool> _active;
    private readonly HashSet<Guid> _created = [];
    private readonly HashSet<Guid> _unfinished = [];
    private readonly HashSet<Guid> _passwordAssigned = [];
    private static readonly string[] Attributes = ["objectGUID","objectClass","objectSid","sIDHistory","sAMAccountName","msDS-PrincipalName",
        "adminCount","isCriticalSystemObject","primaryGroupID","userAccountControl","groupType","memberOf","givenName","sn","displayName",
        "userPrincipalName","department","title","mail","description","pwdLastSet","allowedAttributesEffective"];
    private AdminCatalog? _catalog;
    public string Actor { get; }

    public LdapAdministration(LdapConnection ldap, LabPolicy policy, Func<IReadOnlyList<LiveObject>> snapshot, Func<bool> active)
    {
        (_ldap, _policy, _snapshot, _active) = (ldap, policy, snapshot, active);
        Actor = CheckActor();
        Ou(LabPolicy.Root);
    }

    private DirectoryResponse Send(DirectoryRequest request)
    {
        if (!_active()) throw new DirectoryException("Administration has ended for this connection.");
        try { return _ldap.SendRequest(request, LdapConnector.Timeout); }
        catch (DirectoryOperationException e) when (e.Response != null)
        {
            throw ControllerRejection(e.Response.ResultCode, e.Response.ErrorMessage);
        }
        // Transport failures propagate to the workflow as unconfirmed once a write has been submitted.
    }
    internal static DirectoryException ControllerRejection(ResultCode result, string? diagnostic)
    {
        // AD prefixes its free-form diagnostic with a Win32 code. Only the number is safe to journal.
        uint? status = null;
        if (diagnostic is { Length: >= 8 } && (diagnostic.Length == 8 || diagnostic[8] == ':') &&
            uint.TryParse(diagnostic.AsSpan(0, 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint code))
            status = code;

        string detail = status switch
        {
            0x52D => "The controller rejected the password under the effective password policy.",
            0x216C => "The controller rejected the quotation marks in the unicodePwd encoding.",
            0x202D => "The controller requires confidentiality for this operation.",
            0x2098 => "The controller denied the delegated operation.",
            _ => result switch
            {
                ResultCode.InsufficientAccessRights => "The controller denied the delegated operation.",
                ResultCode.ConfidentialityRequired or ResultCode.StrongAuthRequired => "The controller requires stronger connection protection.",
                ResultCode.ConstraintViolation => "The controller reported a directory constraint violation.",
                ResultCode.UnwillingToPerform => "The controller declined this operation.",
                ResultCode.EntryAlreadyExists or ResultCode.AttributeOrValueExists => "The object or membership already exists.",
                ResultCode.NoSuchObject or ResultCode.NoSuchAttribute => "The target or reviewed value no longer exists.",
                _ => "The controller rejected the request."
            }
        };
        string codes = status.HasValue ? $"LDAP {(int)result}, AD 0x{status:X8}" : $"LDAP {(int)result}";
        return new DirectoryException($"{detail} ({codes})");
    }
    private SearchResponse Search(string dn, string filter, SearchScope scope, params string[] attrs) =>
        (SearchResponse)Send(new SearchRequest(dn, filter, scope, attrs));
    private SearchResultEntry One(string dn, string filter, params string[] attrs)
    {
        var rows = Search(dn, filter, SearchScope.Base, attrs).Entries;
        return rows.Count == 1 ? rows[0] : throw new DirectoryException("Required directory evidence is missing or the target changed.");
    }
    private static string Value(SearchResultEntry e, string a) => LdapReader.Values(e, a).FirstOrDefault() ?? "";
    private static Guid Id(SearchResultEntry e) => e.Attributes["objectGUID"]?.GetValues(typeof(byte[])).FirstOrDefault() is byte[] b && b.Length == 16 ? new Guid(b) : throw new DirectoryException("Object identity is unavailable.");
    private static SecurityIdentifier Sid(SearchResultEntry e) => e.Attributes["objectSid"]?.GetValues(typeof(byte[])).FirstOrDefault() is byte[] b ? new(b, 0) : throw new DirectoryException("Principal SID unavailable.");
    private static int Int(SearchResultEntry e, string a) => int.TryParse(Value(e, a), CultureInfo.InvariantCulture, out int value) ? value : throw new DirectoryException("Required security attributes are unavailable.");
    private static string Escape(string s) => string.Concat(Encoding.UTF8.GetBytes(s).Select(b => $"\\{b:x2}"));
    private SearchResultEntry ById(Guid id, string filter)
    {
        string octets = string.Concat(id.ToByteArray().Select(b => $"\\{b:x2}"));
        var rows = Search(LabPolicy.Base, $"(&{filter}(objectGUID={octets}))", SearchScope.Subtree, Attributes).Entries;
        if (rows.Count != 1 || Id(rows[0]) != id) throw new DirectoryException("Approved object identity is missing or changed.");
        return rows[0];
    }
    private static bool HasAttribute(SearchResultEntry e, string a) => e.Attributes.AttributeNames.Cast<string>().Any(n => n.Equals(a, StringComparison.OrdinalIgnoreCase) || n.StartsWith(a + ";range=", StringComparison.OrdinalIgnoreCase));
    private void Ou(string dn)
    {
        _policy.RequireOu(dn);
        One(dn, "(&(objectClass=organizationalUnit)(!(isCriticalSystemObject=TRUE)))", "1.1");
    }
    private static void Ordinary(SearchResultEntry e)
    {
        LabPolicy.RequireOrdinary(Value(e, "sAMAccountName"), Sid(e).Value, Value(e, "adminCount") == "1",
            Value(e, "isCriticalSystemObject").Equals("TRUE", StringComparison.OrdinalIgnoreCase), HasAttribute(e, "sIDHistory"));
    }
    private SearchResultEntry Group(Guid id)
    {
        _policy.RequireGroup(id);
        var e = ById(id, "(objectClass=group)");
        Ordinary(e);
        _policy.RequireSeparateGroup(id, Sid(e).Value);
        Ou(LiveObject.ParentOf(e.DistinguishedName));
        int type = Int(e, "groupType");
        LabPolicy.RequireGroupType(type, HasAttribute(e, "memberOf"));
        return e;
    }
    private string[] ReadEffectiveGroups(SearchResultEntry e)
    {
        var evidence = One(e.DistinguishedName, "(objectClass=user)", "tokenGroups", "objectGUID", "objectSid");
        if (Id(evidence) != Id(e) || !Sid(evidence).Equals(Sid(e))) throw new DirectoryException("Account identity changed during membership verification.");
        if (evidence.Attributes.AttributeNames.Cast<string>().Any(n => n.StartsWith("tokenGroups;", StringComparison.OrdinalIgnoreCase)))
            throw new DirectoryException("Incomplete effective membership evidence.");
        var tokens = evidence.Attributes["tokenGroups"];
        if (tokens == null || tokens.Count == 0) throw new DirectoryException("Effective group membership could not be verified.");
        return tokens.GetValues(typeof(byte[])).Cast<byte[]>().Select(b => new SecurityIdentifier(b, 0).Value).ToArray();
    }
    private string CheckActor()
    {
        var e = ById(_policy.OperatorId, "(&(objectCategory=person)(objectClass=user))");
        Ordinary(e);
        var id = Encoding.UTF8.GetString(((ExtendedResponse)Send(new ExtendedRequest("1.3.6.1.4.1.4203.1.11.3"))).ResponseValue);
        LabPolicy.RequireBoundActor(id, Sid(e).Value, Value(e, "msDS-PrincipalName"));
        if ((Int(e, "userAccountControl") & 2) != 0) throw new DirectoryException("The delegated operator account is disabled.");
        string domainSid = Sid(One(LabPolicy.Base, "(objectClass=domainDNS)", "objectSid")).Value;
        LabPolicy.RequireDomainPrincipal(Sid(e).Value, domainSid);

        var direct = new List<string>();
        foreach (string sid in _policy.ApprovedOperatorGroups)
        {
            LabPolicy.RequireDomainPrincipal(sid, domainSid);
            var identifier = new SecurityIdentifier(sid);
            var bytes = new byte[identifier.BinaryLength];
            identifier.GetBinaryForm(bytes, 0);
            string filter = $"(&(objectClass=group)(objectSid={string.Concat(bytes.Select(b => $"\\{b:x2}"))}))";
            var rows = Search(LabPolicy.Base, filter, SearchScope.Subtree, Attributes).Entries;
            if (rows.Count != 1 || Sid(rows[0]).Value != sid) throw new DirectoryException("Operator group resolution was missing or ambiguous.");
            var group = rows[0];
            Ordinary(group);
            LabPolicy.RequireGroupType(Int(group, "groupType"), HasAttribute(group, "memberOf"));
            _policy.RequireSeparateGroup(Id(group), sid);

            // Re-read the member link on DC01. A cached logon token cannot keep a removed membership alive.
            if (Search(group.DistinguishedName, $"(&{filter}(member={Escape(e.DistinguishedName)}))", SearchScope.Base, "1.1").Entries.Count == 1)
                direct.Add(sid);
        }
        _policy.RequireOperatorMembership(ReadEffectiveGroups(e), domainSid, Int(e, "primaryGroupID"), direct);
        return Value(e, "sAMAccountName");
    }
    private SearchResultEntry User(Guid id)
    {
        _policy.RequireUser(id, _created);
        var e = ById(id, "(&(objectCategory=person)(objectClass=user))");
        Ordinary(e);
        Ou(LiveObject.ParentOf(e.DistinguishedName));
        var flags = Int(e, "userAccountControl");
        if ((flags & 512) == 0 || (flags & (0x80000 | 0x1000000 | 0x2000 | 0x1000 | 0x800 | 0x4000000)) != 0)
            throw new DirectoryException("Service, trust and delegation-enabled accounts are excluded.");
        var domainSid = Sid(e).AccountDomainSid?.Value ?? throw new DirectoryException("Domain SID unavailable.");
        LabPolicy.RequireMemberships(ReadEffectiveGroups(e), domainSid, Int(e, "primaryGroupID"), _policy.ApprovedGroups.Select(id => Sid(Group(id)).Value));
        return e;
    }
    public AdminCatalog Catalog()
    {
        if (!_active() || _catalog == null) throw new DirectoryException("Refresh administration access before continuing.");
        return _catalog;
    }

    public void RefreshAccess()
    {
        _catalog = null;
        CheckActor();
        var objects = _snapshot();
        List<string> destinations = [];
        List<AdminGroup> groups = [];
        List<AdminPerson> users = [];
        foreach (var item in objects)
        {
            // A denied or unverifiable target is excluded. Authentication and policy failures
            // are checked again after the scan, before publishing the new catalog.
            try
            {
                if (item.Kind == ObjectKind.OrganizationalUnits && _policy.AllowsOu(item.DistinguishedName))
                {
                    var ou = One(item.DistinguishedName, "(&(objectClass=organizationalUnit)(!(isCriticalSystemObject=TRUE)))",
                        "allowedChildClassesEffective", "objectGUID");
                    if (Id(ou) == item.Id && LdapReader.Values(ou, "allowedChildClassesEffective").Contains("user", StringComparer.OrdinalIgnoreCase))
                        destinations.Add(item.DistinguishedName);
                }
                if (item.Kind == ObjectKind.Groups && _policy.ApprovedGroups.Contains(item.Id))
                {
                    var group = Group(item.Id);
                    if (LdapReader.Values(group, "allowedAttributesEffective").Contains("member", StringComparer.OrdinalIgnoreCase))
                        groups.Add(new(item.Id, item.Name, item.GroupScope + " security", item.Members));
                }
            }
            catch (DirectoryException) { continue; }
        }
        foreach (var item in objects.Where(o => o.Kind == ObjectKind.Users && (_created.Contains(o.Id) || _policy.ApprovedUsers.Contains(o.Id))))
        {
            try
            {
                var user = User(item.Id);
                users.Add(new(item.Id, Profile(user), LiveObject.ParentOf(user.DistinguishedName), (Int(user, "userAccountControl") & 2) == 0,
                    groups.Where(g => g.Members.Contains(user.DistinguishedName, StringComparer.OrdinalIgnoreCase)).Select(g => g.Id).ToArray())
                {
                    WritableAttributes = LdapReader.Values(user, "allowedAttributesEffective").ToHashSet(StringComparer.OrdinalIgnoreCase)
                });
            }
            catch (DirectoryException) { continue; }
        }
        CheckActor();
        _catalog = new(LabPolicy.Domain, destinations.Order().ToArray(), users.ToArray(), groups.ToArray(), _policy.Templates,
            objects.SelectMany(o => new[] { o.Account, o.EmployeeInfo?.Upn ?? "" }).ToArray());
    }

    private static Employee Profile(SearchResultEntry e) => new(Value(e, "givenName"), Value(e, "sn"), Value(e, "displayName"), Value(e, "sAMAccountName"),
        Value(e, "userPrincipalName"), Value(e, "department"), Value(e, "title"), Value(e, "mail"), Value(e, "description"));
    internal static AddRequest CreateDisabledRequest(string dn, Employee profile)
    {
        var request = new AddRequest(dn, "user");
        request.Attributes.Add(new DirectoryAttribute("cn", profile.Username));
        foreach (var (name, value) in profile.EditableAttributes().Where(pair => pair.Value.Length > 0))
            request.Attributes.Add(new DirectoryAttribute(name, value));
        request.Attributes.Add(new DirectoryAttribute("sAMAccountName", profile.Username));
        request.Attributes.Add(new DirectoryAttribute("userPrincipalName", profile.Upn));
        request.Attributes.Add(new DirectoryAttribute("userAccountControl", "514"));
        return request;
    }

    private static DirectoryAttributeModification Mod(string name, DirectoryAttributeOperation op, string value)
    {
        var mod = new DirectoryAttributeModification { Name = name, Operation = op };
        if (value.Length > 0) mod.Add(value);
        return mod;
    }
    public PreparedChange Prepare(AdminCommand command, SecureString? password)
    {
        CheckActor();
        var reviewed = Plan(command, password);
        // Intent is journaled between preparation and submission. Resolve identity and scope again.
        Guid SubmitReviewedChange()
        {
            CheckActor();
            var fresh = Plan(command, password);
            if ((fresh.Target, fresh.Before, fresh.After) != (reviewed.Target, reviewed.Before, reviewed.After)) throw new DirectoryException("The directory changed. Review the operation again.");
            return fresh.Commit();
        }
        return reviewed with { Commit = SubmitReviewedChange };
    }
    private PreparedChange Plan(AdminCommand c, SecureString? secret)
    {
        if (c.Action == AdminAction.CreateDisabled)
        {
            var p = c.Profile!;
            AdminValidation.EmployeeFields(p, LabPolicy.Domain);
            LabPolicy.RequireOrdinaryName(p.Username);
            Ou(c.Location);
            foreach (var groupId in c.RequestedGroups ?? []) Group(groupId);
            if (Search(LabPolicy.Base, $"(|(sAMAccountName={Escape(p.Username)})(userPrincipalName={Escape(p.Upn)}))", SearchScope.Subtree, "1.1").Entries.Count != 0)
                throw new DirectoryException("Duplicate domain username or UPN.");
            string dn = $"CN={p.Username},{c.Location}";
            return new(dn, "Absent", "Disabled; " + p.Attributes, () =>
            {
                Send(CreateDisabledRequest(dn, p));
                try
                {
                    var added = One(dn, $"(&(objectClass=user)(sAMAccountName={Escape(p.Username)})(userPrincipalName={Escape(p.Upn)}))", "objectGUID");
                    Guid id = Id(added);
                    _created.Add(id); _unfinished.Add(id);
                    return id;
                }
                catch (Exception ex) { throw new IOException("Creation acknowledged; identity readback incomplete.", ex); }
            });
        }
        var user = User(c.User);
        string target = user.DistinguishedName;
        var request = new ModifyRequest { DistinguishedName = target };
        string before, after;
        int flags = Int(user, "userAccountControl");
        switch (c.Action)
        {
            case AdminAction.TemporaryPassword:
                if (!_unfinished.Contains(c.User) || _passwordAssigned.Contains(c.User) || (flags & 2) == 0) throw new DirectoryException("Password setup requires a newly created disabled account.");
                AdminValidation.Password(secret);
                return new(target, "Initial setup", "Temporary password assigned", () =>
                {
                    SendPasswordReset(target, secret!, request => Send(request));
                    _passwordAssigned.Add(c.User);
                    return c.User;
                });
            case AdminAction.AccountSettings:
            case AdminAction.SetEnabled:
                if (c.Action == AdminAction.AccountSettings && (!_unfinished.Contains(c.User) || !_passwordAssigned.Contains(c.User))) throw new DirectoryException("Onboarding context expired.");
                if (c.Action == AdminAction.SetEnabled && c.Enabled && _unfinished.Contains(c.User))
                    throw new DirectoryException("Reconcile incomplete onboarding before enabling this account.");
                if (c.Expected != null && c.Expected.Enabled != ((flags & 2) == 0)) throw new DirectoryException("Account state changed since review.");
                before = $"Enabled={(flags & 2) == 0}"; after = $"Enabled={c.Enabled}";
                if (c.Action == AdminAction.AccountSettings)
                {
                    before += $"; change at logon={Value(user, "pwdLastSet") == "0"}"; after += $"; change at logon={c.ChangeAtLogon}";
                    request.Modifications.Add(Mod("pwdLastSet", DirectoryAttributeOperation.Replace, c.ChangeAtLogon ? "0" : "-1"));
                }
                request.Modifications.Add(Mod("userAccountControl", DirectoryAttributeOperation.Replace, (c.Enabled ? flags & ~2 : flags | 2).ToString(CultureInfo.InvariantCulture)));
                break;
            case AdminAction.EditAttributes:
                var old = Profile(user); var profile = c.Profile!;
                if (old != c.Expected?.Profile || profile.Username != old.Username || profile.Upn != old.Upn) throw new DirectoryException("Account attributes or identifiers changed since review.");
                AdminValidation.EditedFields(old, profile);
                var current = old.EditableAttributes();
                foreach (var (k, v) in profile.EditableAttributes().Where(kv => current[kv.Key] != kv.Value)) request.Modifications.Add(Mod(k, DirectoryAttributeOperation.Replace, v));
                before = old.Attributes; after = profile.Attributes; break;
            case AdminAction.AddMember:
            case AdminAction.RemoveMember:
                var group = Group(c.Group); bool add = c.Action == AdminAction.AddMember;
                bool member = Search(group.DistinguishedName, $"(member={Escape(target)})", SearchScope.Base, "1.1").Entries.Count == 1;
                if (member == add) throw new DirectoryException("Membership changed since review.");
                request.DistinguishedName = group.DistinguishedName;
                request.Modifications.Add(Mod("member", add ? DirectoryAttributeOperation.Add : DirectoryAttributeOperation.Delete, target));
                target += " / " + group.DistinguishedName; before = member ? "Member" : "Absent"; after = add ? "Member" : "Absent"; break;
            default: throw new DirectoryException("Unsupported administrative action.");
        }
        return new(target, before, after, () =>
        {
            Send(request);
            if (c.Action == AdminAction.AccountSettings)
            {
                _unfinished.Remove(c.User);
                _passwordAssigned.Remove(c.User);
            }
            return c.User;
        });
    }
    internal static void SendPasswordReset(string dn, SecureString password, Action<ModifyRequest> send)
    {
        byte[] quoted = new byte[checked((password.Length + 2) * 2)];
        IntPtr ptr = Marshal.SecureStringToBSTR(password);
        try
        {
            quoted[0] = 34; quoted[^2] = 34;
            Marshal.Copy(ptr, quoted, 2, password.Length * 2);
            var mod = new DirectoryAttributeModification { Name = "unicodePwd", Operation = DirectoryAttributeOperation.Replace };
            mod.Add(quoted);
            send(new ModifyRequest(dn, mod));
        }
        finally { CryptographicOperations.ZeroMemory(quoted); Marshal.ZeroFreeBSTR(ptr); }
    }
}
