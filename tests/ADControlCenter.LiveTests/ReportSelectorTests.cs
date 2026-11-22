using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ADControlCenter.App.Presentation;
using ADControlCenter.App.Views;
using ADControlCenter.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADControlCenter.LiveTests;

[TestClass]
public sealed class ReportSelectorTests
{
    [STATestMethod]
    public void ThemedSelectorRendersFriendlyLabelsAndBindsTypedDatasets()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/ADControlCenter;component/Views/Theme.xaml", UriKind.Relative)
        });
        var view = new ReportsView();
        var window = new Window { Content = view, Width = 700, Height = 500, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show();
            var labels = new (string Name, ReportKind Kind)[] {
                ("User accounts", ReportKind.Users), ("Security groups", ReportKind.SecurityGroups), ("Computers", ReportKind.Computers),
                ("Organizational units", ReportKind.OrganizationalUnits), ("Counts Only (Privacy-Safe)", ReportKind.CountsOnly)
            };
            foreach (bool live in new[] { false, true })
            {
                var model = new ReportsPage("Example", live);
                view.DataContext = model;
                window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                var selector = Descendants(view).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Directory dataset");
                Assert.AreEqual(labels.Length, selector.Items.Count);
                for (int i = 0; i < labels.Length; i++)
                {
                    selector.IsDropDownOpen = true;
                    window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                    var item = (ComboBoxItem)selector.ItemContainerGenerator.ContainerFromIndex(i);
                    item.UpdateLayout();
                    Assert.IsTrue(Descendants(item).OfType<TextBlock>().Any(t => t.Text == labels[i].Name), "Dropdown label");
                    selector.SelectedItem = item.Content;
                    selector.IsDropDownOpen = false;
                    window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                    var displayed = Descendants(selector).OfType<TextBlock>().Select(t => t.Text).ToArray();
                    CollectionAssert.Contains(displayed, labels[i].Name, "Collapsed selector must display the friendly name.");
                    Assert.IsFalse(displayed.Any(s => s.Contains("ReportChoice {")));
                    Assert.AreEqual(labels[i].Kind, model.Selected.Kind);
                }
            }
        }
        finally { window.Close(); application.Shutdown(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
