using ADControlCenter.Core.Live;
using System.Windows.Input;

namespace ADControlCenter.App.Presentation;

public sealed class ConnectionPage
{
    public string Host { get; set; } = "DC01.ad.lab.test";
    public string UserPrincipalName { get; set; } = "";
    public TransportChoice[] Choices { get; } = [new("LDAPS · TLS on 636", Protection.Ldaps), new("Kerberos · signed and sealed on 389", Protection.Kerberos)];
    public TransportChoice Choice { get; set; }
    public ConnectionPage() => Choice = Choices[0];
}
public sealed record TransportChoice(string Name, Protection Protection);

public sealed record OrganizationalBranch(LiveObject Entry)
{
    public List<OrganizationalBranch> Children { get; } = [];
}

public sealed class LiveRecordsPage : Bindable
{
    private readonly LiveObject[] _objects;
    private string _search = "", _parent = "All locations", _state = "All states";
    private BrowseResult _result;
    private LiveObject? _selected;
    public ObjectKind Kind { get; }
    public bool IsHierarchy => Kind == ObjectKind.OrganizationalUnits;
    public IReadOnlyList<OrganizationalBranch> Roots { get; } = [];
    public string[] Locations { get; }
    public string[] States { get; }
    public string Search { get => _search; set { if (Set(ref _search, value)) Move(1); } }
    public string Parent { get => _parent; set { if (value != null && Set(ref _parent, value)) Move(1); } }
    public string State { get => _state; set { if (value != null && Set(ref _state, value)) Move(1); } }
    public BrowseResult Result => _result;
    public string Caption => $"{Result.Total:N0} objects   ·   Page {Result.Page} of {Result.Pages}";
    public LiveObject? Selected { get => _selected; set => Set(ref _selected, value); }
    public ICommand Previous { get; }
    public ICommand Next { get; }
    public LiveRecordsPage(IEnumerable<LiveObject> source, ObjectKind kind)
    {
        _objects = source.Where(o => o.Kind == kind).ToArray();
        Kind = kind;
        if (IsHierarchy)
        {
            var nodes = _objects.ToDictionary(o => o.DistinguishedName, o => new OrganizationalBranch(o), StringComparer.OrdinalIgnoreCase);
            var roots = new List<OrganizationalBranch>();
            foreach (var node in nodes.Values.OrderBy(n => n.Entry.Name))
                if (nodes.TryGetValue(node.Entry.Parent, out var parent)) parent.Children.Add(node);
                else roots.Add(node);
            Roots = roots;
        }
        Locations = ["All locations", .. _objects.Select(o => o.Parent).Distinct(StringComparer.OrdinalIgnoreCase).Order()];
        States = ["All states", .. _objects.Select(o => o.Status).Where(s => s.Length > 0).Distinct().Order()];
        _result = Query(1);
        Previous = new Command(_ => Move(Result.Page - 1), _ => Result.Page > 1);
        Next = new Command(_ => Move(Result.Page + 1), _ => Result.Page < Result.Pages);
    }
    private BrowseResult Query(int page) => LiveQuery.Browse(_objects, Search, Parent == Locations[0] ? "" : Parent, State == States[0] ? "" : State, page);
    private void Move(int page)
    {
        _result = Query(page);
        Selected = null;
        Changed(nameof(Result));
        Changed(nameof(Caption));
        CommandManager.InvalidateRequerySuggested();
    }
}
