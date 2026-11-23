using System.Net;
using System.Security;
using System.Text.Json;
using ADControlCenter.Core.Admin;

namespace ADControlCenter.Core.Live;

public enum Protection { Ldaps, Kerberos }
public enum ObjectKind { Users, Computers, Groups, OrganizationalUnits }

public sealed record Controller(string Host, Protection Protection)
{
    public void Validate()
    {
        if (Uri.CheckHostName(Host) != UriHostNameType.Dns || !Host.Contains('.') || IPAddress.TryParse(Host, out _))
            throw new DirectoryException("Enter the controller’s fully qualified DNS hostname.");
        if (!Enum.IsDefined(Protection)) throw new DirectoryException("Choose a supported secure transport.");
    }
}

public sealed record LiveObject(ObjectKind Kind, string Name, string DistinguishedName, string Account,
    string Status, string Platform, string Email, string Description, string Hostname, DateTimeOffset? LastLogon,
    IReadOnlyList<string> Members, IReadOnlyList<string> MemberOf)
{
    public Guid Id { get; init; }
    public Employee? EmployeeInfo { get; init; }
    public string GroupScope { get; init; } = "";
    public bool IsSecurityGroup { get; init; }
    public string Parent => ParentOf(DistinguishedName);
    public static string ParentOf(string dn)
    {
        for (int i = 0; i < dn.Length; i++)
        {
            if (dn[i] == '\\') { i++; continue; }
            if (dn[i] == ',') return dn[(i + 1)..];
        }
        return "";
    }
    public string Details => string.Join(Environment.NewLine, new[]
    {
        $"Distinguished name   {DistinguishedName}", $"Parent   {Parent}", Field("Account", Account), Field("Department",EmployeeInfo?.Department), Field("Job title",EmployeeInfo?.Title), Field("UPN",EmployeeInfo?.Upn),
        Field("Group scope",GroupScope), Kind == ObjectKind.Groups ? $"Category   {(IsSecurityGroup ? "Security" : "Distribution")}" : "",
        Field("State", Status), Field("Operating system", Platform), Field("DNS hostname", Hostname),
        Field("Email", Email), Field("Description", Description), Field("Replicated last logon", LastLogon?.ToLocalTime().ToString("g")),
        Kind == ObjectKind.Groups ? $"Direct members ({Members.Count})\n{string.Join("\n", Members)}" : "",
        Kind != ObjectKind.OrganizationalUnits ? $"Direct memberOf ({MemberOf.Count})\n{string.Join("\n", MemberOf)}" : ""
    }.Where(s => s.Length > 0));
    private static string Field(string label, string? value) => string.IsNullOrEmpty(value) ? "" : $"{label}   {value}";
}

// Inventory consumers receive a read-only interface. Administration requires a separate session capability.
public interface ILiveReader : IDisposable
{
    string NamingContext { get; }
    IReadOnlyList<LiveObject> Read(CancellationToken token);
}

public interface ILiveConnector
{
    ILiveReader Open(Controller controller, NetworkCredential credential);
}

public sealed class LiveSession(ILiveConnector connector) : IDisposable
{
    private ILiveReader? _reader;
    private Guid _administrationLease;
    private Func<bool>? _policyMatches;
    private IAdminBackend? _backend;
    public bool AdministrationCurrent
    {
        get
        {
            if (!Connected || _administrationLease == Guid.Empty) return false;
            try { if (_policyMatches?.Invoke() != false) return true; }
            catch (Exception) { /* Unreadable or insecure machine policy revokes the lease. */ }
            DisableAdministration();
            return false;
        }
    }

    public IAdminBackend EnableAdministration(Func<LabPolicy> loadPolicy)
    {
        DisableAdministration();
        var policy = loadPolicy();
        string approved = JsonSerializer.Serialize(policy);
        return Activate(policy, () => JsonSerializer.Serialize(loadPolicy()) == approved);
    }

    public IAdminBackend EnableAdministration(LabPolicy policy) => Activate(policy, null);

    private IAdminBackend Activate(LabPolicy policy, Func<bool>? matches)
    {
        DisableAdministration();
        policy.Validate();
        if (policy.ApprovedOperatorGroups.Length == 0) throw new DirectoryException("Configure an approved operator group SID before enabling administration.");
        if (!Host.Equals(LabPolicy.Controller,StringComparison.OrdinalIgnoreCase) || _reader is not IAdministrationSource source ||
            !_reader.NamingContext.Equals(LabPolicy.Base,StringComparison.OrdinalIgnoreCase)) throw new DirectoryException("Administration requires the approved lab controller.");
        var lease = _administrationLease = Guid.NewGuid();
        _policyMatches = matches;
        bool Active() => _administrationLease == lease && AdministrationCurrent;
        try
        {
            var backend = source.Activate(policy, () => Objects, Active);
            _backend = new SessionBackend(backend, Active);
            _backend.RefreshAccess();
            return _backend;
        }
        catch { DisableAdministration(); throw; }
    }
    private sealed class SessionBackend(IAdminBackend inner, Func<bool> active) : IAdminBackend
    {
        private void RequireActive()
        {
            if (!active()) throw new DirectoryException("Administration approval expired. Authorize this connection again.");
        }
        public string Actor => inner.Actor;
        public void RefreshAccess() { RequireActive(); inner.RefreshAccess(); }
        public AdminCatalog Catalog() { RequireActive(); return inner.Catalog(); }
        public PreparedChange Prepare(AdminCommand command, SecureString? password)
        {
            RequireActive();
            var change = inner.Prepare(command, password);
            return change with { Commit = () => { RequireActive(); return change.Commit(); } };
        }
    }

    public void DisableAdministration()
    {
        _administrationLease = Guid.Empty;
        _policyMatches = null;
        _backend = null;
    }
    public bool Connected => _reader != null;
    public IReadOnlyList<LiveObject> Objects { get; private set; } = [];
    public string Host { get; private set; } = "";

    // The shell serializes operations and executes network calls on a worker thread.
    public void Open(Controller controller, NetworkCredential credential, bool testOnly, CancellationToken token)
    {
        controller.Validate();
        if (Connected) throw new DirectoryException("Disconnect before opening another session.");
        token.ThrowIfCancellationRequested();
        var candidate = connector.Open(controller, credential);
        try
        {
            if (testOnly) return;
            var snapshot = candidate.Read(token);
            token.ThrowIfCancellationRequested();
            Host = controller.Host;
            Objects = snapshot;
            _reader = candidate;
        }
        finally { if (_reader != candidate) candidate.Dispose(); }
    }
    public void Refresh(CancellationToken token)
    {
        var reader = _reader ?? throw new DirectoryException("Connect to a controller first.");
        var previous = Objects;
        try
        {
            var snapshot = reader.Read(token);
            token.ThrowIfCancellationRequested();
            Objects = snapshot;
            _backend?.RefreshAccess();
        }
        catch
        {
            Objects = previous;
            DisableAdministration();
            throw;
        }
    }
    public void Dispose()
    {
        DisableAdministration();
        _reader?.Dispose();
        _reader = null;
        Objects = [];
        Host = "";
    }
}

public sealed record BrowseResult(IReadOnlyList<LiveObject> Items, int Total, int Page, int Pages);
public static class LiveQuery
{
    public const int PageSize = 50;
    public static BrowseResult Browse(IEnumerable<LiveObject> source, string search, string parent, string state, int page)
    {
        var matches = source.Where(o => (parent.Length == 0 || o.Parent.Equals(parent, StringComparison.OrdinalIgnoreCase))
            && (state.Length == 0 || o.Status == state)
            && new[] { o.Name, o.Account, o.DistinguishedName, o.Email, o.Hostname, o.Platform, o.EmployeeInfo?.Department ?? "" }.Any(s => s.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.DistinguishedName).ToArray();
        int pages = Math.Max(1, (matches.Length + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, pages);
        return new(matches.Skip((page - 1) * PageSize).Take(PageSize).ToArray(), matches.Length, page, pages);
    }
}
