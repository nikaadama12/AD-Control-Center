using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ADControlCenter.Core;
using ADControlCenter.App.Presentation;

namespace ADControlCenter.App.Views;

public sealed class Interaction : IInteraction
{
    private static readonly Dictionary<string, string> Light = new()
    {
        ["Canvas"] = "#F2F5F3",
        ["Sidebar"] = "#E7EDE8",
        ["Panel"] = "#FFFFFF",
        ["Field"] = "#F4F7F4",
        ["Hover"] = "#DFE8E1",
        ["Line"] = "#C4D0C8",
        ["Text"] = "#18251F",
        ["Muted"] = "#46594D",
        ["Subtle"] = "#506555",
        ["Accent"] = "#A5DB74",
        ["Tint"] = "#D9EACD",
        ["AccentText"] = "#315C21",
        ["Danger"] = "#923B2D"
    };
    public void ApplyTheme(string theme)
    {
        WindowChrome.IsDark = theme == "Dark";
        var resources = Application.Current.Resources;
        foreach (var (key, color) in Light)
        {
            if (theme == "Dark") resources.Remove(key);
            else resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
        foreach (Window window in Application.Current.Windows) WindowChrome.Apply(window);
    }
    private static Window Owner => Application.Current.Windows.OfType<Window>().LastOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
    public string? ChooseCsv()
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv", Title = "Choose fictional user CSV" };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }
    public string? ChooseFolder(string current)
    {
        var dialog = new OpenFolderDialog { Title = "Choose export directory", InitialDirectory = Directory.Exists(current) ? current : "" };
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public bool Confirm(string title, string description, string accept)
    {
        var form = new FormWindow(Owner, title);
        form.AddText(description);
        return form.ShowForm(accept, () => true, cancel: true, safeDefault: true);
    }
    public void Inform(string title, string description)
    {
        var form = new FormWindow(Owner, title);
        form.AddText(description, selectable: true);
        form.ShowForm("Close", () => true, cancel: false);
    }

    public bool EditUser(DirectoryUser? user, Action<UserInput> save)
    {
        var form = new FormWindow(Owner, user == null ? "Create demo user" : "Edit demo user");
        var name = form.AddField("Display name", user?.DisplayName ?? "", 80);
        var username = form.AddField("Username", user?.Username ?? "", 32);
        var department = form.AddField("Organizational unit", user?.Department ?? "", 64);
        return form.ShowForm(user == null ? "Create user" : "Save changes", () =>
        {
            save(new(name.Text, username.Text, department.Text));
            return true;
        });
    }
    public bool EditGroup(DirectoryGroup? group, IReadOnlyList<DirectoryUser> users, Func<string, string, IReadOnlyList<Guid>, bool> save)
    {
        var form = new FormWindow(Owner, group == null ? "Create demo group" : "Edit demo group");
        var name = form.AddField("Group name", group?.Name ?? "", 64);
        var description = form.AddField("Description", group?.Description ?? "", 240);
        var search = form.AddField("Filter members", "", 80);
        var selected = (group?.Members ?? []).ToHashSet();
        var panel = new StackPanel();
        var count = form.AddText("");
        void UpdateCount() => count.Text = $"Members selected   {selected.Count}";
        void Populate()
        {
            panel.Children.Clear();
            foreach (var u in users.Where(u => (u.DisplayName + " " + u.Username).Contains(search.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(u => u.DisplayName))
            {
                var box = new CheckBox { Content = $"{u.DisplayName}   ·   {u.Username}", IsChecked = selected.Contains(u.Id), Margin = new(1, 4, 0, 4) };
                box.SetResourceReference(Control.ForegroundProperty, "Text");
                box.Checked += (_, _) => { selected.Add(u.Id); UpdateCount(); };
                box.Unchecked += (_, _) => { selected.Remove(u.Id); UpdateCount(); };
                panel.Children.Add(box);
            }
        }
        UpdateCount(); Populate(); search.TextChanged += (_, _) => Populate();
        form.Body.Children.Add(new ScrollViewer { Content = panel, Height = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return form.ShowForm(group == null ? "Create group" : "Save group", () => save(name.Text, description.Text, selected.ToArray()));
    }
}

internal sealed class FormWindow : Window
{
    internal StackPanel Body { get; } = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new(0, 10, 0, 0) };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 15, 0, 0) };
    private TextBox? _first;
    internal FormWindow(Window owner, string title)
    {
        Style = (Style)FindResource(typeof(Window));
        SourceInitialized += (_, _) => WindowChrome.Apply(this);
        Owner = owner;
        Title = title; Width = Math.Min(480, SystemParameters.WorkArea.Width);
        MaxHeight = Math.Min(owner.ActualHeight > 0 ? owner.ActualHeight : 720, SystemParameters.WorkArea.Height) - 24;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var layout = new DockPanel { Margin = new(22) };
        var head = new StackPanel { Margin = new(0, 0, 0, 14) };
        var badge = new TextBlock { Text = Application.Current.MainWindow?.DataContext is ShellViewModel shell ? shell.ModeLabel : "DEMO MODE", FontFamily = new("Consolas"), FontSize = 11 };
        badge.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        head.Children.Add(badge);
        head.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(head, Dock.Top); layout.Children.Add(head);
        var foot = new StackPanel();
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); foot.Children.Add(_error); foot.Children.Add(_buttons);
        DockPanel.SetDock(foot, Dock.Bottom); layout.Children.Add(foot);
        layout.Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
    }

    internal TextBox AddField(string label, string initial, int max)
    {
        Body.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 4) });
        var input = new TextBox { Text = initial, MaxLength = max };
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        Body.Children.Add(input); _first ??= input;
        return input;
    }
    internal TextBlock AddText(string text, bool selectable = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 4), LineHeight = 20 };
        if (selectable) Body.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0) });
        else Body.Children.Add(block);
        return block;
    }

    internal bool ShowForm(string action, Func<bool> save, bool cancel = true, bool safeDefault = false)
    {
        if (cancel)
        {
            var back = new Button { Content = "Cancel", IsCancel = true, Margin = new(0, 0, 8, 0), IsDefault = safeDefault };
            back.Click += (_, _) => DialogResult = false; _buttons.Children.Add(back);
            if (safeDefault) ContentRendered += (_, _) => back.Focus();
        }
        var ok = new Button { Content = action, IsDefault = !safeDefault };
        ok.SetResourceReference(StyleProperty, "Primary");
        ok.Click += (_, _) =>
        {
            try { if (save()) DialogResult = true; }
            catch (Exception ex) when (ex is DirectoryException or IOException or UnauthorizedAccessException)
            { _error.Text = ex.Message; _error.Visibility = Visibility.Visible; }
        };
        _buttons.Children.Add(ok);
        if (_first != null) ContentRendered += (_, _) => _first.Focus();
        return ShowDialog() == true;
    }
}
