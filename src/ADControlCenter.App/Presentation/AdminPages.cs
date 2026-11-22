using System.Security;
using System.Windows.Input;
using ADControlCenter.Core;
using ADControlCenter.Core.Admin;

namespace ADControlCenter.App.Presentation;

public sealed class EmployeeForm : Bindable
{
    private Employee _p;
    public EmployeeForm(Employee p) => _p = p;
    public Employee Value => _p;
    public string FirstName { get => _p.FirstName; set { _p = _p with { FirstName = value }; Changed(); } }
    public string LastName { get => _p.LastName; set { _p = _p with { LastName = value }; Changed(); } }
    public string DisplayName { get => _p.DisplayName; set { _p = _p with { DisplayName = value }; Changed(); } }
    public string Username { get => _p.Username; set { _p = _p with { Username = value }; Changed(); } }
    public string Upn { get => _p.Upn; set { _p = _p with { Upn = value }; Changed(); } }
    public string Department { get => _p.Department; set { _p = _p with { Department = value }; Changed(); } }
    public string Title { get => _p.Title; set { _p = _p with { Title = value }; Changed(); } }
    public string Email { get => _p.Email; set { _p = _p with { Email = value }; Changed(); } }
    public string Description { get => _p.Description; set { _p = _p with { Description = value }; Changed(); } }
}

public sealed class GroupPick(AdminGroup group, bool original, Action changed) : Bindable
{
    public AdminGroup Group => group;
    public bool Original { get; } = original;
    private bool _selected = original;
    private string _reason = original ? "Current membership" : "Manual selection";
    public string Reason { get => _reason; private set => Set(ref _reason, value); }
    public bool Selected { get => _selected; set { if (Set(ref _selected, value)) { Reason = "Manual selection"; changed(); } } }
    public void Template(bool apply, string department)
    {
        Selected = apply;
        if (apply) Reason = department + " department template";
    }
}

public delegate Task<AdminResult> RunAdmin(Func<AdminResult> work);

public sealed class OnboardingPage : Bindable, IDisposable
{
    private readonly AdminWorkflow _workflow;
    private readonly RunAdmin _run;
    private readonly IInteraction _ui;
    private readonly bool _live;
    private readonly AdminCatalog _catalog;
    private SecureString? _secret;
    private int _stage = 1;
    private bool _working;
    private string _error = "";
    public EmployeeForm Form { get; } = new(Employee.Empty);
    public string[] Locations => _catalog.Locations;
    public string Location { get; set; } = "";
    public GroupPick[] Groups { get; }
    public bool Enabled { get; set; }
    public bool ChangeAtLogon { get; set; } = true;
    public int Stage { get => _stage; private set { Set(ref _stage, value); Changed(nameof(Heading)); Changed(nameof(Review)); Changed(nameof(StepLabel)); CommandManager.InvalidateRequerySuggested(); } }
    public string StepLabel => $"Step {Stage} of 6";
    public string Heading => new[] { "Employee information", "Destination OU", "Group membership", "Account settings", "Review and confirm", "Results" }[Stage - 1];
    public string Error { get => _error; private set => Set(ref _error, value); }
    public string ModeNotice => _live ? "LIVE ADMINISTRATION · Confirming will modify Active Directory." : "DEMO MODE · Changes apply to the local fictional directory.";
    public string Review
    {
        get
        {
            var p = Form.Value;
            var groups = Groups.Where(g => g.Selected).Select(g => $"{g.Group.Name} ({g.Reason})");
            return string.Join("\n", [
                $"{p.DisplayName}  ·  {p.Department}  ·  {p.Title}",
                $"Given name: {p.FirstName}     Surname: {p.LastName}",
                $"Username: {p.Username}     UPN: {p.Upn}",
                $"Email: {(p.Email.Length == 0 ? "Unspecified" : p.Email)}",
                $"Description: {(p.Description.Length == 0 ? "Unspecified" : p.Description)}",
                "", $"Destination OU: {Location}", $"Group assignments: {string.Join(", ", groups.DefaultIfEmpty("None"))}",
                "", $"Account after setup: {(Enabled ? "Enabled" : "Disabled")}",
                $"Change password at next logon: {(ChangeAtLogon ? "Required" : "Optional")}",
                "Temporary password supplied"
            ]);
        }
    }
    public AdminResult? Result { get; private set; }
    public ICommand Next { get; }
    public ICommand Back { get; }
    public ICommand Template { get; }
    public ICommand Apply { get; }
    public ICommand Details { get; }
    public OnboardingPage(AdminWorkflow workflow, bool live, IInteraction ui, RunAdmin run)
    {
        (_workflow, _live, _ui, _run) = (workflow, live, ui, run);
        _catalog = workflow.Catalog();
        Groups = _catalog.Groups.Select(g => new GroupPick(g, false, () => Changed(nameof(Review)))).ToArray();
        Next = new Command(_ => Advance(), _ => !_working && Stage < 5);
        Back = new Command(_ => { Error = ""; if (Stage is 4 or 5) ClearPassword(); Stage--; }, _ => !_working && Stage is > 1 and < 6);
        Template = new Command(_ => UseTemplate(), _ => Stage == 3 && !_working);
        Apply = new Command(async _ => await SubmitAsync(), _ => Stage == 5 && !_working);
        Details = new Command(_ => ShowDetails(), _ => Result?.User != Guid.Empty && Result != null);
    }
    public void SetPassword(SecureString value)
    {
        ClearPassword();
        if (Stage == 4) _secret = value.Copy();
    }
    private void ClearPassword() { _secret?.Dispose(); _secret = null; }
    public void Dispose() => ClearPassword();
    private void UseTemplate()
    {
        var match = _catalog.Templates.FirstOrDefault(t => t.Key.Equals(Form.Department, StringComparison.OrdinalIgnoreCase));
        if (match.Value == null) { Error = "No template for this department. Select groups manually."; return; }
        foreach (var group in Groups) group.Template(match.Value.Contains(group.Group.Id), Form.Department);
        Error = "";
    }
    private Hire Draft() => new(Form.Value, Location, Groups.Where(g => g.Selected).Select(g => g.Group.Id).ToArray(), Enabled, ChangeAtLogon);
    public void Advance()
    {
        if (_working || Stage >= 5) return;
        try
        {
            switch (Stage)
            {
                case 1: AdminValidation.Identity(Form.Value, _workflow.Catalog()); break;
                case 2: AdminValidation.Location(Location, _catalog); break;
                case 3: AdminValidation.Groups(Draft().Groups, _catalog); break;
                case 4: AdminValidation.Password(_secret); break;
            }
            Error = ""; Stage++;
        }
        catch (DirectoryException e) { Error = e.Message; }
    }
    public async Task SubmitAsync()
    {
        if (_working || Stage != 5 || _secret == null) return;
        _working = true;
        try
        {
            if (!_ui.Confirm(_live ? "Create live lab account?" : "Create demo account?", ModeNotice + "\n\n" + Review, "Confirm creation"))
            { ClearPassword(); Stage = 4; Error = "Cancelled. Re-enter the temporary password to continue."; return; }
            var hire = Draft();
            var secret = _secret;
            Result = await _run(() => _workflow.Onboard(hire, secret));
            Changed(nameof(Result)); Stage = 6;
        }
        catch (Exception e) { Error = e is DirectoryException ? e.Message : "Operation interrupted. Check activity before continuing."; Stage = 4; }
        finally { ClearPassword(); _working = false; CommandManager.InvalidateRequerySuggested(); }
    }
    private void ShowDetails()
    {
        var user = _workflow.Catalog().Users.FirstOrDefault(u => u.Id == Result?.User);
        _ui.Inform("Account details", user == null ? "Refresh the inventory to view the account." : user.Profile.Attributes + "\nOU: " + user.Location + "\nEnabled: " + user.Enabled);
    }
}

public sealed class AccountAdminPage : Bindable
{
    private readonly AdminWorkflow _workflow;
    private readonly IInteraction _ui;
    private readonly RunAdmin _run;
    private readonly bool _live;
    private AdminCatalog _catalog;
    private AdminPerson? _user;
    private string _query = "", _groupQuery = "", _message = "";
    private bool _working;
    public EmployeeForm Form { get; private set; } = new(Employee.Empty);
    public bool Enabled { get; set; }
    public string Query { get => _query; set { Set(ref _query, value); Changed(nameof(Users)); } }
    public string GroupQuery { get => _groupQuery; set { Set(ref _groupQuery, value); Changed(nameof(VisibleGroups)); } }
    public IEnumerable<AdminPerson> Users => _catalog.Users.Where(u => new[] { u.Profile.Username, u.Profile.DisplayName, u.Profile.Department, u.Location }
        .Any(s => s.Contains(Query, StringComparison.OrdinalIgnoreCase)));
    public IEnumerable<GroupPick> VisibleGroups => Picks.Where(g => g.Group.Name.Contains(GroupQuery, StringComparison.OrdinalIgnoreCase));
    public GroupPick[] Picks { get; private set; } = [];
    public AdminPerson? User { get => _user; set { if (Set(ref _user, value)) Select(); } }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string MembershipChanges => string.Join("\n", Picks.Where(g => g.Original != g.Selected).Select(g => (g.Selected ? "Add   " : "Remove   ") + g.Group.Name).DefaultIfEmpty("No membership changes."));
    public string CurrentMemberships => string.Join(", ", Picks.Where(g => g.Original).Select(g => g.Group.Name).DefaultIfEmpty("None"));
    public ICommand Save { get; }
    public ICommand Members { get; }
    public AdminResult? Result { get; private set; }
    public AccountAdminPage(AdminWorkflow workflow, bool live, IInteraction ui, RunAdmin run)
    {
        (_workflow, _live, _ui, _run) = (workflow, live, ui, run); _catalog = workflow.Catalog();
        Select();
        Save = new Command(async _ => await SaveAsync(), _ => User != null && !_working);
        Members = new Command(p => { if (p is AdminGroup group) _ui.Inform(group.Name + " · " + group.Scope, string.Join("\n", group.Members.DefaultIfEmpty("No direct members."))); });
    }
    private void Select()
    {
        Form = new(User?.Profile ?? Employee.Empty); Enabled = User?.Enabled ?? false;
        Picks = _catalog.Groups.Select(g => new GroupPick(g, User?.Groups.Contains(g.Id) == true, () => Changed(nameof(MembershipChanges)))).ToArray();
        foreach (string name in new[] { nameof(Form), nameof(Enabled), nameof(Picks), nameof(VisibleGroups), nameof(CurrentMemberships), nameof(MembershipChanges) }) Changed(name);
    }
    public async Task SaveAsync()
    {
        var user = User;
        if (user == null || _working) return;
        _working = true;
        try
        {
            var profile = Form.Value;
            if (profile != user.Profile) AdminValidation.EditedFields(user.Profile, profile);
            var ids = Picks.Where(g => g.Selected).Select(g => g.Group.Id).ToArray();
            var enabled = Enabled;
            string preview = $"{user.Label}\nOU: {user.Location}\n{MembershipChanges}\nEnabled: {user.Enabled} → {enabled}";
            if (profile != user.Profile) preview += "\nBefore: " + user.Profile.Attributes + "\nAfter: " + profile.Attributes;
            if (profile == user.Profile && enabled == user.Enabled && Picks.All(g => g.Selected == g.Original)) { Message = "There are no changes to apply."; return; }
            if (!_ui.Confirm(_live ? "Modify live Active Directory?" : "Apply demo changes?", preview, "Apply changes")) { Message = "Cancelled."; return; }
            Result = await _run(() => _workflow.Update(user, profile, enabled, ids));
            Message = Result.Summary;
            _catalog = _workflow.Catalog(); Changed(nameof(Users));
            _user = _catalog.Users.FirstOrDefault(u => u.Id == user.Id); Changed(nameof(User)); Select();
        }
        catch (Exception e) { Message = e is DirectoryException ? e.Message : "Operation interrupted. Review activity before continuing."; }
        finally { _working = false; CommandManager.InvalidateRequerySuggested(); }
    }
}

public sealed record AdminHistoryPage(IReadOnlyList<AdminEvent> Entries);
