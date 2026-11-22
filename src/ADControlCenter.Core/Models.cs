namespace ADControlCenter.Core;

public sealed record DirectoryUser(Guid Id, string DisplayName, string Username, string Department,
    bool Enabled, bool Locked, DateTimeOffset? LastPasswordSimulation = null)
{
    public Admin.Employee? EmployeeInfo { get; init; }
    public bool ChangeAtLogon { get; init; }
    public string Status => !Enabled ? "Disabled" : Locked ? "Locked" : "Active";
}

public sealed record Computer(Guid Id, string Name, string OperatingSystem, string Department, DateTimeOffset LastSeen)
{
    public bool IsStale => LastSeen < DateTimeOffset.UtcNow.AddDays(-90);
    public string Status => IsStale ? "Stale" : "Current";
}

public sealed record DirectoryGroup(Guid Id, string Name, string Description, IReadOnlyList<Guid> Members)
{
    public int MemberCount => Members.Count;
}

public sealed record AuditEntry(DateTimeOffset Timestamp, string Actor, string Action, string Target, string Outcome, string Details);
public sealed record Preferences(string Theme, string ExportDirectory);
public sealed record DirectoryInventory(IReadOnlyList<DirectoryUser> Users, IReadOnlyList<Computer> Computers,
    IReadOnlyList<DirectoryGroup> Groups);
public sealed record DirectoryData(int SchemaVersion, long Revision, IReadOnlyList<DirectoryUser> Users,
    IReadOnlyList<Computer> Computers, IReadOnlyList<DirectoryGroup> Groups, IReadOnlyList<AuditEntry> Audit, Preferences Settings);
public sealed record UserInput(string DisplayName, string Username, string Department);
public sealed record ImportRow(int Row, UserInput Input, string Error)
{
    public string Result => Error.Length == 0 ? "Ready" : Error;
}
public sealed record ImportPreview(IReadOnlyList<ImportRow> Rows, long Revision, string Source)
{
    public bool CanApply => Rows.Count > 0 && Rows.All(row => row.Error.Length == 0);
}

public enum AccountAction { Enable, Disable, Unlock, ResetPassword }
public enum ReportKind { Users, Computers, SecurityGroups, OrganizationalUnits, CountsOnly }
public enum ReportFormat { Csv, Json, Html }
public sealed class DirectoryException(string message) : Exception(message);

public interface IDirectoryService
{
    DirectoryInventory GetInventory();
    void SaveUser(Guid? id, UserInput values);
    void PerformAccountAction(Guid id, AccountAction action);
    void SaveGroup(Guid? id, string name, string description, IReadOnlyList<Guid> members);
    ImportPreview PreviewImport(string csv, string source);
    void ApplyImport(ImportPreview preview);
}

public interface IDemoWorkspace
{
    Preferences Settings { get; }
    IReadOnlyList<AuditEntry> Audit { get; }
    void Record(string action, string target, string outcome, string details);
    string Export(ReportKind kind, ReportFormat format);
    void SaveSettings(Preferences settings);
    void Clear();
    void Reset();
}
