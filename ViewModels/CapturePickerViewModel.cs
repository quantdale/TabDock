using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.ViewModels;

/// <summary>
/// View-model for the capture-picker dialog.
/// Lists top-level windows and lets the user choose a target group.
/// </summary>
public sealed class CapturePickerViewModel : ViewModelBase, IDisposable
{
    private readonly GroupManager _manager;
    private readonly IconService _icons;
    private readonly LoggingService _log;
    private readonly Dispatcher? _dispatcher;
    private readonly Func<IEnumerable<WindowInfo>>? _testCandidateSource;
    private GroupOption? _selectedGroupOption;
    private CancellationTokenSource? _iconLoadCancellation;
    private int _refreshGeneration;
    private bool _disposed;
    private Task _iconResolutionCompletion = Task.CompletedTask;

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

    /// <summary>
    /// Completes when the current refresh has finished its bounded background
    /// icon extraction. Row updates are still marshalled to the UI dispatcher;
    /// this task is primarily useful for diagnostics/performance measurement.
    /// </summary>
    public Task IconResolutionCompletion => _iconResolutionCompletion;

    public CapturePickerViewModel(GroupManager manager, IconService icons, LoggingService log)
        : this(manager, icons, log, testCandidateSource: null)
    {
    }

    internal CapturePickerViewModel(
        GroupManager manager,
        IconService icons,
        LoggingService log,
        Func<IEnumerable<WindowInfo>>? testCandidateSource)
    {
        _manager = manager;
        _icons = icons;
        _log = log;
        _dispatcher = Application.Current?.Dispatcher;
        _testCandidateSource = testCandidateSource;

        RefreshCommand = new RelayCommand(_ => Refresh());
        GroupSelectedCommand = new RelayCommand(_ => GroupingRequested?.Invoke(this, EventArgs.Empty), _ => HasSelection);
        CancelCommand = new RelayCommand(_ => Canceled?.Invoke(this, EventArgs.Empty));

        Refresh();
    }

    public void Refresh()
    {
        if (_disposed)
            return;

        CancelIconResolution();
        int refreshGeneration = unchecked(++_refreshGeneration);
        Stopwatch stopwatch = Stopwatch.StartNew();
        int windowsSeen = 0;
        int candidates = 0;
        var uncachedIcons = new List<IconWorkItem>();
        Guid? previouslySelectedGroupId = SelectedGroupOption?.Id;
        Windows.Clear();
        Groups.Clear();

        Groups.Add(new GroupOption(Guid.Empty, "<New group>"));
        foreach (var g in _manager.Groups)
        {
            Groups.Add(new GroupOption(g.Id, g.Name));
        }
        // Refreshing the expensive desktop enumeration must not silently
        // change the destination selected by the user. Resetting to
        // <New group> here caused a refresh from an existing container to
        // create an unintended empty group when the subsequent capture was
        // cancelled or failed.
        SelectedGroupOption = SelectGroupAfterRefresh(Groups, previouslySelectedGroupId);

        // The filters below run for every top-level window on the desktop
        // (typically a few hundred, of which a handful are real candidates), so
        // they are ordered cheapest-first: two style/text reads eliminate the
        // vast majority before the enumeration spends anything on a cross-process
        // DWM query or on opening a process handle (PERF25-04). The set of
        // windows offered is unchanged — every filter is independent of the
        // others, so only the order in which they reject differs.
        if (_testCandidateSource != null)
        {
            foreach (WindowInfo info in _testCandidateSource())
            {
                AddCandidate(info, uncachedIcons, ref candidates);
            }
        }
        else
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                windowsSeen++;
                try
                {
                if (!NativeMethods.IsWindowVisible(hwnd))
                    return true;

                nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
                if (((long)exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                    return true;

                string? title = NativeMethods.GetWindowTextString(hwnd);
                if (string.IsNullOrWhiteSpace(title))
                    return true;

                string? className = NativeMethods.GetClassNameString(hwnd);
                if (string.IsNullOrEmpty(className))
                    return true;

                if (_manager.IsOwnWindow(hwnd))
                    return true;

                // A window that is already a member of some group must not be
                // offered again. Capturing the same HWND twice produces two
                // CapturedWindow members for one window — two tabs (possibly in two
                // different containers) each positioning, hiding, and releasing it
                // independently — and the WinEvent handlers resolve an HWND to a
                // single member, so only one duplicate would ever receive the
                // destroy/hide bookkeeping and the other is stranded as a tab
                // pointing at a dead window. An INACTIVE tab's guest is hidden and
                // so already filtered out by IsWindowVisible above; this catches the
                // ACTIVE tab of every open group, which is genuinely on screen and
                // would otherwise be listed like any other window.
                if (_manager.IsCapturedWindow(hwnd))
                    return true;

                // Cloaked windows (suspended UWP apps, hidden ApplicationFrameHost
                // ghosts) are reported visible by IsWindowVisible but aren't actually
                // on screen; capturing one produces a tab with nothing behind it.
                // Last, because it is the most expensive check here.
                int hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out bool cloaked, sizeof(uint));
                if (hr == 0 && cloaked)
                    return true;

                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                string? exe = _icons.GetProcessImagePath(pid);
                if (string.IsNullOrWhiteSpace(exe))
                    return true;

                var info = new WindowInfo(hwnd, pid, className, title, exe);
                AddCandidate(info, uncachedIcons, ref candidates);
                return true;
                }
                catch (Exception ex)
                {
                    // One window/process probe throwing (a process exiting
                    // between enumeration and handle open, an injected failure)
                    // must not crash the whole picker; skip that window.
                    _log.LogException($"PICKER[refresh] probe failed for 0x{hwnd.ToInt64():X}", ex);
                    return true;
                }
            }, IntPtr.Zero);
        }

        QueueIconResolution(refreshGeneration, uncachedIcons);

        stopwatch.Stop();
        _log.Log($"PICKER[refresh] windowsSeen={windowsSeen} candidates={candidates} elapsedMs={stopwatch.ElapsedMilliseconds}");
    }

    private void AddCandidate(
        WindowInfo info,
        List<IconWorkItem> uncachedIcons,
        ref int candidates)
    {
        // Resolve only completed cache entries on the UI thread. A miss is
        // queued for the one bounded worker so rows become visible promptly.
        if (_icons.TryGetCachedFileIcon(info.ExePath, out ImageSource? cachedIcon))
        {
            info.Icon = cachedIcon;
        }
        else if (!string.IsNullOrEmpty(info.ExePath))
        {
            uncachedIcons.Add(new IconWorkItem(info, info.ExePath));
        }

        // Only selection changes affect command state. Raising the global
        // requery for every icon assignment turned each refresh into an
        // O(rows^2) CommandManager storm.
        info.PropertyChanged += (_, e) =>
        {
            if (!string.Equals(e.PropertyName, nameof(WindowInfo.IsSelected), StringComparison.Ordinal))
                return;
            OnPropertyChanged(nameof(HasSelection));
            ((RelayCommand)GroupSelectedCommand).RaiseCanExecuteChanged();
        };
        Windows.Add(info);
        candidates++;
    }

    private void QueueIconResolution(int refreshGeneration, IReadOnlyList<IconWorkItem> requests)
    {
        _iconResolutionCompletion = Task.CompletedTask;
        if (requests.Count == 0)
            return;

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _iconResolutionCompletion = completion.Task;
        var cancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = cancellation.Token;
        _iconLoadCancellation = cancellation;
        _ = Task.Run(() => ResolveIconsOnWorker(
            refreshGeneration,
            requests,
            cancellationToken,
            cancellation,
            completion));
    }

    private void ResolveIconsOnWorker(
        int refreshGeneration,
        IReadOnlyList<IconWorkItem> requests,
        CancellationToken cancellationToken,
        CancellationTokenSource cancellation,
        TaskCompletionSource<bool> completion)
    {
        try
        {
            foreach (IconWorkItem request in requests)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                ImageSource? icon = null;
                try
                {
                    icon = _icons.GetFileIcon(request.ExePath);
                }
                catch (Exception ex)
                {
                    // One cosmetic icon failure must not abort the remaining
                    // candidates. IconService normally converts extraction
                    // failures to a cached null; this protects the worker from
                    // an injected or future resolver failure as well.
                    _log.LogException("Capture picker icon resolution", ex);
                }

                if (cancellationToken.IsCancellationRequested)
                    return;
                PostIconResult(refreshGeneration, request.Row, icon);
            }
            completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _log.LogException("Capture picker icon worker", ex);
            completion.TrySetException(ex);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
                completion.TrySetCanceled(cancellationToken);

            // Either the worker or the UI-side cancellation path owns disposal,
            // never both. The token is safe for the worker to inspect even when
            // the UI wins the exchange and disposes the source concurrently.
            if (ReferenceEquals(Interlocked.CompareExchange(
                    ref _iconLoadCancellation,
                    null,
                    cancellation),
                cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    private void PostIconResult(int refreshGeneration, WindowInfo row, ImageSource? icon)
    {
        if (_dispatcher == null || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            return;

        try
        {
            _dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                if (_disposed || refreshGeneration != _refreshGeneration || !Windows.Contains(row))
                    return;
                row.Icon = icon;
            }));
        }
        catch (InvalidOperationException)
        {
            // The picker can close while the worker is between extraction and
            // dispatch. Closing invalidates the generation and cancellation;
            // a dispatcher shutdown is a benign final race.
        }
    }

    private void CancelIconResolution()
    {
        CancellationTokenSource? cancellation = Interlocked.Exchange(
            ref _iconLoadCancellation,
            null);
        if (cancellation == null)
            return;

        cancellation.Cancel();
        cancellation.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        unchecked { _refreshGeneration++; }
        CancelIconResolution();
    }

    private sealed record IconWorkItem(WindowInfo Row, string ExePath);

    internal static GroupOption? SelectGroupAfterRefresh(
        IEnumerable<GroupOption> options,
        Guid? previouslySelectedGroupId)
        => options.FirstOrDefault(option => option.Id == previouslySelectedGroupId)
            ?? options.FirstOrDefault();

    public sealed class WindowInfo : ViewModelBase
    {
        private bool _isSelected;
        private ImageSource? _icon;

        public IntPtr Hwnd { get; }
        public uint ProcessId { get; }
        public string ClassName { get; }
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

        public WindowInfo(IntPtr hwnd, uint processId, string className, string title, string? exePath)
        {
            Hwnd = hwnd;
            ProcessId = processId;
            ClassName = className;
            Title = title;
            ExePath = exePath ?? string.Empty;
        }

        public WindowCaptureTarget ToCaptureTarget()
            => new WindowCaptureTarget(Hwnd, ProcessId, ClassName, Title, ExePath);
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
