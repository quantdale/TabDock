using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TabDock.ViewModels;

namespace TabDock.Views;

/// <summary>
/// Modal dialog that lets the user pick top-level windows to capture into a group.
/// </summary>
public partial class CapturePickerWindow : Window
{
    private readonly CapturePickerViewModel _viewModel;

    public CapturePickerResult? Result { get; private set; }

    public CapturePickerWindow(CapturePickerViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        viewModel.GroupingRequested += OnGroupingRequested;
        viewModel.Canceled += OnCanceled;
    }

    private void OnGroupingRequested(object? sender, EventArgs e)
    {
        var selected = _viewModel.Windows.Where(w => w.IsSelected).Select(w => w.Hwnd).ToList();
        Guid targetGroupId = _viewModel.SelectedGroupOption?.Id ?? Guid.Empty;

        Result = new CapturePickerResult
        {
            SelectedHwnds = selected,
            TargetGroupId = targetGroupId,
        };

        DialogResult = true;
        Close();
    }

    private void OnCanceled(object? sender, EventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.GroupingRequested -= OnGroupingRequested;
        _viewModel.Canceled -= OnCanceled;
        base.OnClosed(e);
    }
}
