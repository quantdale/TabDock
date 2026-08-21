using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
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
        set
        {
            // Reject blank/whitespace-only renames so a group can never become
            // an empty, invisible entry in the launcher/group list. Keep the
            // existing name when the user clears the box.
            string? trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                // A silent return would leave the TextBox showing "" while the
                // model keeps the old name; raise so the binding reverts.
                OnPropertyChanged(nameof(Name));
                return;
            }
            _group.Name = trimmed;
        }
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

    // Tab-strip projection: mirrors Tabs when no split is active; while a split
    // pair exists it replaces the LEFT member's slot with a single composite
    // item ([ A | B ]) and suppresses the RIGHT member's ordinary tab. The
    // underlying Tabs collection remains authoritative for identity and order —
    // this is a presentation-layer concept only (SplitCompositeViewModel).
    public ObservableCollection<object> DisplayTabs { get; } = new();
    private SplitCompositeViewModel? _splitComposite;

    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (SetProperty(ref _activeTab, value))
            {
                foreach (var t in Tabs)
                    t.IsActive = t == value;
                _splitComposite?.RefreshActiveState();
            }
        }
    }

    public ICommand StartRenameCommand { get; }
    public ICommand FinishRenameCommand { get; }
    public ICommand CloseGroupCommand { get; }
    public ICommand DeleteGroupCommand { get; }

    public event EventHandler? CloseRequested;
    public event EventHandler? AddWindowsRequested;
    public event EventHandler? DeleteGroupRequested;

    /// <summary>
    /// Raised when popping out the last tab leaves this group with zero members.
    /// The destroy/hide WinEvent paths (GuestLifecycleService.RemoveDeadMember)
    /// already close an emptied container automatically; pop-out via drag-out or
    /// the context menu was the one path that left an empty container open
    /// indefinitely (finding L11). Distinct from CloseRequested (raised by
    /// CloseGroup, itself invoked from inside ContainerWindow's own Closing
    /// handler) to avoid re-entering Window.Close from within its own Closing
    /// event.
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

        // The strip projection mirrors every Tabs mutation; subscribe before
        // populating so DisplayTabs is filled by the same Add events.
        Tabs.CollectionChanged += Tabs_CollectionChanged;

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
        CloseGroupCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, EventArgs.Empty));
        DeleteGroupCommand = new RelayCommand(_ => DeleteGroupRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Switches the tab strip to the composite representation for a split pair:
    /// the LEFT member's slot becomes a single [ A | B ] item and the RIGHT
    /// member's ordinary tab is suppressed. Presentation only — Tabs keeps both
    /// members and their order.
    /// </summary>
    public void SetSplitComposite(TabViewModel left, TabViewModel right)
    {
        if (left == null || right == null || ReferenceEquals(left, right))
            return;
        _splitComposite = new SplitCompositeViewModel(left, right);
        _splitComposite.RefreshActiveState();
        RebuildDisplayTabs();
    }

    /// <summary>
    /// Restores the ordinary one-tab-per-member strip after split exit.
    /// </summary>
    public void ClearSplitComposite()
    {
        _splitComposite = null;
        RebuildDisplayTabs();
    }

    private void RebuildDisplayTabs()
    {
        DisplayTabs.Clear();
        SplitCompositeViewModel? composite = _splitComposite;
        if (composite == null)
        {
            foreach (TabViewModel t in Tabs)
                DisplayTabs.Add(t);
            return;
        }
        foreach (TabViewModel t in Tabs)
        {
            if (ReferenceEquals(t, composite.Right))
                continue; // suppressed while the pair exists
            if (ReferenceEquals(t, composite.Left))
                DisplayTabs.Add(composite);
            else
                DisplayTabs.Add(t);
        }
    }

    private void Tabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_splitComposite != null)
        {
            // A mutation while a pair exists changes the projection wholesale.
            RebuildDisplayTabs();
            return;
        }
        // No composite: mirror the mutation exactly. Add/Remove/Move keep the
        // ListBox item containers alive, which the anti-oscillation tab-drag
        // behaviour depends on — a clear-and-rebuild would recreate containers
        // on every reorder and re-enable the oscillation feedback loop.
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                DisplayTabs.Insert(e.NewStartingIndex, Tabs[e.NewStartingIndex]);
                break;
            case NotifyCollectionChangedAction.Remove:
                DisplayTabs.RemoveAt(e.OldStartingIndex);
                break;
            case NotifyCollectionChangedAction.Move:
                DisplayTabs.Move(e.OldStartingIndex, e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Reset:
                RebuildDisplayTabs();
                break;
        }
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

    /// <summary>Durably commits the final order after a high-frequency drag.</summary>
    public void CommitReorder() => _manager.CommitReorder();

    public void AddCapturedWindow(CapturedWindow window)
    {
        TabViewModel? previousActive = ActiveTab;
        TabViewModel? tvm = null;
        bool memberAdded = false;
        bool tabAdded = false;
        try
        {
            // Finish all managed-object construction before mutating either
            // authoritative collection. If icon/view-model setup fails, no
            // captured member is exposed to lifecycle monitoring halfway
            // through the insertion.
            tvm = new TabViewModel(window);
            tvm.PopOutRequested += OnPopOutRequested;
            tvm.CloseWindowRequested += OnCloseWindowRequested;
            tvm.Icon = _icons.GetFileIcon(window.ExePath);

            _group.Members.Add(window);
            memberAdded = true;
            Tabs.Add(tvm);
            tabAdded = true;
            SetActiveTab(tvm);
        }
        catch
        {
            if (tabAdded && tvm != null)
                Tabs.Remove(tvm);

            if (tvm != null)
            {
                tvm.PopOutRequested -= OnPopOutRequested;
                tvm.CloseWindowRequested -= OnCloseWindowRequested;
            }

            if (memberAdded)
                _group.Members.Remove(window);

            if (previousActive != null && Tabs.Contains(previousActive))
                ActiveTab = previousActive;
            else if (Tabs.Count == 0)
                ActiveTab = null;

            throw;
        }
    }

    /// <summary>
    /// Releases one tab back to standalone and re-derives the active tab.
    /// Releasing an INACTIVE tab keeps the currently active one active: the
    /// removal must not switch the user away from the window they are looking
    /// at, which happens on the release of any background tab — context-menu
    /// "Pop out" on one, or a background guest that closes or hides itself
    /// (GuestLifecycleService.RemoveDeadMember routes both through here).
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

        WindowReleaseOutcome releaseOutcome = _manager.ReleaseTab(_group, idx, show);
        if (releaseOutcome == WindowReleaseOutcome.RecoveryPending)
        {
            _log.Log($"Release of tab {idx} retained in group {_group.Id}: native recovery is pending.");
            return;
        }
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
        else if (ActiveTab != null && Tabs.Contains(ActiveTab))
        {
            // The active tab itself was released, but the split-member removal
            // path (ContainerWindow.HandleSplitMemberRemoved, fired
            // synchronously by Tabs.RemoveAt above) has already selected the
            // correct survivor when a presented pair loses a member, or
            // retained the current non-member guest when a dormant pair loses
            // one. The positional neighbour pick below would disagree with
            // that transition and could silently hide the retained guest.
            // Honor the already-selected active tab; SetActiveTab still
            // re-syncs the model's positional ActiveIndex.
            SetActiveTab(ActiveTab);
        }
        else
        {
            // The active tab itself was released through an ordinary path: fall
            // through to its neighbour (the tab that slid into its slot, or the
            // new last tab).
            SetActiveTab(Tabs[Math.Min(idx, Tabs.Count - 1)]);
        }
    }

    public bool CloseGroup()
    {
        bool released = _manager.CloseGroup(_group);
        foreach (TabViewModel t in Tabs.Where(t => !_group.Members.Contains(t.Model)).ToList())
        {
            t.PopOutRequested -= OnPopOutRequested;
            t.CloseWindowRequested -= OnCloseWindowRequested;
            Tabs.Remove(t);
        }
        if (!released)
        {
            // A partial close leaves only RecoveryPending members in the
            // authoritative model. Do not activate one merely to repair a
            // removed selection: ActiveTab changes drive native positioning,
            // while the pending guest must not receive a new mutation until
            // its strong identity probe succeeds on a retry.
            if (ActiveTab != null && !Tabs.Contains(ActiveTab))
                ActiveTab = null;
            return false;
        }

        Tabs.Clear();
        ActiveTab = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Clears only the presentation-side tabs after an emergency release has
    /// already returned the native windows to standalone. This is used by the
    /// session-ending path so a cancelled logoff cannot leave stale captured
    /// tabs visible in an otherwise normalized group.
    /// </summary>
    public void ClearReleasedTabsAfterSessionEnding()
    {
        _splitComposite = null;
        foreach (TabViewModel tab in Tabs)
        {
            tab.PopOutRequested -= OnPopOutRequested;
            tab.CloseWindowRequested -= OnCloseWindowRequested;
        }
        Tabs.Clear();
        ActiveTab = null;
    }

    // Pop-out is an ordinary visible release; it was a second, drifted copy of
    // ReleaseTab's body (which is where the "keep the active tab active" rule
    // lives), so it delegates instead of duplicating it.
    private void OnPopOutRequested(object? sender, TabViewModel tab) => ReleaseTab(tab);

    private void OnCloseWindowRequested(object? sender, TabViewModel tab)
    {
        IntPtr hwnd = tab.Model.Hwnd;
        if (hwnd == IntPtr.Zero || !_manager.IsCurrentCapturedWindow(tab.Model))
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
        // The shepherd gate above checks the owning PID, stable class, and
        // executable immediately before posting. Titles are intentionally
        // excluded because guests can change them while captured.
        if (!NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
            _log.Log($"Close-window: PostMessage(WM_CLOSE) to 0x{hwnd.ToInt64():X} failed: {NativeMethods.FormatLastError()}");
    }

    private void OnGroupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null)
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(Group.AccentColor))
                OnPropertyChanged(nameof(AccentBrush));
            if (e.PropertyName == nameof(Group.Name)
                || e.PropertyName == nameof(Group.AccentColor))
            {
                _manager.RequestDurableSave("group-metadata-committed");
            }
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
        // Tabs is a VM-owned ObservableCollection — its CollectionChanged keeps
        // this VM (and its ContainerWindow) rooted if a view keeps a reference
        // to Tabs/DisplayTabs after the container closes (capture-picker
        // re-target, PersistedTabs-only Group). Unsubscribe explicitly so
        // Closed containers do not leak through the strip projection.
        Tabs.CollectionChanged -= Tabs_CollectionChanged;
    }
}
