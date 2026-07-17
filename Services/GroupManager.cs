using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Owns all groups and coordinates capture/release, tab switching/reordering,
/// group close, and emergency release. Enforces the flat no-nesting rule.
/// </summary>
public sealed class GroupManager
{
    private readonly WindowCaptureService _capture;
    private readonly WindowShepherdService _shepherd;
    private readonly PersistenceService _persistence;
    private readonly LoggingService _log;
    private readonly HashSet<IntPtr> _ownContainerHwnds = new();
    private readonly object _lock = new();

    public ObservableCollection<Group> Groups { get; } = new();

    public GroupManager(WindowCaptureService capture, WindowShepherdService shepherd, PersistenceService persistence, LoggingService log)
    {
        _capture = capture;
        _shepherd = shepherd;
        _persistence = persistence;
        _log = log;
    }

    /// <summary>
    /// Releases a captured window through whichever backend originally
    /// captured its owning group (see <see cref="Group.Mode"/>).
    /// </summary>
    private void ReleaseViaBackend(Group group, CapturedWindow cw, bool show)
    {
        if (group.Mode == GroupCaptureMode.Shepherd)
            _shepherd.Release(cw, show);
        else
            _capture.Release(cw, show);
    }

    public void RestoreState()
    {
        foreach (var group in _persistence.Load())
        {
            Groups.Add(group);
        }
    }

    public void SaveState()
    {
        _saveDebounce?.Stop();
        _persistence.Save(Groups);
    }

    private DispatcherTimer? _saveDebounce;

    /// <summary>
    /// Debounced SaveState: persists layout intent ~1s after the most recent
    /// state change so it survives crashes and force-kills (this app's history
    /// is dominated by those), without a disk write per event for high-frequency
    /// mutations such as drag-reorder. UI thread only (DispatcherTimer).
    /// </summary>
    public void RequestSave()
    {
        if (_saveDebounce == null)
        {
            _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _saveDebounce.Tick += (_, _) =>
            {
                _saveDebounce!.Stop();
                _persistence.Save(Groups);
            };
        }
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    public void RegisterContainerHwnd(IntPtr hwnd)
    {
        lock (_lock) { _ownContainerHwnds.Add(hwnd); }
    }

    public void UnregisterContainerHwnd(IntPtr hwnd)
    {
        lock (_lock) { _ownContainerHwnds.Remove(hwnd); }
    }

    public bool IsOwnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        lock (_lock)
        {
            if (_ownContainerHwnds.Contains(hwnd))
                return true;

            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root != IntPtr.Zero && _ownContainerHwnds.Contains(root))
                return true;

            // Also guard against our own process.
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == NativeMethods.GetCurrentProcessId();
        }
    }

    /// <summary>
    /// True if the HWND is a live captured member of any group. Works by value
    /// comparison only, so it is also valid for HWNDs that were just destroyed.
    /// Must be called on the UI thread (Groups/Members are mutated there).
    /// </summary>
    public bool IsCapturedWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        foreach (var group in Groups)
        {
            foreach (var member in group.Members)
            {
                if (member.Hwnd == hwnd)
                    return true;
            }
        }
        return false;
    }

    public Group CreateGroup(string name = "Group", string accentColor = "#2196F3")
    {
        var group = new Group { Name = name, AccentColor = accentColor };
        Groups.Add(group);
        _log.Log($"Created group {group.Id} '{name}'");
        RequestSave();
        return group;
    }

    public string? CaptureIntoGroup(IntPtr hwnd, IntPtr hostHwnd, Group group)
    {
        if (IsOwnWindow(hwnd))
            return "Cannot capture a TabDock window (no nesting).";

        var cw = _capture.Capture(hwnd, hostHwnd, out string? error);
        if (cw == null)
            return error ?? "Capture failed.";

        group.Members.Add(cw);
        group.ActiveIndex = group.Members.Count - 1;
        _log.Log($"Added window 0x{hwnd.ToInt64():X} to group {group.Id}");
        return null;
    }

    public void SwitchActiveTab(Group group, int index)
    {
        if (index < 0 || index >= group.Members.Count)
            return;
        group.ActiveIndex = index;
        _log.Log($"Switched group {group.Id} to tab {index}");
        RequestSave();
    }

    public void MoveTab(Group group, int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= group.Members.Count)
            return;
        if (newIndex < 0 || newIndex >= group.Members.Count)
            return;
        if (oldIndex == newIndex)
            return;

        var item = group.Members[oldIndex];
        group.Members.RemoveAt(oldIndex);
        group.Members.Insert(newIndex, item);
        group.ActiveIndex = newIndex;
        _log.Log($"Reordered tab {oldIndex}->{newIndex} in group {group.Id}");
        RequestSave();
    }

    public void ReleaseTab(Group group, int index, bool show = true)
    {
        if (index < 0 || index >= group.Members.Count)
            return;

        var cw = group.Members[index];
        group.Members.RemoveAt(index);
        ReleaseViaBackend(group, cw, show);

        if (group.ActiveIndex >= group.Members.Count)
            group.ActiveIndex = group.Members.Count - 1;

        _log.Log($"Released tab {index} from group {group.Id}");
        RequestSave();
    }

    public void CloseGroup(Group group)
    {
        // Release in reverse so indices stay stable.
        while (group.Members.Count > 0)
        {
            var cw = group.Members[^1];
            group.Members.RemoveAt(group.Members.Count - 1);
            ReleaseViaBackend(group, cw, show: true);
        }

        if (Groups.Contains(group))
            Groups.Remove(group);

        _log.Log($"Closed group {group.Id}");
        RequestSave();
    }

    public void RemoveGroup(Group group)
    {
        if (Groups.Contains(group))
        {
            Groups.Remove(group);
            RequestSave();
        }
    }

    public void EmergencyReleaseAll()
    {
        _log.Log("EMERGENCY RELEASE: releasing all captured windows.");
        try
        {
            foreach (var group in Groups.ToList())
            {
                foreach (var cw in group.Members.ToList())
                {
                    try
                    {
                        ReleaseViaBackend(group, cw, show: true);
                    }
                    catch (Exception ex)
                    {
                        _log.LogException("EmergencyReleaseAll", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogException("EmergencyReleaseAll enumeration", ex);
        }
    }
}
