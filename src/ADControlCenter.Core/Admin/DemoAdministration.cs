using ADControlCenter.Core.Admin;
using System.Security;

namespace ADControlCenter.Core;

public sealed partial class DemoDirectoryService : IAdminBackend
{
    string IAdminBackend.Actor => "Demo Operator";
    public string DemoAuditPath => Path.Combine(Path.GetDirectoryName(_file)!, "demo-administration.jsonl");

    private static Employee Profile(DirectoryUser user)
    {
        var names = user.DisplayName.Split(' ', 2);
        return (user.EmployeeInfo ?? new Employee(names[0], names.Last(), user.DisplayName, user.Username, user.Username + "@northstar.example", user.Department, "", "", ""))
            with
        { DisplayName = user.DisplayName, Username = user.Username };
    }
    public AdminCatalog Catalog()
    {
        var data = Read();
        var groups = data.Groups.Select(g => new AdminGroup(g.Id, g.Name, "Global security",
            data.Users.Where(u => g.Members.Contains(u.Id)).Select(u => u.Username).ToArray())).ToArray();
        var templates = new Dictionary<string, Guid[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (department, team) in new[] { ("IT", "Engineering Team"), ("Finance", "Finance Team"), ("HR", "People Team") })
            templates[department] = groups.Where(g => g.Name == team).Select(g => g.Id).ToArray();
        string templateFile = Path.Combine(Path.GetDirectoryName(_file)!, "demo-templates.json");
        if (File.Exists(templateFile))
        {
            try
            {
                var overrides = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(templateFile));
                if (overrides == null) throw new DirectoryException("Invalid demo department templates.");
                foreach (var (department, names) in overrides)
                {
                    if (names == null || names.Any(name => !groups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
                        throw new DirectoryException("A demo template references a missing group.");
                    templates[department] = groups.Where(g => names.Contains(g.Name, StringComparer.OrdinalIgnoreCase)).Select(g => g.Id).ToArray();
                }
            }
            catch (System.Text.Json.JsonException) { throw new DirectoryException("Invalid demo-templates.json."); }
        }
        return new("northstar.example", SampleDirectory.Units.Concat(data.Users.Select(u => u.Department)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            data.Users.Select(u => new AdminPerson(u.Id, Profile(u), u.Department, u.Enabled, data.Groups.Where(g => g.Members.Contains(u.Id)).Select(g => g.Id).ToArray())).ToArray(),
            groups, templates, data.Users.SelectMany(u => new[] { u.Username, Profile(u).Upn }).ToArray());
    }

    public PreparedChange Prepare(AdminCommand command, SecureString? password)
    {
        lock (_gate)
        {
            var revision = _data.Revision;
            var next = _data;
            DirectoryUser user;
            string before, after, target;
            if (command.Action == AdminAction.CreateDisabled)
            {
                var p = command.Profile!;
                var catalog = Catalog();
                AdminValidation.Identity(p, catalog);
                AdminValidation.Location(command.Location, catalog);
                user = new(Guid.NewGuid(), p.DisplayName, p.Username, command.Location, false, false) { EmployeeInfo = p };
                next = next with { Users = next.Users.Append(user).ToArray() };
                before = "Absent"; after = $"Disabled; OU={command.Location}; {p.Attributes}"; target = user.Username;
            }
            else
            {
                user = FindUser(command.User);
                var updated = user;
                target = user.Username;
                switch (command.Action)
                {
                    case AdminAction.EditAttributes:
                        if (Profile(user) != command.Expected?.Profile) throw new DirectoryException("Attributes changed since review.");
                        before = Profile(user).Attributes; after = command.Profile!.Attributes;
                        updated = user with { DisplayName = command.Profile.DisplayName, EmployeeInfo = command.Profile }; break;
                    case AdminAction.AccountSettings:
                    case AdminAction.SetEnabled:
                        if (command.Expected != null && command.Expected.Enabled != user.Enabled) throw new DirectoryException("Account state changed since review.");
                        before = $"Enabled={user.Enabled}; change at logon={user.ChangeAtLogon}";
                        updated = user with { Enabled = command.Enabled, ChangeAtLogon = command.Action == AdminAction.AccountSettings ? command.ChangeAtLogon : user.ChangeAtLogon };
                        after = $"Enabled={updated.Enabled}; change at logon={updated.ChangeAtLogon}"; break;
                    case AdminAction.TemporaryPassword:
                        AdminValidation.Password(password);
                        before = "Not set"; after = "Simulated temporary password set";
                        updated = user with { LastPasswordSimulation = DateTimeOffset.UtcNow }; break;
                    case AdminAction.AddMember:
                    case AdminAction.RemoveMember:
                        var group = next.Groups.SingleOrDefault(g => g.Id == command.Group) ?? throw new DirectoryException("Group no longer exists.");
                        bool add = command.Action == AdminAction.AddMember;
                        if (group.Members.Contains(user.Id) == add) throw new DirectoryException("Membership changed since review.");
                        target = $"{user.Username} / {group.Name}"; before = add ? "Absent" : "Member"; after = add ? "Member" : "Absent";
                        var members = add ? group.Members.Append(user.Id).ToArray() : group.Members.Where(id => id != user.Id).ToArray();
                        next = next with { Groups = next.Groups.Select(g => g.Id == group.Id ? group with { Members = members } : g).ToArray() }; break;
                    default: throw new DirectoryException("Unsupported operation.");
                }
                next = next with { Users = next.Users.Select(u => u.Id == user.Id ? updated : u).ToArray() };
            }
            return new(target, before, after, () =>
            {
                lock (_gate)
                {
                    if (_data.Revision != revision) throw new DirectoryException("Workspace changed since review.");
                    Commit(next, command.Action.ToString(), target, $"{before} → {after}");
                    return user.Id;
                }
            });
        }
    }
}
