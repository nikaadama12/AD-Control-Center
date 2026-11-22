using System.Windows.Input;
using ADControlCenter.Core;
using ADControlCenter.Core.Live;

namespace ADControlCenter.App.Presentation;

public sealed partial class ShellViewModel : Bindable
{
    private readonly IDirectoryService _directory;
    private readonly IDemoWorkspace _workspace;
    private readonly IInteraction _ui;
    private NavItem _current;
    private object _page;
    private string _status = "Workspace ready";
    private ImportPreview? _preview;
    private NavItem[] DemoNavigation { get; } = [new("Dashboard","Dashboard"), new("Users","Users"), new("Computers","Computers"),
        new("Groups","Groups"), new("Bulk Operations","Bulk"), new("Reports","Reports"), new("Audit Logs","Audit")];
    public NavItem SettingsNav { get; } = new("Settings", "Settings");
    public NavItem Current { get => _current; private set { Set(ref _current, value); Changed(nameof(Title)); Changed(nameof(Subtitle)); } }
    public object Page { get => _page; private set => Set(ref _page, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Title => Current.Name switch { "Dashboard" => "Directory overview", "Bulk Operations" => "Bulk operations", "Audit Logs" => "Audit logs", _ => Current.Name };
    public string Subtitle => Current.Name == "Connection Manager" ? "Test authentication and manage your protected directory session." : IsLive ? $"Read-only directory snapshot · {_loaded:g}" : Current.Name switch
    {
        "Dashboard" => "Account health and activity across your local directory.",
        "Users" => "Manage fictional user accounts and simulate account operations.",
        "Computers" => "Inspect device inventory and identify inactive computer accounts.",
        "Groups" => "Manage security groups and their directory memberships.",
        "Bulk Operations" => "Validate a CSV and review every user before applying changes.",
        "Reports" => "Export a snapshot of your fictional directory.",
        "Audit Logs" => "Review actions and outcomes in the local workspace.",
        _ => "Configure your workspace and manage the local sample directory."
    };
    public string Today => DateTime.Now.ToString("ddd, MMM d, yyyy");
    public ICommand Navigate { get; }
    public ICommand Run { get; }
    public ShellViewModel(IDirectoryService directory, IDemoWorkspace workspace, IInteraction ui, ILiveConnector connector, string? liveExportFolder = null)
    {
        PrivateExports = liveExportFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AD Control Center", "LiveExports");
        _live = new(connector);
        _directory = directory;
        _workspace = workspace;
        _ui = ui;
        _current = DemoNavigation[0];
        workspace.Record("Workspace opened", "northstar.example", "Success", "Local DEMO workspace initialized.");
        _page = new DashboardPage(directory.GetInventory(), workspace.Audit);
        Navigate = new Command(p => Safe("Navigation", () => Open(p is NavItem n ? n.Name : p?.ToString() ?? "Dashboard")), p => !Busy && (!IsLive || (p as NavItem)?.Name != "Settings"));
        Run = new Command(p => Safe(p?.ToString() ?? "Command", () => Execute(p!.ToString()!)), p => Available(p?.ToString()));
    }

    private void Safe(string action, Action work)
    {
        try { work(); }
        catch (Exception e) when (e is DirectoryException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            Status = e.Message;
            try { Record(action, Current.Name, e is DirectoryException ? "Validation" : "Failed", e.Message); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { Status += " Audit storage is unavailable."; }
            _ui.Inform("Action needs attention", Status);
        }
    }
    private void Open(string name)
    {
        if (Busy || IsLive && !LiveNavigation.Any(n => n.Name == name)) return;
        string? filter = name == "Stale" ? "Stale" : null;
        if (filter != null) name = "Computers";
        Current = Navigation.Append(SettingsNav).First(n => n.Name == name);
        Record("Page viewed", name, "Success", "Opened workspace page.");
        Load(filter: filter);
        Status = $"{name} ready";
    }

    private void Load(bool preserve = false, string? filter = null)
    {
        if (IsLive) { LoadLive(); return; }
        var d = _directory.GetInventory();
        Page = Current.Name switch
        {
            "Connection Manager" => new ConnectionPage(),
            "Dashboard" => new DashboardPage(d, _workspace.Audit),
            "Bulk Operations" => new BulkPage(_preview, AuditViewChange),
            "Reports" => new ReportsPage(_workspace.Settings.ExportDirectory),
            "Settings" => new SettingsPage(_workspace.Settings),
            _ => new RecordsPage(Current.Name, d, _workspace.Audit, AuditViewChange,
                preserve ? Page as RecordsPage : null, filter)
        };
        CommandManager.InvalidateRequerySuggested();
    }
    private void AuditViewChange(string action) =>
        Safe(action, () => Record(action, Current.Name, "Success", "Directory view updated."));

    private object? Selected => (Page as RecordsPage)?.Selected;
    private bool Available(string? action) => !Busy && (IsLive ? action switch
    {
        "Refresh" or "Disconnect" => true,
        "Export" => Page is ReportsPage,
        "View" => Page is LiveRecordsPage { Selected: not null } || Selected != null,
        _ => false
    } : action != "Disconnect" && action switch
    {
        "View" or "Edit" => Selected != null,
        "Enable" => Selected is DirectoryUser { Enabled: false },
        "Disable" => Selected is DirectoryUser { Enabled: true },
        "Unlock" => Selected is DirectoryUser { Locked: true },
        "ResetPassword" => Selected is DirectoryUser,
        "Apply" => _preview?.CanApply == true,
        _ => true
    });

    private bool Confirm(string action, string target, string details, string accept)
    {
        if (_ui.Confirm(action, details, accept)) return true;
        Cancel(action, target);
        return false;
    }
    private void Cancel(string action, string target)
    {
        Record(action, target, "Cancelled", "Operator cancelled the action.");
        Status = "Action cancelled";
    }

    private void Execute(string action)
    {
        if (!Available(action)) return;
        if (IsLive) { ExecuteLive(action); return; }
        switch (action)
        {
            case "Refresh":
                Record("View refreshed", Current.Name, "Success", "Reloaded the local directory view.");
                Load(true); Status = "Directory refreshed"; break;
            case "View": ViewSelected(); break;
            case "New": case "Edit": Edit(action == "Edit"); break;
            case "Enable":
            case "Disable":
            case "Unlock":
            case "ResetPassword":
                Account(Enum.Parse<AccountAction>(action)); break;
            case "Import": Import(); break;
            case "Apply":
                if (_preview == null || !Confirm("Apply CSV import?", _preview.Source, $"Create {_preview.Rows.Count} enabled DEMO users from {_preview.Source}?", "Create users")) return;
                _directory.ApplyImport(_preview); _preview = null; Load(); Status = "CSV import applied"; break;
            case "Export":
                ExportReport((ReportsPage)Page); break;
            case "Folder":
                var settings = (SettingsPage)Page;
                string? path = _ui.ChooseFolder(settings.Folder);
                if (path != null) { settings.Folder = path; Record("Folder selected", "Settings", "Success", "Export directory selected for editing."); }
                else Cancel("Choose folder", "Settings");
                break;
            case "SaveSettings":
                var s = (SettingsPage)Page;
                _workspace.SaveSettings(new(s.Theme, s.Folder)); _ui.ApplyTheme(s.Theme); Status = "Settings saved"; break;
            case "Clear":
            case "Reset":
                bool reset = action == "Reset";
                if (!Confirm(reset ? "Restore sample directory?" : "Clear demo directory?", "northstar.example",
                    reset ? "Replace all users, computers, groups and memberships with fresh sample records? Your settings and audit history will be kept." :
                    "Remove all fictional users, computers, groups and memberships? Your settings and audit history will be kept.", reset ? "Restore samples" : "Clear directory")) return;
                if (reset) _workspace.Reset(); else _workspace.Clear();
                _preview = null; Load(); Status = reset ? "Sample directory restored" : "Demo directory cleared"; break;
        }
    }

    private void ExportReport(ReportsPage report)
    {
        var choice = report.Selected;
        bool counts = choice.Kind == ReportKind.CountsOnly;
        string title = counts ? "Export aggregate counts?" : "Export detailed directory data?";
        string scope = IsLive && !counts ? $"{ReportWriter.LiveDetailCount(_live.Objects, choice.Kind):N0} live records."
            : ModeLabel;
        string privacy = counts ? "" : IsLive ? "\nThis file contains real identifying data. Keep it private and outside Git."
            : "\nThis file contains individual fictional directory records.";
        if (!Confirm(title, "Reports", $"{choice.Name} · {scope}\n{choice.Description}{privacy}\n\n{report.Format} destination: {report.Folder}",
            counts ? "Export counts" : "Export details")) return;

        string file = IsLive ? ReportWriter.WriteLive(report.Folder, _live.Objects, choice.Kind, report.Format)
            : _workspace.Export(choice.Kind, report.Format);
        if (IsLive) Record("Report exported", "Reports", "Success", counts ? "Aggregate export confirmed." : "Detailed export confirmed.");
        Status = "Report exported";
        _ui.Inform("Report exported", file);
    }

    private void ViewSelected()
    {
        var (target, detail) = Selected switch
        {
            DirectoryUser u => (u.DisplayName, $"Username   {u.Username}\nOrganizational unit   {u.Department}\nDEMO status   {u.Status}\nLast simulated password reset   {u.LastPasswordSimulation?.ToLocalTime().ToString("g") ?? "Never"}\nGroups   {string.Join(", ", _directory.GetInventory().Groups.Where(g => g.Members.Contains(u.Id)).Select(g => g.Name).DefaultIfEmpty("None"))}"),
            Computer c => (c.Name, $"{c.OperatingSystem}\nOrganizational unit   {c.Department}\nLast seen   {c.LastSeen.ToLocalTime():f}\nStatus   {c.Status}"),
            DirectoryGroup g => (g.Name, $"{g.Description}\n\nMembers ({g.MemberCount})\n" + string.Join("\n", _directory.GetInventory().Users.Where(u => g.Members.Contains(u.Id)).Select(u => u.DisplayName))),
            AuditEntry a => (a.Action, $"{a.Timestamp.ToLocalTime():F}\nActor   {a.Actor}\nTarget   {a.Target}\nOutcome   {a.Outcome}\n\n{a.Details}"),
            _ => throw new DirectoryException("Select a record.")
        };
        Record("Record viewed", target, "Success", Current.Name);
        _ui.Inform(target, detail);
    }

    private void Edit(bool editing)
    {
        string target = Current.Name;
        bool saved;
        if (target == "Users")
        {
            var u = editing ? (DirectoryUser?)Selected : null;
            saved = _ui.EditUser(u, input => ValidateSave(() => _directory.SaveUser(u?.Id, input)));
        }
        else
        {
            var g = editing ? (DirectoryGroup?)Selected : null;
            saved = _ui.EditGroup(g, _directory.GetInventory().Users, (name, description, members) =>
            {
                if (g != null && g.Members.Except(members).Any() && !Confirm("Remove group memberships?", g.Name,
                    $"Remove {g.Members.Except(members).Count()} memberships from {g.Name} and save your edits?", "Remove and save"))
                    return false;
                ValidateSave(() => _directory.SaveGroup(g?.Id, name, description, members));
                return true;
            });
        }
        if (!saved) { Cancel(editing ? "Edit record" : "Create record", target); return; }
        Load(true); Status = editing ? "Record updated" : "Record created";
    }
    private void ValidateSave(Action work)
    {
        try { work(); }
        catch (DirectoryException e) { Record("Form validation", Current.Name, "Validation", e.Message); throw; }
    }
    private void Account(AccountAction action)
    {
        if (Selected is not DirectoryUser user) return;
        string label = action == AccountAction.ResetPassword ? "password reset" : action.ToString().ToLowerInvariant();
        if (!Confirm($"DEMO {label}", user.Username, $"Simulate {label} for {user.Username}? This changes only the local demo record.", "Simulate action")) return;
        _directory.PerformAccountAction(user.Id, action); Load(true); Status = $"DEMO {label} completed for {user.Username}";
    }
    private void Import()
    {
        string? file = _ui.ChooseCsv();
        if (file == null) { Cancel("Choose CSV", "Bulk Operations"); return; }
        _preview = null; Load();
        if (new FileInfo(file).Length > 2_000_000) throw new DirectoryException("Choose a CSV smaller than 2 MB.");
        _preview = _directory.PreviewImport(File.ReadAllText(file), Path.GetFileName(file));
        Load(); Status = _preview.CanApply ? "Preview ready for confirmation" : "Review validation errors and choose a corrected CSV";
    }
}
