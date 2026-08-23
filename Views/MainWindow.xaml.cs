using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
        GroupsListView.MouseDoubleClick += OnGroupsListDoubleClick;
        GroupsListView.PreviewKeyDown += OnGroupsListKeyDown;
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
        if (e.Key != Key.Enter)
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
