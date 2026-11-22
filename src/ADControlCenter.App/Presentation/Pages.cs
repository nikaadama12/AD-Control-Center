using System.Collections;
using System.Windows.Data;
using System.ComponentModel;
using ADControlCenter.Core;
using ADControlCenter.Core.Live;

namespace ADControlCenter.App.Presentation;

public sealed record NavItem(string Name, string Icon);
public sealed record Metric(string Label, string Value, string Detail, string Icon, string Accent, string Page);
public sealed record UnitBar(string Name, int Count, double Fraction);
public sealed record HealthItem(string Label, int Count, string Color);
public sealed record ReportChoice(string Name, string Description, ReportKind Kind);

public sealed class DashboardPage
{
    public IReadOnlyList<Metric> Metrics { get; }
    public IReadOnlyList<UnitBar> Units { get; }
    public IReadOnlyList<HealthItem> Health { get; }
    public IReadOnlyList<AuditEntry> Recent { get; }
    public int Total { get; }
    public string DistributionTitle { get; } = "Users by organizational unit";
    public string DistributionCaption { get; } = "Account distribution · Top 6 units";
    public DashboardPage(IReadOnlyList<LiveObject> snapshot, IReadOnlyList<AuditEntry> events)
    {
        DistributionTitle = "Users by parent location";
        DistributionCaption = "Account distribution · Top 6 containers";
        var users = snapshot.Where(o => o.Kind == ObjectKind.Users).ToArray();
        Total = users.Length;
        var units = users.GroupBy(o => o.Parent, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Take(6).ToArray();
        Units = units.Select(g => new UnitBar(g.Key, g.Count(), (double)g.Count() / units[0].Count())).ToArray();
        Health = new[] { ("Enabled", "#B9F481"), ("Disabled", "#7B95AF"), ("Locked", "#EFBC77"), ("Unknown", "#B8A3FA") }
            .Select(s => new HealthItem(s.Item1, users.Count(u => u.Status == s.Item1), s.Item2)).Where(h => h.Count > 0).ToArray();
        Metrics = [
            new("Directory users", Total.ToString("N0"), "Visible user accounts", "Users", "#B9F481", "Users"),
            new("Computers", Count(ObjectKind.Computers), "Visible devices", "Computers", "#83BBFF", "Computers"),
            new("Groups", Count(ObjectKind.Groups), "Visible directory groups", "Groups", "#B8A3FA", "Groups"),
            new("Organizational units", Count(ObjectKind.OrganizationalUnits), "Visible hierarchy", "Directory", "#F0BC73", "Organizational Units")];
        Recent = events.Take(5).ToArray();
        string Count(ObjectKind kind) => snapshot.Count(o => o.Kind == kind).ToString("N0");
    }
    public DashboardPage(DirectoryInventory data, IReadOnlyList<AuditEntry> audit)
    {
        Total = data.Users.Count;
        int active = data.Users.Count(u => u.Status == "Active");
        int locked = data.Users.Count(u => u.Status == "Locked");
        int disabled = Total - active - locked;
        int stale = data.Computers.Count(c => c.IsStale);
        Metrics = [
            new("Directory users", Total.ToString("N0"), $"{active:N0} active accounts", "Users", "#B9F481", "Users"),
            new("Computers", data.Computers.Count.ToString("N0"), $"{data.Computers.Count - stale:N0} current devices", "Computers", "#83BBFF", "Computers"),
            new("Security groups", data.Groups.Count.ToString("N0"), $"{data.Groups.Sum(g => g.MemberCount):N0} memberships", "Groups", "#B8A3FA", "Groups"),
            new("Stale computers", stale.ToString("N0"), "Inactive over 90 days", "History", "#F0BC73", "Stale")
        ];
        var units = data.Users.GroupBy(u => u.Department, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToArray();
        Units = units.Take(6).Select(g => new UnitBar(g.Key, g.Count(), (double)g.Count() / units[0].Count())).ToArray();
        Health = [new("Active", active, "#B9F481"), new("Disabled", disabled, "#7B95AF"), new("Locked", locked, "#EFBC77")];
        Recent = audit.Where(a => a.Action != "Page viewed" && a.Action != "Workspace opened").Take(5).ToArray();
    }
}

public sealed class RecordsPage : Bindable
{
    private readonly Action<string> _audit;
    public string Name { get; }
    public ListCollectionView View { get; }
    public string[] Options { get; }
    public string[] Units { get; }
    public string[] OperatingSystems { get; }
    private string _search = "", _filter, _unit = "All units", _os = "All systems";
    private object? _selected;
    public object? Selected { get => _selected; set { if (Set(ref _selected, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public string Search { get => _search; set { if (Set(ref _search, value)) Refresh("Search"); } }
    public string Filter { get => _filter; set { if (value != null && Set(ref _filter, value)) Refresh("Status filter"); } }
    public string Unit { get => _unit; set { if (value != null && Set(ref _unit, value)) Refresh("Unit filter"); } }
    public string OS { get => _os; set { if (value != null && Set(ref _os, value)) Refresh("OS filter"); } }
    public string CountLabel => $"{View.Count:N0} {(View.Count == 1 ? "record" : "records")}";
    public RecordsPage(string name, DirectoryInventory d, IReadOnlyList<AuditEntry> entries, Action<string> audit, RecordsPage? previous = null, string? initialFilter = null)
    {
        Name = name;
        _audit = audit;
        IList list = name switch
        {
            "Users" => d.Users.ToList(),
            "Computers" => d.Computers.ToList(),
            "Groups" => d.Groups.ToList(),
            _ => entries.ToList()
        };
        Options = name switch
        {
            "Users" => ["All statuses", "Active", "Disabled", "Locked"],
            "Computers" => ["All statuses", "Current", "Stale"],
            "Groups" => ["All groups", "With members", "Empty"],
            _ => ["All outcomes", "Success", "Cancelled", "Validation", "Failed"]
        };
        Units = new[] { "All units" }.Concat((name == "Users" ? d.Users.Select(u => u.Department) : d.Computers.Select(c => c.Department))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order()).ToArray();
        OperatingSystems = new[] { "All systems" }.Concat(d.Computers.Select(c => c.OperatingSystem).Distinct().Order()).ToArray();
        _filter = initialFilter ?? previous?.Filter ?? Options[0];
        if (previous != null)
        {
            _search = previous.Search;
            if (Units.Contains(previous.Unit)) _unit = previous.Unit;
            if (OperatingSystems.Contains(previous.OS)) _os = previous.OS;
        }
        View = new ListCollectionView(list) { Filter = Matches };
        if (previous != null)
            foreach (SortDescription sort in previous.View.SortDescriptions) View.SortDescriptions.Add(sort);
        Selected = list.Cast<object>().FirstOrDefault(x => View.Contains(x) && SameRecord(x, previous?.Selected));
    }

    private static bool SameRecord(object a, object? b) => (a, b) switch
    {
        (DirectoryUser x, DirectoryUser y) => x.Id == y.Id,
        (DirectoryGroup x, DirectoryGroup y) => x.Id == y.Id,
        (Computer x, Computer y) => x.Id == y.Id,
        _ => Equals(a, b)
    };

    private bool Matches(object obj)
    {
        bool search(params string[] values) => values.Any(s => s.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase));
        bool unit(string value) => _unit == "All units" || value.Equals(_unit, StringComparison.OrdinalIgnoreCase);
        bool status(string value) => _filter == Options[0] || _filter == value;
        return obj switch
        {
            DirectoryUser u => search(u.DisplayName, u.Username, u.Department, u.EmployeeInfo?.Department ?? "") && unit(u.Department) && status(u.Status),
            Computer c => search(c.Name, c.OperatingSystem, c.Department) && unit(c.Department) && status(c.Status) && (_os == "All systems" || _os == c.OperatingSystem),
            DirectoryGroup g => search(g.Name, g.Description) && status(g.MemberCount == 0 ? "Empty" : "With members"),
            AuditEntry a => search(a.Actor, a.Action, a.Target, a.Details, a.Outcome) && status(a.Outcome),
            _ => false
        };
    }

    public void RecordSort(string column) => _audit($"Sort by {column}");

    private void Refresh(string action)
    {
        View.Refresh();
        if (Selected != null && !View.Contains(Selected)) Selected = null;
        Changed(nameof(CountLabel));
        _audit(action);
    }
}

public sealed class BulkPage : Bindable
{
    private readonly Action<string> _audit;
    private string _search = "", _filter = "All rows";
    public string[] Filters { get; } = ["All rows", "Ready", "Invalid"];
    public string Search { get => _search; set { if (Set(ref _search, value)) Refresh("Preview search"); } }
    public string Filter { get => _filter; set { if (value != null && Set(ref _filter, value)) Refresh("Preview filter"); } }
    public ListCollectionView Rows { get; }
    public ImportPreview? Preview { get; }
    public string FileLabel => Preview?.Source ?? "Choose a CSV to preview";
    public string Summary => Preview == null ? "No file selected" : $"{Preview.Rows.Count} rows  /  {Preview.Rows.Count(r => r.Error.Length > 0)} invalid";
    public BulkPage(ImportPreview? preview, Action<string> audit)
    {
        Preview = preview;
        _audit = audit;
        Rows = new ListCollectionView((preview?.Rows ?? []).ToList())
        {
            Filter = obj => obj is ImportRow r && (_filter == "All rows" || (_filter == "Ready") == (r.Error.Length == 0))
                && new[] { r.Input.DisplayName, r.Input.Username, r.Input.Department, r.Error }.Any(s => s.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase))
        };
    }
    private void Refresh(string action)
    {
        Rows.Refresh();
        _audit(action);
    }
}
public sealed class ReportsPage : Bindable
{
    public ReportChoice[] Choices { get; }
    public ReportFormat[] Formats { get; }
    private ReportChoice _selected;
    public ReportChoice Selected { get => _selected; set => Set(ref _selected, value); }
    public ReportFormat Format { get; set; }
    public string Folder { get; }

    public ReportsPage(string folder, bool live = false)
    {
        Folder = folder;
        Formats = live ? [ReportFormat.Csv, ReportFormat.Html] : Enum.GetValues<ReportFormat>();
        Choices = [
            new("User accounts", live ? "Names, account names, parent directory paths, account states and email addresses."
                : "Display names, usernames, organizational units and account states.", ReportKind.Users),
            new("Security groups", live ? "Security group names, parent directory paths, descriptions and direct member counts."
                : "Group names, descriptions and member counts.", ReportKind.SecurityGroups),
            new("Computers", live ? "Names, account names, parent directory paths, account states and operating systems."
                : "Hostnames, operating systems, organizational units, last activity and stale-account status.", ReportKind.Computers),
            new("Organizational units", live ? "OU names, distinguished names and parent directory paths."
                : "Distinct organizational unit names assigned to demo users and computers.", ReportKind.OrganizationalUnits),
            new("Counts Only (Privacy-Safe)", "Six numeric totals only: users, enabled users, disabled users, groups, computers and OUs. No identifying fields.", ReportKind.CountsOnly)
        ];
        _selected = Choices[0];
    }
}

public sealed class SettingsPage(Preferences prefs) : Bindable
{
    private string _folder = prefs.ExportDirectory;
    public string Folder { get => _folder; set => Set(ref _folder, value); }
    public string Theme { get; set; } = prefs.Theme;
    public string[] Themes { get; } = ["Dark", "Light"];
}
