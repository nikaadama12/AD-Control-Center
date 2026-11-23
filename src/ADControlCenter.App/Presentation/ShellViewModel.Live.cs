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
    private readonly Func<LabPolicy> _loadPolicy;
    private string? _liveTheme;
    private readonly IAdminAudit _liveJournal;
    private readonly AdminWorkflow? _demoAdmin;
    private AdminWorkflow? _liveAdmin;
    private AdminWorkflow? ActiveWorkflow=>IsLive?_liveAdmin:_demoAdmin;
    public bool IsAdministration=>IsLive && _liveAdmin!=null;
    private bool IsManagementPage => Current.Name is "Onboarding" or "Account Administration" or "Administration History";
    public bool ShowAuthorization => IsLive && !IsAdministration && IsManagementPage;
    public string ModeExplanation => IsAdministration
        ? "Approved lab scope only. The controller checks permissions on each confirmed change."
        : IsLive ? "Live Read-Only Mode — enable authorized administration to make changes."
        : "Changes affect the fictional local directory.";
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
    private void NotifySession()
    {
        foreach (string property in new[] { nameof(IsLive), nameof(CanConnect), nameof(CanDisconnect), nameof(Busy),
            nameof(IsAdministration), nameof(ModeExplanation), nameof(ShowAuthorization), nameof(ModeLabel), nameof(DirectoryLabel), nameof(DirectoryType), nameof(OperatorLabel), nameof(OperatorDetail), nameof(Initials), nameof(Subtitle) })
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
            Current = Navigation[0];
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
        finally { _operation = null; NotifySession(); CheckAdministrationPolicy(); }
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
            "Onboarding" => new LiveRecordsPage(_live.Objects, ObjectKind.OrganizationalUnits),
            "Account Administration" => new LiveRecordsPage(_live.Objects, ObjectKind.Users),
            "Administration History" => new AdminHistoryPage(_sessionHistory, "Administration activity from this connection · UTC"),
            "Bulk Operations" => new SectionNotice("CSV import is available in Demo Mode",
                "Live connections support individual, reviewed changes in the administration pages. Disconnect in Connection Manager to import fictional users."),
            "Settings" => new LiveAppearancePage(_liveTheme ?? _workspace.Settings.Theme),
            "Reports" => new ReportsPage(PrivateExports, true),
            "Connection Manager" => new ConnectionPage { Host = _live.Host },
            "Audit Logs" => new RecordsPage("Audit Logs", new([], [], []), _events.ToArray(), AuditViewChange),
            _ => new LiveRecordsPage(_live.Objects, Current.Name == "Organizational Units" ? ObjectKind.OrganizationalUnits : Enum.Parse<ObjectKind>(Current.Name))
        };
    }

    private IReadOnlyList<AdminEvent> _sessionHistory = [];
    public async Task EnableAdministrationAsync()
    {
        if (!IsLive || Busy || IsAdministration || !_ui.Confirm("Enable live administration?",
            "This connection will be allowed to submit confirmed changes in the approved AppTesting OUs as the delegated lab operator.","Enable administration")) return;
        AdminWorkflow? workflow=null;
        await NetworkAsync("Verifying delegated identity and lab scope…", token =>
        {
            token.ThrowIfCancellationRequested();
            var backend = _live.EnableAdministration(_loadPolicy);
            if (token.IsCancellationRequested)
            {
                _live.DisableAdministration();
                token.ThrowIfCancellationRequested();
            }
            workflow = new(backend, _liveJournal);
        }, () => { _liveAdmin = workflow; NotifySession(); Load(); Status = "Live administration enabled for this connection."; });
    }

    public void CheckAdministrationPolicy()
    {
        if (IsAdministration && !Busy && !_live.AdministrationCurrent)
            EndAdministration("Administration approval changed or expired. Review the policy and authorize again.");
    }

    private void EndAdministration(string message)
    {
        _live.DisableAdministration();
        _liveAdmin = null;
        NotifySession();
        Status = message;
        Safe("Administration ended", () => Load());
    }

    private async Task<AdminResult> RunAdminAsync(AdminWorkflow owner, Func<AdminResult> work)
    {
        if (Busy || !ReferenceEquals(owner, ActiveWorkflow)) throw new DirectoryException("Administration is unavailable.");
        using var operation=new CancellationTokenSource();
        _operation=operation; Status="Applying reviewed operations…"; NotifySession();
        try
        {
            var result=await Task.Run(work);
            Status=result.Success?"Reviewed changes completed.":"Review the operation results before continuing.";
            if(IsLive)
            {
                try
                {
                    _sessionHistory = owner.History().Where(e => e.OperationId == result.OperationId)
                        .Concat(_sessionHistory).ToArray();
                    await Task.Run(()=>_live.Refresh(operation.Token));
                    _loaded=DateTimeOffset.Now;
                }
                catch (Exception) { Status+=" Inventory refresh failed. Refresh before making another change."; }
            }
            return result;
        }
        finally { _operation=null; NotifySession(); CheckAdministrationPolicy(); }
    }

    private void ExecuteLive(string action)
    {
        switch (action)
        {
            case "EnableAdministration": _=EnableAdministrationAsync(); break;
            case "EndAdministration": EndAdministration("Live read-only mode restored."); break;
            case "ApplyAppearance" when Page is LiveAppearancePage appearance:
                _ui.ApplyTheme(appearance.Theme);
                _liveTheme = appearance.Theme;
                Status = "Appearance applied to this connection.";
                break;
            case "Disconnect":
                _liveAdmin=null;
                _live.Dispose();
                _events.Clear();
                _sessionHistory = [];
                _liveTheme = null;
                _ui.ApplyTheme(_workspace.Settings.Theme);
                Current = Navigation[0];
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
