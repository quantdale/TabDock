using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Owns all groups and coordinates capture/release, tab switching/reordering,
/// group close, and emergency release. Enforces the flat no-nesting rule.
/// </summary>
public sealed class GroupManager
{
    private readonly WindowCaptureService _capture;
    private readonly PersistenceService _persistence;
    private readonly LoggingService _log;
    private readonly HashSet<IntPtr> _ownContainerHwnds = new();
    private readonly object _lock = new();

    public ObservableCollection<Group> Groups { get; } = new();

    public GroupManager(WindowCaptureService capture, PersistenceService persistence, LoggingService log)
    {
        _capture = capture;
        _persistence = persistence;
        _log = log;
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
        _persistence.Save(Groups);
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
    }

    public void ReleaseTab(Group group, int index, bool show = true)
    {
        if (index < 0 || index >= group.Members.Count)
            return;

        var cw = group.Members[index];
        group.Members.RemoveAt(index);
        _capture.Release(cw, show);

        if (group.ActiveIndex >= group.Members.Count)
            group.ActiveIndex = group.Members.Count - 1;

        _log.Log($"Released tab {index} from group {group.Id}");
    }

    public void CloseGroup(Group group)
    {
        // Release in reverse so indices stay stable.
        while (group.Members.Count > 0)
        {
            var cw = group.Members[^1];
            group.Members.RemoveAt(group.Members.Count - 1);
            _capture.Release(cw);
        }

        if (Groups.Contains(group))
            Groups.Remove(group);

        _log.Log($"Closed group {group.Id}");
    }

    public void RemoveGroup(Group group)
    {
        if (Groups.Contains(group))
            Groups.Remove(group);
    }

    public void EmergencyReleaseAll()
    {
        _log.Log("EMERGENCY RELEASE: releasing all captured windows.");
        try
        {
            var all = Groups.SelectMany(g => g.Members).ToList();
            foreach (var cw in all)
            {
                try
                {
                    _capture.Release(cw);
                }
                catch (Exception ex)
                {
                    _log.LogException("EmergencyReleaseAll", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogException("EmergencyReleaseAll enumeration", ex);
        }
    }
}
