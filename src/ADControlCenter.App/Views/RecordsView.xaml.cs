using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ADControlCenter.App.Presentation;

namespace ADControlCenter.App.Views;

public partial class RecordsView : UserControl
{
    public RecordsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => { if (e.NewValue is RecordsPage page) BuildColumns(page.Name); };
    }
    private void BuildColumns(string page)
    {
        Table.Columns.Clear();
        (string Header, string Path, double Width, string? Format)[] columns = page switch
        {
            "Users" => [("Display name", "DisplayName", 1.25, null), ("Username", "Username", 1.2, null), ("Organizational unit", "Department", 1.2, null), ("DEMO status", "Status", .85, null)],
            "Computers" => [("Hostname", "Name", 1, null), ("Operating system", "OperatingSystem", 1.2, null), ("Unit", "Department", .95, null), ("Last seen", "LastSeen.LocalDateTime", .95, "{0:yyyy-MM-dd}"), ("Status", "Status", .65, null)],
            "Groups" => [("Group name", "Name", 1.1, null), ("Description", "Description", 2, null), ("Members", "MemberCount", .7, null)],
            _ => [("Timestamp", "Timestamp.LocalDateTime", 1.2, "{0:MM-dd HH:mm:ss}"), ("Action", "Action", 1.4, null), ("Actor", "Actor", 1, null), ("Target", "Target", 1.2, null), ("Outcome", "Outcome", .8, null), ("Details", "Details", 1.7, null)]
        };
        foreach (var c in columns)
        {
            DataGridColumn column = c.Path == "Status"
                ? new DataGridTemplateColumn { CellTemplate = (DataTemplate)Resources["StatusCell"] }
                : new DataGridTextColumn { Binding = new Binding(c.Path) { StringFormat = c.Format } };
            column.Header = c.Header;
            column.SortMemberPath = c.Path;
            column.Width = new DataGridLength(c.Width, DataGridLengthUnitType.Star);
            column.MinWidth = 62;
            Table.Columns.Add(column);
        }
    }
    private void OpenRecord(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject element || ItemsControl.ContainerFromElement(Table, element) is not DataGridRow) return;
        if (Window.GetWindow(this)?.DataContext is ShellViewModel shell && shell.Run.CanExecute("View")) shell.Run.Execute("View");
    }
    private void Sorted(object sender, DataGridSortingEventArgs e)
    {
        if (DataContext is RecordsPage page) page.RecordSort(e.Column.Header.ToString()!);
    }
}
