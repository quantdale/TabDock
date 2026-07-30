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
    private readonly IconService _icons;
    private readonly LoggingService _log;
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

    // ContainerWindow binds AccentBrush from several places (window background,
    // caption strip, colour chip), and WPF re-reads the property for each of
    // them on every change notification. Converting the colour string and
    // allocating a fresh SolidColorBrush per read was pure waste; cache the
    // frozen brush and rebuild it only when the colour actually changes
    // (PERF25-06). A frozen brush is safe to share across all the bindings.
    private static readonly Converters.ColorToBrushConverter s_colorToBrush = new();
    private Brush? _accentBrush;
    private string? _accentBrushSource;

    public Brush AccentBrush
    {
        get
        {
            string color = AccentColor;
            if (_accentBrush == null || !string.Equals(_accentBrushSource, color, StringComparison.Ordinal))
            {
                _accentBrush = (Brush)s_colorToBrush.Convert(color, typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture);
                _accentBrushSource = color;
            }
            return _accentBrush;
        }
    }

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
            }
        }
    }

    public ICommand StartRenameCommand { get; }
    public ICommand FinishRenameCommand { get; }
    public ICommand PickColorCommand { get; }
    public ICommand CloseGroupCommand { get; }

    public event EventHandler? CloseRequested;
    public event EventHandler? AddWindowsRequested;

    /// <summary>
    /// Raised when popping out the last tab leaves this group with zero members.
    /// The destroy/hide WinEvent paths (App.RemoveDeadMember) already close an
    /// emptied container automatically; pop-out via drag-out or the context menu
    /// was the one path that left an empty container open indefinitely (finding
    /// L11). Distinct from CloseRequested (raised by CloseGroup, itself invoked
    /// from inside ContainerWindow's own Closing handler) to avoid re-entering
    /// Window.Close from within its own Closing event.
    /// </summary>
    public event EventHandler? EmptiedByPopOut;

    public void RequestAddWindows()
    {
        AddWindowsRequested?.Invoke(this, EventArgs.Empty);
    }

    public GroupViewModel(Group group, GroupManager manager, IconService icons, LoggingService log)
    {
        _group = group;
        _manager = manager;
        _icons = icons;
        _log = log;

        _group.PropertyChanged += OnGroupPropertyChanged;

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
        // Intentional no-op placeholder pending future UI wiring (item #6 /
        // investigation_findings.md:285): PickColorCommand is unbound in any
        // XAML file, so nothing currently invokes it. It previously invoked
        // AddWindowsRequested by mistake, which would have silently opened the
        // capture picker instead of picking a color if it ever became
        // reachable. Fixed to do nothing rather than the wrong thing.
        PickColorCommand = new RelayCommand(_ => { });
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

    /// <summary>
    /// Releases one tab back to standalone and re-derives the active tab.
    /// Releasing an INACTIVE tab keeps the currently active one active: the
    /// removal must not switch the user away from the window they are looking
    /// at, which happens on the release of any background tab — context-menu
    /// "Pop out" on one, or a background guest that closes or hides itself
    /// (App.RemoveDeadMember routes both through here).
    /// </summary>
    public void ReleaseTab(TabViewModel tab, bool show = true)
    {
        int idx = Tabs.IndexOf(tab);
        if (idx < 0)
            return;

        // Snapshot by reference, before the removal: Group.ActiveIndex is a
        // positional index into Members, so it silently points at a different
        // member once an earlier one is removed and cannot be used to identify
        // "which tab was active" afterwards.
        TabViewModel? previouslyActive = ActiveTab;

        _manager.ReleaseTab(_group, idx, show);
        tab.PopOutRequested -= OnPopOutRequested;
        tab.CloseWindowRequested -= OnCloseWindowRequested;
        Tabs.RemoveAt(idx);

        if (Tabs.Count == 0)
        {
            ActiveTab = null;
            EmptiedByPopOut?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (previouslyActive != null && previouslyActive != tab && Tabs.Contains(previouslyActive))
        {
            // Unchanged active tab, but SetActiveTab still has to run: it is what
            // re-syncs the model's positional ActiveIndex to the surviving tab's
            // new index. ActiveTab itself does not change, so no ActiveTab
            // PropertyChanged fires and the shepherd sync correctly stays put.
            SetActiveTab(previouslyActive);
        }
        else
        {
            // The active tab itself was released: fall through to its neighbour
            // (the tab that slid into its slot, or the new last tab).
            SetActiveTab(Tabs[Math.Min(idx, Tabs.Count - 1)]);
        }
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

    // Pop-out is an ordinary visible release; it was a second, drifted copy of
    // ReleaseTab's body (which is where the "keep the active tab active" rule
    // lives), so it delegates instead of duplicating it.
    private void OnPopOutRequested(object? sender, TabViewModel tab) => ReleaseTab(tab);

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
        //
        // HWND-recycle guard immediately before posting: IsWindow above only
        // proves the HWND value is currently a window, not that it is still
        // OUR guest — the guest may have closed and Windows may already have
        // reused the value for an unrelated window, which would then receive
        // an arbitrary WM_CLOSE. Verify the owning PID matches the one stored
        // at capture time.
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint currentPid);
        if (currentPid != tab.Model.ProcessId)
        {
            _log.Log($"Close-window: skipping 0x{hwnd.ToInt64():X} — HWND recycled (expected PID {tab.Model.ProcessId}, now {currentPid}).");
            ReleaseTab(tab);
            return;
        }
        if (!NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
            _log.Log($"Close-window: PostMessage(WM_CLOSE) to 0x{hwnd.ToInt64():X} failed: {NativeMethods.FormatLastError()}");
    }

    public void RefreshIcon(TabViewModel tab)
    {
        tab.Icon = _icons.GetFileIcon(tab.Model.ExePath);
    }

    private void OnGroupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null)
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(Group.AccentColor))
                OnPropertyChanged(nameof(AccentBrush));
        }
    }

    /// <summary>
    /// Unsubscribes from the (long-lived, possibly-outlives-this-view-model)
    /// Group. Without this, a GroupViewModel whose container closed while the
    /// Group itself stayed alive (e.g. a restored-but-not-yet-repopulated group
    /// re-targeted from the capture picker) is kept reachable forever via
    /// Group.PropertyChanged, along with everything it references (Tabs,
    /// the ContainerWindow that owned it, its visual tree). Call once, from
    /// ContainerWindow_Closed.
    /// </summary>
    public void Detach()
    {
        _group.PropertyChanged -= OnGroupPropertyChanged;
    }
}
