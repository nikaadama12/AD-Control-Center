using System.Net;
using System.Security;
using System.Windows.Input;
using ADControlCenter.Core;
using ADControlCenter.Core.Live;
using ADControlCenter.Core.Admin;

namespace ADControlCenter.App.Presentation;

public sealed partial class ShellViewModel
{
    private readonly LiveSession _live;
    private readonly List<AuditEntry> _events = [];
    private CancellationTokenSource? _operation;
    private DateTimeOffset _loaded;
    private static readonly NavItem Manager = new("Connection Manager", "Shield");
    private static readonly NavItem[] LiveNavigation = [new("Dashboard","Dashboard"), new("Users","Users"), new("Computers","Computers"),
        new("Groups","Groups"), new("Organizational Units","Directory"), new("Reports","Reports"), new("Audit Logs","Audit"), Manager];
    private static readonly NavItem[] AdminNavigation=[new("Onboarding","Users"),new("Account administration","Groups"),new("Administration history","Audit")];
    private readonly AdminWorkflow? _demoAdmin;
    private AdminWorkflow? _liveAdmin;
    private AdminWorkflow? ActiveWorkflow=>IsLive?_liveAdmin:_demoAdmin;
    public bool IsAdministration=>IsLive && _liveAdmin!=null;
    public IEnumerable<NavItem> Navigation => (IsLive ? LiveNavigation.AsEnumerable() : DemoNavigation.Append(Manager))
        .Concat(ActiveWorkflow==null?[]:AdminNavigation).ToArray();
    public bool IsLive => _live.Connected;
    public bool CanConnect => !Busy && !IsLive;
    public bool CanDisconnect => !Busy && IsLive;
    public bool Busy => _operation != null;
    public string ModeLabel => IsAdministration ? "LIVE ADMINISTRATION MODE" : IsLive ? "LIVE READ-ONLY MODE" : "DEMO MODE";
    public string DirectoryLabel => IsLive ? _live.Host : "northstar.example";
    public string DirectoryType => IsLive ? "DOMAIN CONTROLLER" : "LOCAL DIRECTORY";
    public string OperatorLabel => IsAdministration ? _liveAdmin!.Actor : IsLive ? "Directory reader" : "Demo Operator";
    public string OperatorDetail => IsLive ? "Authenticated session" : "Local administrator demo";
    public string Initials => IsAdministration ? "AD" : IsLive ? "RO" : "DO";
    private string PrivateExports { get; }
    private string _previousMode = "DEMO MODE";
    private void NotifySession()
    {
        if (_previousMode != ModeLabel)
        {
            _previousMode = ModeLabel;
            Changed(nameof(Navigation));
        }
        foreach (string property in new[] { nameof(IsLive), nameof(CanConnect), nameof(CanDisconnect), nameof(Busy),
            nameof(IsAdministration), nameof(ModeLabel), nameof(DirectoryLabel), nameof(DirectoryType), nameof(OperatorLabel), nameof(OperatorDetail), nameof(Initials), nameof(Subtitle) })
            Changed(property);
        CommandManager.InvalidateRequerySuggested();
    }

    public async Task ConnectAsync(ConnectionPage form, SecureString password, bool testOnly)
    {
        if (!CanConnect || Page != form) return;
        string upn = form.UserPrincipalName.Trim();
        int split = upn.LastIndexOf('@');
        if (split <= 0 || split == upn.Length - 1 || password.Length == 0)
        {
            Status = "Enter a domain UPN and password.";
            return;
        }
        var controller = new Controller(form.Host.Trim(), form.Choice.Protection);
        var credential = controller.Protection == Protection.Ldaps ? new NetworkCredential(upn, password)
            : new NetworkCredential(upn[..split], password, upn[(split + 1)..]);
        await NetworkAsync(testOnly ? "Testing authenticated connection…" : "Authenticating and loading directory…", token =>
            _live.Open(controller, credential, testOnly, token), () =>
        {
            if (testOnly) { Status = "Authenticated test passed · RootDSE read successfully. Re-enter the password to connect."; return; }
            _loaded = DateTimeOffset.Now;
            _preview = null;
            _events.Clear();
            Record("Connected", "Directory", "Success", "Authenticated read-only session established.");
            Current = LiveNavigation[0];
            Load();
            Status = $"Live snapshot loaded at {_loaded:T}";
        });
    }

    private async Task NetworkAsync(string progress, Action<CancellationToken> work, Action complete)
    {
        if (Busy) return;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        Status = progress;
        NotifySession();
        try { await Task.Run(() => work(cancellation.Token)); complete(); }
        catch (OperationCanceledException) { Status = "Directory request cancelled."; }
        catch (Exception ex)
        {
            string message = ex is DirectoryException ? ex.Message : "Secure directory request failed. Check DNS, transport policy, permissions and connectivity.";
            Status = IsLive ? $"{message} Showing snapshot from {_loaded:T}." : message;
            if (IsLive) Record("Directory request", "Directory", "Failed", "The previous snapshot was retained.");
        }
        finally { _operation = null; NotifySession(); }
    }

    private Task RefreshLiveAsync() => NetworkAsync("Refreshing live directory…", _live.Refresh, () =>
    {
        _loaded = DateTimeOffset.Now;
        Record("Snapshot refreshed", "Directory", "Success", "Completed all directory pages.");
        Load();
        Status = $"Live snapshot refreshed at {_loaded:T}";
    });
    public void CancelOperation() => _operation?.Cancel();
    public void CloseSession() { if(Page is IDisposable disposable) disposable.Dispose(); _liveAdmin=null; _live.Dispose(); }

    private void Record(string action, string target, string outcome, string detail)
    {
        if (IsLive) _events.Insert(0, new(DateTimeOffset.UtcNow, "Directory reader", action, target, outcome, detail));
        else _workspace.Record(action, target, outcome, detail);
    }

    private void LoadLive()
    {
        Page = Current.Name switch
        {
            "Dashboard" => new DashboardPage(_live.Objects, _events),
            "Reports" => new ReportsPage(PrivateExports, true),
            "Connection Manager" => new ConnectionPage { Host = _live.Host },
            "Audit Logs" => new RecordsPage("Audit Logs", new([], [], []), _events.ToArray(), AuditViewChange),
            _ => new LiveRecordsPage(_live.Objects, Current.Name == "Organizational Units" ? ObjectKind.OrganizationalUnits : Enum.Parse<ObjectKind>(Current.Name))
        };
    }

    private async Task EnableAdministrationAsync()
    {
        if (!IsLive || Busy || IsAdministration || !_ui.Confirm("Enable live administration?",
            "This connection will be allowed to submit confirmed changes in the approved AppTesting OUs as the delegated lab operator.","Enable administration")) return;
        AdminWorkflow? workflow=null;
        await NetworkAsync("Verifying delegated identity and lab scope…", token =>
        {
            var backend=_live.EnableAdministration(LabPolicy.Load());
            var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AD Control Center","Administration","audit.jsonl");
            workflow=new(backend,new FileAdminAudit(path,live:true));
        }, () => { _liveAdmin=workflow; Status="Live administration enabled for this connection."; });
    }

    private async Task<AdminResult> RunAdminAsync(Func<AdminResult> work)
    {
        if (Busy || ActiveWorkflow==null) throw new DirectoryException("Administration is unavailable.");
        using var operation=new CancellationTokenSource();
        _operation=operation; Status="Applying reviewed operations…"; NotifySession();
        try
        {
            var result=await Task.Run(work);
            Status=result.Success?"Reviewed changes completed.":"Review the operation results before continuing.";
            if(IsLive)
            {
                try { await Task.Run(()=>_live.Refresh(operation.Token)); _loaded=DateTimeOffset.Now; }
                catch (Exception) { Status+=" Inventory refresh failed. Refresh before making another change."; }
            }
            return result;
        }
        finally { _operation=null; NotifySession(); }
    }

    private void ExecuteLive(string action)
    {
        switch (action)
        {
            case "EnableAdministration": _=EnableAdministrationAsync(); break;
            case "EndAdministration": _live.DisableAdministration(); _liveAdmin=null; NotifySession(); Load(); Status="Live read-only mode restored."; break;
            case "Disconnect":
                _liveAdmin=null;
                _live.Dispose();
                _events.Clear();
                Current = DemoNavigation[0];
                NotifySession();
                Load();
                Status = "Disconnected · Demo workspace restored";
                break;
            case "Refresh": _ = RefreshLiveAsync(); break;
            case "View" when Page is LiveRecordsPage { Selected: { } row }:
                Record("Record viewed", Current.Name, "Success", "Opened live record details.");
                _ui.Inform(row.Name, row.Details); break;
            case "View" when Page is RecordsPage: ViewSelected(); break;
            case "Export" when Page is ReportsPage report:
                ExportReport(report); break;
        }
    }
}
