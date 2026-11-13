using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabDock.Infrastructure;
using TabDock.Models;
using TabDock.ViewModels;

namespace TabDock.Views;

/// <summary>
/// The application's main launcher/control window. A row is a live affordance:
/// double-clicking it (or pressing Enter on the selection) opens that group's
/// container through the registry-first OpenContainer path.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel) : this(viewModel, null)
    {
    }

    internal MainWindow(MainViewModel viewModel, ResourceDictionary? resources)
    {
        if (resources != null)
            Resources.MergedDictionaries.Add(resources);
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
        // Standard system chrome would otherwise render with the user's light
        // theme/accent; keep the caption and border in the product palette.
        SourceInitialized += (_, _) => WindowChromeTheme.ApplyDarkChrome(this);
        GroupsListView.MouseDoubleClick += OnGroupsListDoubleClick;
        GroupsListView.PreviewKeyDown += OnGroupsListKeyDown;
        MotionProfile.PlayEntrance((FrameworkElement)Content);
    }

    private void OnGroupsListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a row click opens: a double-click on blank list space must not
        // re-open whatever happens to be selected.
        if (FindRowGroup(e.OriginalSource) is Group group)
            ViewModel.OpenSelectedGroupCommand.Execute(group);
    }

    private void OnGroupsListKeyDown(object sender, KeyEventArgs e)
    {
        // A nested Open button owns its row-specific command parameter. Only
        // navigation-container Enter should fall back to the selected workspace.
        if (e.Key != Key.Enter || (e.OriginalSource is not ListViewItem
            && !ReferenceEquals(e.OriginalSource, GroupsListView)))
            return;
        if (ViewModel.SelectedGroup is Group group)
        {
            e.Handled = true;
            ViewModel.OpenSelectedGroupCommand.Execute(group);
        }
    }

    private Group? FindRowGroup(object source)
    {
        for (DependencyObject? current = source as DependencyObject; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.ListViewItem item)
                return item.Content as Group;
        }
        return null;
    }
}
