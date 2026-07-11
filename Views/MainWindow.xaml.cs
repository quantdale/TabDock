using System.Windows;
using TabDock.ViewModels;

namespace TabDock.Views;

/// <summary>
/// The application's main launcher/control window.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
