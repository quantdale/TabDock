using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.ViewModels;

/// <summary>
/// View-model for the capture-picker dialog.
/// Lists top-level windows and lets the user choose a target group.
/// </summary>
public sealed class CapturePickerViewModel : ViewModelBase
{
    private readonly GroupManager _manager;
    private readonly IconService _icons;
    private GroupOption? _selectedGroupOption;

    public ObservableCollection<WindowInfo> Windows { get; } = new();
    public ObservableCollection<GroupOption> Groups { get; } = new();

    public GroupOption? SelectedGroupOption
    {
        get => _selectedGroupOption;
        set => SetProperty(ref _selectedGroupOption, value);
    }

    public bool HasSelection
    {
        get
        {
            foreach (var w in Windows)
                if (w.IsSelected)
                    return true;
            return false;
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand GroupSelectedCommand { get; }
    public ICommand CancelCommand { get; }

    public event EventHandler? GroupingRequested;
    public event EventHandler? Canceled;

    public CapturePickerViewModel(GroupManager manager, IconService icons)
    {
        _manager = manager;
        _icons = icons;

        RefreshCommand = new RelayCommand(_ => Refresh());
        GroupSelectedCommand = new RelayCommand(_ => GroupingRequested?.Invoke(this, EventArgs.Empty), _ => HasSelection);
        CancelCommand = new RelayCommand(_ => Canceled?.Invoke(this, EventArgs.Empty));

        Refresh();
    }

    public void Refresh()
    {
        Windows.Clear();
        Groups.Clear();

        Groups.Add(new GroupOption(Guid.Empty, "<New group>"));
        foreach (var g in _manager.Groups)
        {
            Groups.Add(new GroupOption(g.Id, g.Name));
        }
        SelectedGroupOption = Groups.FirstOrDefault();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;
            if (_manager.IsOwnWindow(hwnd))
                return true;

            // Cloaked windows (suspended UWP apps, hidden ApplicationFrameHost
            // ghosts) are reported visible by IsWindowVisible but aren't actually
            // on screen; capturing one produces a tab with nothing behind it.
            int hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out bool cloaked, sizeof(uint));
            if (hr == 0 && cloaked)
                return true;

            nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
            if (((long)exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                return true;

            string? title = NativeMethods.GetWindowTextString(hwnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            string? exe = _icons.GetProcessImagePath(pid);

            var info = new WindowInfo(hwnd, title, exe)
            {
                Icon = _icons.GetWindowIcon(hwnd),
            };
            info.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasSelection));
                ((RelayCommand)GroupSelectedCommand).RaiseCanExecuteChanged();
            };
            Windows.Add(info);
            return true;
        }, IntPtr.Zero);
    }

    public sealed class WindowInfo : ViewModelBase
    {
        private bool _isSelected;
        private ImageSource? _icon;

        public IntPtr Hwnd { get; }
        public string Title { get; }
        public string ExePath { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public ImageSource? Icon
        {
            get => _icon;
            set => SetProperty(ref _icon, value);
        }

        public WindowInfo(IntPtr hwnd, string title, string? exePath)
        {
            Hwnd = hwnd;
            Title = title;
            ExePath = exePath ?? string.Empty;
        }
    }

    public sealed class GroupOption
    {
        public Guid Id { get; }
        public string Name { get; }

        public GroupOption(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }
}
