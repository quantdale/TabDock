using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

/// <summary>
/// READ-ONLY UI Automation helpers.
///
/// HARD RULE (enforced by construction): this class exposes only find/read helpers that
/// return elements, rectangles, and strings. It never calls Invoke/SelectionItem/Toggle
/// or any other action pattern — all clicking is done with the real mouse (see
/// <see cref="Input"/>) at coordinates read from here.
/// </summary>
internal static class Uia
{
    public static AutomationElement? FromHwnd(IntPtr hwnd)
    {
        try
        {
            if (!NativeMethods.IsWindow(hwnd))
                return null;
            return AutomationElement.FromHandle(hwnd);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Waits for a top-level window (desktop child) with the given process id and exact title.</summary>
    public static AutomationElement? WaitForWindowElement(uint pid, string titleExact, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Util.ThrowIfCancelled();
            try
            {
                var cond = new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, unchecked((int)pid)),
                    new PropertyCondition(AutomationElement.NameProperty, titleExact));
                AutomationElement? el = AutomationElement.RootElement.FindFirst(TreeScope.Children, cond);
                if (el != null)
                    return el;
            }
            catch
            {
                // Transient UIA failures while windows churn; retry until timeout.
            }
            Thread.Sleep(200);
        }
        return null;
    }

    /// <summary>
    /// Finds descendants of <paramref name="root"/> of a control type whose Name matches.
    /// Returns the first match; <paramref name="matchCount"/> lets callers refuse ambiguous results.
    /// </summary>
    public static AutomationElement? FindDescendantByName(
        AutomationElement root,
        ControlType type,
        string? nameEquals,
        string? nameContains,
        out int matchCount)
    {
        matchCount = 0;
        AutomationElement? found = null;
        try
        {
            AutomationElementCollection all = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, type));
            foreach (AutomationElement el in all)
            {
                string name;
                try { name = el.Current.Name ?? string.Empty; }
                catch { continue; }

                bool match;
                if (nameEquals != null)
                    match = string.Equals(name, nameEquals, StringComparison.Ordinal);
                else if (nameContains != null)
                    match = name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                else
                    match = false;

                if (match)
                {
                    matchCount++;
                    found ??= el;
                }
            }
        }
        catch (Exception ex)
        {
            // Return whatever was found before the failure; matchCount reflects it.
            Console.WriteLine($"    [uia] FindDescendantByName EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        }
        return found;
    }

    /// <summary>Walks up the UIA tree to the nearest ancestor of the given control type.</summary>
    public static AutomationElement? NearestAncestorOfType(AutomationElement element, ControlType type)
    {
        try
        {
            TreeWalker walker = TreeWalker.ControlViewWalker;
            AutomationElement? cur = walker.GetParent(element);
            while (cur != null)
            {
                if (Equals(cur.Current.ControlType, type))
                    return cur;
                cur = walker.GetParent(cur);
            }
        }
        catch
        {
        }
        return null;
    }

    public static AutomationElement? FindFirstOfType(AutomationElement root, ControlType type)
    {
        try
        {
            return root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, type));
        }
        catch
        {
            return null;
        }
    }

    public static int CountChildrenOfType(AutomationElement root, ControlType type)
    {
        try
        {
            return root.FindAll(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, type)).Count;
        }
        catch
        {
            return -1;
        }
    }

    public static Rect GetElementRect(AutomationElement element)
    {
        return element.Current.BoundingRectangle;
    }

    /// <summary>Reads a CheckBox/toggle element's state (read-only; does not toggle it).</summary>
    public static ToggleState? GetToggleState(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out object p))
                return ((TogglePattern)p).Current.ToggleState;
        }
        catch
        {
        }
        return null;
    }

    /// <summary>Left-edge click point of a checkbox (where the box glyph sits), for real-mouse toggling.</summary>
    public static (int X, int Y) LeftBoxPoint(AutomationElement element)
    {
        Rect r = GetElementRect(element);
        return ((int)(r.X + 9), (int)(r.Y + r.Height / 2));
    }

    /// <summary>Screen-coordinate center of an element, for real-mouse clicking.</summary>
    public static (int X, int Y) Center(AutomationElement element)
    {
        Rect r = GetElementRect(element);
        return ((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
    }

    public static List<string> GetChildNames(AutomationElement root)
    {
        var names = new List<string>();
        try
        {
            foreach (AutomationElement el in root.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition))
            {
                try { names.Add(el.Current.Name ?? string.Empty); }
                catch { names.Add("<unreadable>"); }
            }
        }
        catch
        {
        }
        return names;
    }

    /// <summary>
    /// Finds a menu item by name inside any popup owned by <paramref name="pid"/>.
    /// WPF context menus are top-level popup windows, so this scans desktop children.
    /// </summary>
    public static AutomationElement? FindMenuItemOnDesktop(uint pid, string name, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Util.ThrowIfCancelled();
            try
            {
                // Enumerate via Win32 and bridge with FromHandle: the managed UIA
                // client's RootElement children snapshot is stale for freshly
                // created popup windows (same issue as the capture picker).
                foreach (IntPtr h in Discover.GetTopLevelWindowsByPid(pid, visibleOnly: true))
                {
                    AutomationElement? top = FromHwnd(h);
                    AutomationElement? mi = top?.FindFirst(
                        TreeScope.Subtree,
                        new AndCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                            new PropertyCondition(AutomationElement.NameProperty, name)));
                    if (mi != null)
                        return mi;
                }
            }
            catch
            {
            }
            Thread.Sleep(150);
        }
        return null;
    }

    /// <summary>
    /// Finds the first editable text control (Edit or Document) under <paramref name="root"/>.
    /// Returns the match and the total number of editable descendants found.
    /// </summary>
    public static AutomationElement? FindEditOrDocument(AutomationElement root, out int matchCount)
    {
        matchCount = 0;
        AutomationElement? found = null;
        try
        {
            AutomationElementCollection all = root.FindAll(
                TreeScope.Descendants,
                new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)));
            matchCount = all.Count;
            if (matchCount > 0)
                found = all[0];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    [uia] FindEditOrDocument EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        }
        return found;
    }

    /// <summary>Reads the ValuePattern value of an element, if supported.</summary>
    public static string? GetValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object p))
                return ((ValuePattern)p).Current.Value;
        }
        catch
        {
        }
        return null;
    }

    /// <summary>Reads the SelectionItemPattern.IsSelected property, if supported.</summary>
    public static bool? IsSelected(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object p))
                return ((SelectionItemPattern)p).Current.IsSelected;
        }
        catch
        {
        }
        return null;
    }

    /// <summary>Realizes a virtualized automation element so geometry/patterns can be used.</summary>
    public static void Realize(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(VirtualizedItemPattern.Pattern, out object p))
                ((VirtualizedItemPattern)p).Realize();
        }
        catch
        {
        }
    }

    /// <summary>Selects an item via SelectionItemPattern, if supported.</summary>
    public static bool Select(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object p))
            {
                ((SelectionItemPattern)p).Select();
                return true;
            }
        }
        catch
        {
        }
        return false;
    }
}
