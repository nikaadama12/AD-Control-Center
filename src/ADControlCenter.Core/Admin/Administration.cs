using System.Net.Mail;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ADControlCenter.Core.Admin;

public sealed record Employee(string FirstName, string LastName, string DisplayName, string Username, string Upn,
    string Department, string Title, string Email, string Description)
{
    public Dictionary<string, string> EditableAttributes() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["givenName"] = FirstName, ["sn"] = LastName, ["displayName"] = DisplayName,
        ["department"] = Department, ["title"] = Title, ["mail"] = Email, ["description"] = Description
    };
    public static Employee Empty => new("", "", "", "", "", "", "", "", "");
    public string Attributes => $"Name={DisplayName}; given={FirstName}; surname={LastName}; account={Username}; UPN={Upn}; department={Department}; title={Title}; email={Email}; description={Description}";
}

public sealed record AdminPerson(Guid Id, Employee Profile, string Location, bool Enabled, IReadOnlyList<Guid> Groups)
{
    // Demo users have unrestricted fictional fields. LDAP always supplies an explicit set.
    public IReadOnlySet<string>? WritableAttributes { get; init; }
    public bool Allows(string attribute) => WritableAttributes == null || WritableAttributes.Contains(attribute);
    public string Label => $"{Profile.DisplayName} ({Profile.Username})";
}

public sealed record AdminGroup(Guid Id, string Name, string Scope, IReadOnlyList<string> Members);
public sealed record AdminCatalog(string Domain, string[] Locations, AdminPerson[] Users, AdminGroup[] Groups,
    IReadOnlyDictionary<string, Guid[]> Templates, IReadOnlyList<string> ExistingNames);
public sealed record Hire(Employee Profile, string Location, Guid[] Groups, bool Enabled, bool ChangeAtLogon);
public enum AdminAction { CreateDisabled, TemporaryPassword, AccountSettings, AddMember, RemoveMember, EditAttributes, SetEnabled }
public sealed record AdminCommand(AdminAction Action, Guid User = default, Guid Group = default, Employee? Profile = null,
    string Location = "", bool Enabled = false, bool ChangeAtLogon = true, AdminPerson? Expected = null, IReadOnlyList<Guid>? RequestedGroups = null);
public sealed record PreparedChange(string Target, string Before, string After, Func<Guid> Commit);

public interface IAdminBackend
{
    string Actor { get; }
    void RefreshAccess() { }
    AdminCatalog Catalog();
    PreparedChange Prepare(AdminCommand command, SecureString? password);
}

public static partial class AdminValidation
{
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,19}\z")]
    private static partial Regex Sam();
    private static bool Text(string value, int max, bool required = true) => value != null && value.Length <= max &&
            !value.Any(char.IsControl) && value == value.Trim() && (!required || value.Length > 0);

    public static void EmployeeFields(Employee p, string domain)
    {
        if (!Text(p.FirstName, 64) || !Text(p.LastName, 64) || !Text(p.DisplayName, 128))
            throw new DirectoryException("Enter a first name, last name and display name within the field limits.");
        if (!Sam().IsMatch(p.Username) || p.Username.EndsWith('.')) throw new DirectoryException("Use a 1–20 character account name with letters, digits, dots, underscores or hyphens.");
        if (!Text(p.Upn, 113) || !MailAddress.TryCreate(p.Upn, out var upn) || upn.Address != p.Upn ||
            !upn.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)) throw new DirectoryException($"Enter a UPN in {domain}.");
        if (!Text(p.Department, 64) || !Text(p.Title, 128)) throw new DirectoryException("Enter the department and job title.");
        if (!Text(p.Description, 240, false) || !Text(p.Email, 254, false) ||
            p.Email.Length > 0 && (!MailAddress.TryCreate(p.Email, out var email) || email.Address != p.Email))
            throw new DirectoryException("Check the email address and single-line description.");
    }
    public static void EditedFields(Employee before, Employee after)
    {
        if (before.Username != after.Username || before.Upn != after.Upn)
            throw new DirectoryException("Account identifiers cannot be edited.");
        if (before.DisplayName != after.DisplayName && !Text(after.DisplayName, 128))
            throw new DirectoryException("Enter a display name.");
        var fields = new[] { (before.FirstName, after.FirstName, 64), (before.LastName, after.LastName, 64),
            (before.DisplayName, after.DisplayName, 128), (before.Department, after.Department, 64),
            (before.Title, after.Title, 128), (before.Description, after.Description, 240), (before.Email, after.Email, 254) };
        if (fields.Any(field => field.Item1 != field.Item2 && !Text(field.Item2, field.Item3, false)))
            throw new DirectoryException("Check the length, spacing and single-line format of the edited fields.");
        if (before.Email != after.Email && after.Email.Length > 0 &&
            (!MailAddress.TryCreate(after.Email, out var email) || email.Address != after.Email))
            throw new DirectoryException("Enter a valid email address.");
    }

    public static void Identity(Employee p, AdminCatalog catalog)
    {
        EmployeeFields(p, catalog.Domain);
        if (catalog.ExistingNames.Contains(p.Username, StringComparer.OrdinalIgnoreCase) || catalog.ExistingNames.Contains(p.Upn, StringComparer.OrdinalIgnoreCase))
            throw new DirectoryException("The username or UPN already exists.");
    }
    public static void Location(string location, AdminCatalog catalog)
    {
        if (!catalog.Locations.Contains(location, StringComparer.OrdinalIgnoreCase)) throw new DirectoryException("Select an approved destination OU.");
    }
    public static void Groups(IEnumerable<Guid> ids, AdminCatalog catalog)
    {
        if (ids.Any(id => !catalog.Groups.Any(g => g.Id == id))) throw new DirectoryException("A selected group is outside the approved list.");
    }
    public static void Password(SecureString? value)
    {
        if (value is null || value.Length is < 1 or > 256) throw new DirectoryException("Enter a temporary password of up to 256 characters. Domain password policy applies.");
    }
}

public sealed record AdminEvent(DateTimeOffset Utc, Guid OperationId, string Actor, string Operation, string Target,
    string Before, string After, string Outcome, string Details);
public interface IAdminAudit { void Append(AdminEvent entry); IReadOnlyList<AdminEvent> History(); }
public sealed class FileAdminAudit(string file, bool live = false) : IAdminAudit
{
    private void CheckPath()
    {
        if (!live) return;
        ReportWriter.RequirePrivateFolder(Path.GetDirectoryName(file)!);
        if (new FileInfo(file).LinkTarget != null)
            throw new DirectoryException("The live journal must be an ordinary file.");
    }

    public void Append(AdminEvent entry)
    {
        CheckPath();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
        stream.Write(bytes);
        stream.Flush(true);
    }
    public IReadOnlyList<AdminEvent> History()
    {
        try
        {
            CheckPath();
            return File.Exists(file) ? File.ReadLines(file).Where(line => line.Length > 0)
                .Select(line => JsonSerializer.Deserialize<AdminEvent>(line) ?? throw new IOException("Invalid audit entry."))
                .TakeLast(2000).Reverse().ToArray() : [];
        }
        catch (JsonException) { throw new DirectoryException("The private administration journal contains an unreadable entry."); }
    }
}

public sealed record StepResult(string Operation, string Outcome, string Details, string Target);
public sealed record AdminResult(Guid OperationId, Guid User, IReadOnlyList<StepResult> Steps)
{
    public bool Success => Steps.Count > 0 && Steps.All(s => s.Outcome == "Success");
    public string Summary => string.Join(Environment.NewLine, Steps.Select(s => $"{s.Operation} · {s.Outcome} · {s.Details}"));
}

public sealed class AdminWorkflow(IAdminBackend backend, IAdminAudit audit)
{
    public AdminCatalog Catalog() => backend.Catalog();
    public string Actor => backend.Actor;
    public IReadOnlyList<AdminEvent> History() => audit.History();
    public AdminResult Onboard(Hire hire, SecureString password)
    {
        var catalog = Catalog();
        AdminValidation.Identity(hire.Profile, catalog);
        AdminValidation.Location(hire.Location, catalog);
        AdminValidation.Groups(hire.Groups, catalog);
        AdminValidation.Password(password);
        List<AdminCommand> commands = [new(AdminAction.CreateDisabled, Profile: hire.Profile, Location: hire.Location, RequestedGroups: hire.Groups), new(AdminAction.TemporaryPassword)];
        commands.AddRange(hire.Groups.Distinct().Select(id => new AdminCommand(AdminAction.AddMember, Group: id)));
        commands.Add(new(AdminAction.AccountSettings, Enabled: hire.Enabled, ChangeAtLogon: hire.ChangeAtLogon));
        return Execute(commands, password);
    }

    public AdminResult Update(AdminPerson current, Employee profile, bool enabled, IEnumerable<Guid> groups)
    {
        var catalog = Catalog();
        var requested = groups.Distinct().ToArray();
        AdminValidation.Groups(requested, catalog);
        AdminValidation.Groups(current.Groups.Except(requested), catalog);
        var authorized = catalog.Users.SingleOrDefault(u => u.Id == current.Id)
            ?? throw new DirectoryException("Select an approved disposable user.");
        var before = current.Profile.EditableAttributes();
        if ((enabled != current.Enabled && !authorized.Allows("userAccountControl")) ||
            profile.EditableAttributes().Any(pair => before[pair.Key] != pair.Value && !authorized.Allows(pair.Key)))
            throw new DirectoryException("The controller has not advertised write access to a requested attribute.");
        List<AdminCommand> commands = [];
        if (profile != current.Profile)
        {
            AdminValidation.EditedFields(current.Profile, profile);
            commands.Add(new(AdminAction.EditAttributes, current.Id, Profile: profile, Expected: current));
        }
        commands.AddRange(current.Groups.Except(requested).Select(id => new AdminCommand(AdminAction.RemoveMember, current.Id, id)));
        commands.AddRange(requested.Except(current.Groups).Select(id => new AdminCommand(AdminAction.AddMember, current.Id, id)));
        if (enabled != current.Enabled) commands.Add(new(AdminAction.SetEnabled, current.Id, Enabled: enabled, Expected: current));
        if (commands.Count == 0) throw new DirectoryException("There are no changes to apply.");
        return Execute(commands, null);
    }

    private AdminResult Execute(List<AdminCommand> commands, SecureString? password)
    {
        Guid operation = Guid.NewGuid(), user = commands[0].User;
        List<StepResult> results = [];
        foreach (var original in commands)
        {
            var command = original with { User = user };
            var action = command.Action.ToString();
            if (results.Any(r => r.Outcome != "Success"))
            {
                results.Add(new(action, "Skipped", "Earlier step did not complete.", command.Group != Guid.Empty ? command.Group.ToString() : user.ToString()));
                continue;
            }
            string target = command.Group != Guid.Empty ? command.Group.ToString() : command.Profile?.Username ?? user.ToString();
            string before = "", after = "", outcome, detail;
            bool submitted = false;
            AdminEvent Event(string state, string info) => new(DateTimeOffset.UtcNow, operation, Actor, action, target, before, after, state, info);
            try
            {
                var change = backend.Prepare(command, command.Action == AdminAction.TemporaryPassword ? password : null);
                (target, before, after) = (change.Target, change.Before, change.After);
                audit.Append(Event("Pending", "Reviewed transition; server acknowledgement pending."));
                submitted = true;
                var id = change.Commit();
                if (command.Action == AdminAction.CreateDisabled) user = id;
                outcome = "Success"; detail = "Acknowledged.";
            }
            catch (DirectoryException error) { outcome = "Failed"; detail = error.Message; }
            catch (Exception) { outcome = submitted ? "Unconfirmed" : "Failed"; detail = submitted ? "Verify on the controller before any further action." : "Preparation or audit storage unavailable."; }
            try { audit.Append(Event(outcome, detail)); }
            catch (Exception) { detail += " Outcome audit could not be saved."; outcome = "Audit incomplete"; }
            results.Add(new(action, outcome, detail, target));
        }
        return new(operation, user, results);
    }
}
