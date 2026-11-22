using System.Text.Json;
using System.Text.RegularExpressions;

namespace ADControlCenter.Core;

public sealed partial class DemoDirectoryService : IDirectoryService, IDemoWorkspace, IDisposable
{
    private readonly string _file;
    private readonly FileStream _lease;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions StorageOptions = new() { IgnoreReadOnlyProperties = true };
    private DirectoryData _data;

    public DemoDirectoryService(string folder)
    {
        Directory.CreateDirectory(folder);
        _file = Path.Combine(folder, "directory.json");
        try { _lease = new FileStream(Path.Combine(folder, "workspace.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("The local workspace is busy. Close any other AD Control Center window and try again.", ex); }
        try
        {
            if (File.Exists(_file))
            {
                _data = JsonSerializer.Deserialize<DirectoryData>(File.ReadAllText(_file))
                    ?? throw new InvalidDataException("The workspace file is empty.");
                if (_data.SchemaVersion != 1 || _data.Users is null || _data.Computers is null ||
                    _data.Groups is null || _data.Audit is null || _data.Settings is null)
                    throw new InvalidDataException("The workspace format is invalid or unsupported.");
                _data = Freeze(_data);
            }
            else
            {
                _data = SampleDirectory.Create(new("Dark", Path.Combine(folder, "Exports")));
                Persist(_data);
            }
        }
        catch { _lease.Dispose(); throw; }
    }

    public DirectoryData Read() { lock (_gate) return _data; }
    public DirectoryInventory GetInventory()
    {
        var data = Read();
        return new(data.Users, data.Computers, data.Groups);
    }
    public Preferences Settings => Read().Settings;
    public IReadOnlyList<AuditEntry> Audit => Read().Audit;
    public void Dispose() => _lease.Dispose();

    private static DirectoryData Freeze(DirectoryData d) => d with
    {
        Users = Array.AsReadOnly(d.Users.ToArray()),
        Computers = Array.AsReadOnly(d.Computers.ToArray()),
        Groups = Array.AsReadOnly(d.Groups.Select(g => g with { Members = Array.AsReadOnly(g.Members.ToArray()) }).ToArray()),
        Audit = Array.AsReadOnly(d.Audit.ToArray())
    };

    private void Persist(DirectoryData next)
    {
        next = Freeze(next);
        string temp = _file + ".tmp";
        try
        {
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, next, StorageOptions);
                stream.Flush(true);
            }
            File.Move(temp, _file, true);
            _data = next;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void Commit(DirectoryData next, string action, string target, string details, string outcome = "Success", bool changed = true)
    {
        var entry = new AuditEntry(DateTimeOffset.UtcNow, "Demo Operator", action, target, outcome, details);
        Persist(next with { Revision = _data.Revision + (changed ? 1 : 0), Audit = _data.Audit.Prepend(entry).ToArray() });
    }

    public void Record(string action, string target, string outcome, string details)
    {
        lock (_gate) Commit(_data, action, target, details, outcome, false);
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
    internal static string ValidateUser(UserInput input, ISet<string> usernames)
    {
        if (!ValidText(input.DisplayName, 80)) return "Enter a display name of 1–80 characters.";
        if (!UsernamePattern().IsMatch(input.Username)) return "Use 1–32 letters, numbers, dots, underscores or hyphens for the username. Start with a letter or number.";
        if (!ValidText(input.Department, 64)) return "Enter an organizational unit of 1–64 characters.";
        return usernames.Contains(input.Username) ? "This username already exists." : "";
    }

    private static bool ValidText(string text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max && !text.Any(char.IsControl);
    private DirectoryUser FindUser(Guid id) => _data.Users.FirstOrDefault(u => u.Id == id) ?? throw new DirectoryException("This user no longer exists.");

    private static DirectoryUser BuildUser(Guid id, UserInput values, ISet<string> usernames)
    {
        var input = new UserInput(values.DisplayName.Trim(), values.Username.Trim().ToLowerInvariant(), values.Department.Trim());
        string error = ValidateUser(input, usernames);
        if (error.Length > 0) throw new DirectoryException(error);
        return new(id, input.DisplayName, input.Username, input.Department, true, false);
    }

    public void SaveUser(Guid? id, UserInput values)
    {
        lock (_gate)
        {
            var names = _data.Users.Where(u => u.Id != id).Select(u => u.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var user = BuildUser(id ?? Guid.NewGuid(), values, names);
            var users = _data.Users.ToList();
            if (id.HasValue)
            {
                var old = FindUser(id.Value);
                users[users.IndexOf(old)] = old with { DisplayName = user.DisplayName, Username = user.Username, Department = user.Department };
            }
            else users.Add(user);
            Commit(_data with { Users = users }, id.HasValue ? "User updated" : "User created", user.Username, $"Organizational unit: {user.Department}.");
        }
    }

    public void PerformAccountAction(Guid id, AccountAction action)
    {
        lock (_gate)
        {
            var old = FindUser(id);
            var updated = action switch
            {
                AccountAction.Enable when !old.Enabled => old with { Enabled = true },
                AccountAction.Disable when old.Enabled => old with { Enabled = false },
                AccountAction.Unlock when old.Locked => old with { Locked = false },
                AccountAction.ResetPassword => old with { LastPasswordSimulation = DateTimeOffset.UtcNow },
                _ => throw new DirectoryException("The selected action does not apply to this account.")
            };
            string label = action == AccountAction.ResetPassword ? "password reset" : action.ToString().ToLowerInvariant();
            Commit(_data with { Users = _data.Users.Select(u => u.Id == id ? updated : u).ToArray() },
                $"DEMO {label}", old.Username, "Operation simulated on the local account record.");
        }
    }

    public void SaveGroup(Guid? id, string name, string description, IReadOnlyList<Guid> members)
    {
        lock (_gate)
        {
            name = name.Trim();
            description = description.Trim();
            if (!ValidText(name, 64)) throw new DirectoryException("Enter a group name of 1–64 characters.");
            if (description.Length > 240 || description.Any(char.IsControl)) throw new DirectoryException("Use a single-line description of up to 240 characters.");
            if (_data.Groups.Any(g => g.Id != id && g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new DirectoryException("This group name already exists.");
            var ids = members.Distinct().ToArray();
            var known = _data.Users.Select(u => u.Id).ToHashSet();
            if (ids.Any(member => !known.Contains(member))) throw new DirectoryException("A selected member no longer exists.");
            var old = _data.Groups.FirstOrDefault(g => g.Id == id);
            if (id.HasValue && old is null) throw new DirectoryException("This group no longer exists.");
            var group = new DirectoryGroup(id ?? Guid.NewGuid(), name, description, ids);
            var groups = _data.Groups.Where(g => g.Id != id).Append(group).ToArray();
            var previous = old?.Members ?? [];
            Commit(_data with { Groups = groups }, old is null ? "Group created" : "Group updated", name,
                $"{ids.Length} members; {ids.Except(previous).Count()} added, {previous.Except(ids).Count()} removed.");
        }
    }

    public ImportPreview PreviewImport(string csv, string source)
    {
        lock (_gate)
        {
            var rows = CsvImport.Parse(csv, _data.Users);
            Record("Import previewed", source, rows.Any(r => r.Error.Length > 0) ? "Validation" : "Success",
                $"{rows.Count} rows, {rows.Count(r => r.Error.Length > 0)} invalid.");
            return new(rows, _data.Revision, source);
        }
    }

    public void ApplyImport(ImportPreview preview)
    {
        lock (_gate)
        {
            if (preview.Revision != _data.Revision) throw new DirectoryException("Directory data changed. Preview the CSV again.");
            if (!preview.CanApply || preview.Rows.Count > 5000) throw new DirectoryException("Correct the CSV and preview it again.");
            var names = _data.Users.Select(u => u.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var users = _data.Users.ToList();
            foreach (var row in preview.Rows)
            {
                var user = BuildUser(Guid.NewGuid(), row.Input, names);
                users.Add(user);
                names.Add(user.Username);
            }
            Commit(_data with { Users = users }, "Bulk users created", preview.Source, $"{preview.Rows.Count} fictional users imported.");
        }
    }

    public string Export(ReportKind kind, ReportFormat format)
    {
        lock (_gate)
        {
            string file = ReportWriter.Write(_data, kind, format);
            try { Record("Report exported", kind.ToString(), "Success", Path.GetFileName(file)); }
            catch { File.Delete(file); throw; }
            return file;
        }
    }

    public void SaveSettings(Preferences settings)
    {
        if (settings.Theme is not ("Dark" or "Light")) throw new DirectoryException("Choose Dark or Light.");
        string folder = settings.ExportDirectory.Trim();
        if (!Path.IsPathFullyQualified(folder)) throw new DirectoryException("Choose an absolute export directory.");
        lock (_gate) Commit(_data with { Settings = settings with { ExportDirectory = Path.GetFullPath(folder) } },
            "Settings updated", "Workspace", "Theme and export directory saved.");
    }

    public void Clear()
    {
        lock (_gate) Commit(_data with { Users = [], Computers = [], Groups = [] }, "Directory cleared", "northstar.example", "Removed all fictional accounts, computers and groups.");
    }

    public void Reset()
    {
        lock (_gate) Commit(SampleDirectory.Create(_data.Settings), "Samples restored", "northstar.example", "Restored the fictional sample directory.");
    }
}
