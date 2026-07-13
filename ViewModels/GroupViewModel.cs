using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.ViewModels;

public sealed class GroupViewModel : ViewModelBase
{
    private readonly Group _group;
    private readonly GroupManager _manager;
    private readonly WindowCaptureService _capture;
    private readonly IconService _icons;
    private TabViewModel? _activeTab;
    private bool _isRenaming;

    public Group Model => _group;

    public string Name
    {
        get => _group.Name;
        set => _group.Name = value;
    }

    public string AccentColor
    {
        get => _group.AccentColor;
        set => _group.AccentColor = value;
    }

    public Brush AccentBrush => (Brush)new Converters.ColorToBrushConverter().Convert(AccentColor, typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture);

    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetProperty(ref _isRenaming, value);
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (SetProperty(ref _activeTab, value))
            {
                foreach (var t in Tabs)
                    t.IsActive = t == value;
                OnPropertyChanged(nameof(ActiveTabContent));
            }
        }
    }

    public CapturedWindow? ActiveTabContent => ActiveTab?.Model;

    public ICommand StartRenameCommand { get; }
    public ICommand FinishRenameCommand { get; }
    public ICommand PickColorCommand { get; }
    public ICommand CloseGroupCommand { get; }

    public event EventHandler? CloseRequested;
    public event EventHandler? AddWindowsRequested;

    public void RequestAddWindows()
    {
        AddWindowsRequested?.Invoke(this, EventArgs.Empty);
    }

    public GroupViewModel(Group group, GroupManager manager, WindowCaptureService capture, IconService icons)
    {
        _group = group;
        _manager = manager;
        _capture = capture;
        _icons = icons;

        _group.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
            {
                OnPropertyChanged(e.PropertyName);
                if (e.PropertyName == nameof(Group.AccentColor))
                    OnPropertyChanged(nameof(AccentBrush));
            }
        };

        foreach (var m in group.Members)
        {
            var tvm = new TabViewModel(m);
            tvm.PopOutRequested += OnPopOutRequested;
            tvm.CloseWindowRequested += OnCloseWindowRequested;
            tvm.Icon = _icons.GetFileIcon(m.ExePath);
            Tabs.Add(tvm);
        }

        if (Tabs.Count > 0 && group.ActiveIndex >= 0 && group.ActiveIndex < Tabs.Count)
            ActiveTab = Tabs[group.ActiveIndex];
        else if (Tabs.Count > 0)
            ActiveTab = Tabs[0];

        StartRenameCommand = new RelayCommand(_ => IsRenaming = true);
        FinishRenameCommand = new RelayCommand(_ => IsRenaming = false);
        PickColorCommand = new RelayCommand(_ => AddWindowsRequested?.Invoke(this, EventArgs.Empty)); // placeholder; UI handles color picker directly
        CloseGroupCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public void SetActiveTab(TabViewModel tab)
    {
        int idx = Tabs.IndexOf(tab);
        if (idx < 0)
            return;
        _manager.SwitchActiveTab(_group, idx);
        ActiveTab = tab;
    }

    public void ReorderTabs(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Tabs.Count)
            return;
        // A drop position past the last tab means "move to the end". Clamp it so
        // the model (MoveTab) and this collection apply the exact same move —
        // an unclamped index made MoveTab reject the move silently while the
        // Insert below threw ArgumentOutOfRangeException and killed the app.
        if (newIndex >= Tabs.Count)
            newIndex = Tabs.Count - 1;
        if (newIndex < 0 || oldIndex == newIndex)
            return;

        _manager.MoveTab(_group, oldIndex, newIndex);
        var item = Tabs[oldIndex];
        // Move (not RemoveAt+Insert) keeps the existing ListBox container alive,
        // so the SelectedItem/IsSelected bindings and an in-flight drag see the
        // same item instance throughout instead of a destroyed/recreated one.
        Tabs.Move(oldIndex, newIndex);
        ActiveTab = item;
    }

    public void AddCapturedWindow(CapturedWindow window)
    {
        _group.Members.Add(window);
        var tvm = new TabViewModel(window);
        tvm.PopOutRequested += OnPopOutRequested;
        tvm.CloseWindowRequested += OnCloseWindowRequested;
        tvm.Icon = _icons.GetFileIcon(window.ExePath);
        Tabs.Add(tvm);
        SetActiveTab(tvm);
    }

    public void ReleaseTab(TabViewModel tab, bool show = true)
    {
        int idx = Tabs.IndexOf(tab);
        if (idx < 0)
            return;
        _manager.ReleaseTab(_group, idx, show);
        tab.PopOutRequested -= OnPopOutRequested;
        tab.CloseWindowRequested -= OnCloseWindowRequested;
        Tabs.RemoveAt(idx);
        if (Tabs.Count == 0)
            ActiveTab = null;
        else
            SetActiveTab(Tabs[Math.Min(idx, Tabs.Count - 1)]);
    }

    public void CloseGroup()
    {
        _manager.CloseGroup(_group);
        foreach (var t in Tabs)
        {
            t.PopOutRequested -= OnPopOutRequested;
            t.CloseWindowRequested -= OnCloseWindowRequested;
        }
        Tabs.Clear();
        ActiveTab = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPopOutRequested(object? sender, TabViewModel tab)
    {
        int idx = Tabs.IndexOf(tab);
        if (idx < 0)
            return;
        _manager.ReleaseTab(_group, idx);
        tab.PopOutRequested -= OnPopOutRequested;
        tab.CloseWindowRequested -= OnCloseWindowRequested;
        Tabs.RemoveAt(idx);
        if (Tabs.Count == 0)
            ActiveTab = null;
        else
            SetActiveTab(Tabs[Math.Min(idx, Tabs.Count - 1)]);
    }

    private void OnCloseWindowRequested(object? sender, TabViewModel tab)
    {
        IntPtr hwnd = tab.Model.Hwnd;
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            // Window already gone; just clean up the dead tab.
            ReleaseTab(tab);
            return;
        }

        // Ask the guest to close gracefully, in place (releasing first would
        // visibly pop the window out to the desktop before it closes). Do NOT
        // remove the tab here: if the guest actually closes, the destroy
        // WinEvent drives the existing teardown; if it hides to the tray
        // instead, the guest-initiated-hide path does; and if it shows a save
        // prompt or ignores WM_CLOSE, the tab correctly stays alive.
        NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    public void RefreshIcon(TabViewModel tab)
    {
        tab.Icon = _icons.GetFileIcon(tab.Model.ExePath);
    }
}
