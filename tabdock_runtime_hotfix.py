#!/usr/bin/env python3
"""
TabDock runtime hotfix authored against:
a422785960c903b7ef00d6329675ddc5ec3cec11

This script makes only high-confidence runtime changes found during the
repository-wide audit. It intentionally refuses to edit if expected anchors do
not match, so it cannot silently patch a drifted tree.

Usage:
    python tabdock_runtime_hotfix.py C:\\path\\to\\TabDock --check
    python tabdock_runtime_hotfix.py C:\\path\\to\\TabDock --apply

The script does NOT commit or push.
"""

from __future__ import annotations

import argparse
import difflib
import sys
from pathlib import Path

BASELINE = "a422785960c903b7ef00d6329675ddc5ec3cec11"

def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)

def find_matching_brace(text: str, open_pos: int) -> int:
    """C#-aware-enough scanner: ignores comments, strings and chars."""
    depth = 0
    i = open_pos
    state = "code"
    verbatim = False
    while i < len(text):
        ch = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ""

        if state == "code":
            if ch == "/" and nxt == "/":
                state = "line_comment"; i += 2; continue
            if ch == "/" and nxt == "*":
                state = "block_comment"; i += 2; continue
            if ch == "@" and nxt == '"':
                state = "string"; verbatim = True; i += 2; continue
            if ch == '"':
                state = "string"; verbatim = False; i += 1; continue
            if ch == "'":
                state = "char"; i += 1; continue
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return i
            i += 1
            continue

        if state == "line_comment":
            if ch == "\n":
                state = "code"
            i += 1
            continue

        if state == "block_comment":
            if ch == "*" and nxt == "/":
                state = "code"; i += 2
            else:
                i += 1
            continue

        if state == "string":
            if verbatim:
                if ch == '"' and nxt == '"':
                    i += 2
                elif ch == '"':
                    state = "code"; verbatim = False; i += 1
                else:
                    i += 1
            else:
                if ch == "\\":
                    i += 2
                elif ch == '"':
                    state = "code"; i += 1
                else:
                    i += 1
            continue

        if state == "char":
            if ch == "\\":
                i += 2
            elif ch == "'":
                state = "code"; i += 1
            else:
                i += 1
            continue

    raise RuntimeError("unbalanced C# braces")

def replace_method(text: str, signature: str, replacement: str, label: str) -> str:
    start = text.find(signature)
    if start < 0:
        raise RuntimeError(f"{label}: signature not found: {signature}")
    if text.find(signature, start + 1) >= 0:
        raise RuntimeError(f"{label}: signature is not unique")
    open_pos = text.find("{", start + len(signature))
    if open_pos < 0:
        raise RuntimeError(f"{label}: opening brace not found")
    close_pos = find_matching_brace(text, open_pos)
    return text[:start] + replacement + text[close_pos + 1:]

def patch_group_manager(text: str) -> str:
    old = """    public void SwitchActiveTab(Group group, int index)
    {
        if (index < 0 || index >= group.Members.Count)
            return;
        group.ActiveIndex = index;
        _log.Log($"Switched group {group.Id} to tab {index}");
        DiagnosticRuntime.Record("group.active-tab", group: group.Id.ToString("N"), action: "switch", result: "success",
            data: new Dictionary<string, string> { ["index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        RequestDurableSave("active-tab-selected");
    }
"""
    new = """    public void SwitchActiveTab(Group group, int index)
    {
        if (index < 0 || index >= group.Members.Count)
            return;

        // Active selection is a hot-path presentation preference, not a
        // crash-safety boundary. Avoid a synchronous durable state-file commit
        // on every click/focus transition; the capture recovery journal is the
        // safety-critical store. Also make repeated activation of the already
        // active index a true no-op.
        if (group.ActiveIndex == index)
            return;

        group.ActiveIndex = index;
        _log.Log($"Switched group {group.Id} to tab {index}");
        DiagnosticRuntime.Record("group.active-tab", group: group.Id.ToString("N"), action: "switch", result: "success",
            data: new Dictionary<string, string> { ["index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        RequestSave();
    }
"""
    text = replace_once(text, old, new, "GroupManager.SwitchActiveTab")
    return text

def patch_window_shepherd(text: str) -> str:
    text = replace_once(
        text,
        "    private HiddenWindowJournalFile? _journalCache;\n",
        """    private HiddenWindowJournalFile? _journalCache;

    // A normal capture durably commits its complete recovery entry before the
    // first presentation mutation. Rewriting and fsync'ing that identical entry
    // before every tab-hide made tab switching perform forced disk I/O on the UI
    // thread. Track capture generations whose rescue entry is already durable so
    // ordinary hides can reuse it. An intentional-hide marker removes the token
    // from this set because a later retained capture must re-commit rescue intent.
    private readonly HashSet<long> _durablyJournaledCaptureTokens = new();
""",
        "WindowShepherd durable-token field",
    )

    old = """    private bool JournalCapture(CapturedWindow window)
        => UpsertJournalEntry(window, doNotRescue: false, "JournalCapture");

    /// <summary>
    /// Refreshes the durable capture entry immediately before a TabDock-driven
    /// hide. The entry already exists for a normal capture. If an older journal
    /// was loaded, the schema-compatibility gate refuses this write rather
    /// than silently rewriting tokenless recovery evidence as v3.
    /// </summary>
    private bool JournalHide(CapturedWindow window)
        => UpsertJournalEntry(window, doNotRescue: false, "JournalHide");

    private bool JournalMarkIntentionalHide(CapturedWindow window)
        => UpsertJournalEntry(window, doNotRescue: true, "JournalIntentionalHide");
"""
    new = """    private bool JournalCapture(CapturedWindow window)
    {
        bool committed = UpsertJournalEntry(window, doNotRescue: false, "JournalCapture");
        if (committed)
            _durablyJournaledCaptureTokens.Add(window.WindowIdentityToken);
        return committed;
    }

    /// <summary>
    /// Ensures rescue intent is durable before a TabDock-driven hide.
    ///
    /// Capture already commits the complete capture-session recovery entry
    /// synchronously before any presentation mutation. For that overwhelmingly
    /// common case, rewriting the identical JSON with WriteThrough + Flush(true)
    /// on every tab switch is redundant and blocks the WPF input turn. Only
    /// captures that do not currently have a known-durable rescue entry pay the
    /// synchronous journal write here.
    /// </summary>
    private bool JournalHide(CapturedWindow window)
    {
        if (_durablyJournaledCaptureTokens.Contains(window.WindowIdentityToken))
        {
            TestSequence("JournalHide.already-durable");
            return true;
        }

        bool committed = UpsertJournalEntry(window, doNotRescue: false, "JournalHide");
        if (committed)
            _durablyJournaledCaptureTokens.Add(window.WindowIdentityToken);
        return committed;
    }

    private bool JournalMarkIntentionalHide(CapturedWindow window)
    {
        bool committed = UpsertJournalEntry(window, doNotRescue: true, "JournalIntentionalHide");
        if (committed)
            _durablyJournaledCaptureTokens.Remove(window.WindowIdentityToken);
        return committed;
    }
"""
    return replace_once(text, old, new, "WindowShepherd journal hot path")

def patch_layout_coordinator(text: str) -> str:
    old = """        if (ensureFinalPass)
            _relayoutAfterPending = true;
        if (_relayoutPending)
            return;
        _relayoutPending = true;
"""
    new = """        // A requested "final pass" only needs a SECOND pass when another
        // render callback is already pending. When idle, the pass we are about
        // to schedule is itself the final pass; latching here would always
        // execute two frames for one request.
        if (_relayoutPending)
        {
            if (ensureFinalPass)
                _relayoutAfterPending = true;
            return;
        }
        _relayoutPending = true;
"""
    return replace_once(text, old, new, "PresentationLayoutCoordinator final-pass latch")

def patch_native_host(text: str) -> str:
    return replace_once(
        text,
        "NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);",
        "NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);",
        "NativeHwndHost FRAMECHANGED",
    )

def patch_container_xaml(text: str) -> str:
    old = """                     SelectionChanged="TabsListBox_SelectionChanged"
                     PreviewMouseRightButtonDown="TabsListBox_PreviewMouseRightButtonDown"
"""
    new = """                     SelectionChanged="TabsListBox_SelectionChanged"
                     PreviewMouseLeftButtonDown="TabsListBox_PreviewMouseLeftButtonDown_SplitInteraction"
                     PreviewMouseRightButtonDown="TabsListBox_PreviewMouseRightButtonDown"
"""
    return replace_once(text, old, new, "ContainerWindow.xaml split preview ordering")

def patch_split_partial(text: str) -> str:
    text = replace_method(
        text,
        "    protected override void OnContentRendered(EventArgs e)",
        """    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_splitInteractionHooksAttached)
            return;

        // The non-member click handler is wired in XAML, so it is registered
        // during InitializeComponent BEFORE the ordinary drag/selection guard
        // that ContainerWindow.xaml.cs adds later. One routed-event pass now
        // owns pair -> C/D activation; there is no handledEventsToo recovery
        // handler and no second hit-test after another handler has swallowed
        // the event.
        _viewModel.DisplayTabs.CollectionChanged += SplitDisplayTabs_CollectionChanged;
        _splitInteractionHooksAttached = true;
    }""",
        "SplitInteraction.OnContentRendered",
    )

    text = replace_method(
        text,
        "    protected override void OnClosed(EventArgs e)",
        """    protected override void OnClosed(EventArgs e)
    {
        if (_splitInteractionHooksAttached)
        {
            _viewModel.DisplayTabs.CollectionChanged -= SplitDisplayTabs_CollectionChanged;
            _splitInteractionHooksAttached = false;
        }
        DisarmSplitPresentationSettle();
        base.OnClosed(e);
    }""",
        "SplitInteraction.OnClosed",
    )
    return text

def patch_container_cs(text: str) -> str:
    old = """        if (ensureFinalPass)
            _relayoutAfterPending = true;
        if (_relayoutPending)
            return;
        _relayoutPending = true;
"""
    new = """        if (_relayoutPending)
        {
            if (ensureFinalPass)
                _relayoutAfterPending = true;
            return;
        }
        _relayoutPending = true;
"""
    text = replace_once(text, old, new, "ContainerWindow final-pass latch")

    text = replace_once(
        text,
        "if (NativeMethods.GetWindow(top.Hwnd, NativeMethods.GW_HWNDNEXT) != bottom.Hwnd)",
        "if (!IsWindowAbove(top.Hwnd, bottom.Hwnd))",
        "ContainerWindow split z-order adjacency",
    )

    marker = "    #region Split screen\n"
    helper = """    /// <summary>
    /// Returns true when <paramref name="upper"/> occurs anywhere above
    /// <paramref name="lower"/> in the top-level z-order. The split compositor
    /// cares about relative order, not strict adjacency: IME, accessibility,
    /// overlay and shell helper HWNDs can legally sit between two TabDock guests.
    /// </summary>
    private static bool IsWindowAbove(IntPtr upper, IntPtr lower)
    {
        if (upper == IntPtr.Zero || lower == IntPtr.Zero || upper == lower)
            return false;

        for (IntPtr hwnd = upper; hwnd != IntPtr.Zero; hwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT))
        {
            if (hwnd == lower)
                return true;
        }
        return false;
    }

"""
    text = replace_once(text, marker, helper + marker, "ContainerWindow z-order helper insertion")

    text = replace_once(
        text,
        "        LayoutUpdated += (_, _) => RequestRelayout();\n",
        "        LayoutUpdated += ContainerWindow_LayoutUpdated;\n",
        "ContainerWindow LayoutUpdated subscription",
    )

    fields_anchor = "    private bool _relayoutAfterPending;\n"
    fields_new = """    private bool _relayoutAfterPending;
    private bool _hasObservedContentRect;
    private NativeMethods.RECT _lastObservedContentRect;
"""
    text = replace_once(text, fields_anchor, fields_new, "ContainerWindow content rect cache fields")

    request_signature = "    private void RequestRelayout(bool ensureFinalPass = false)"
    idx = text.find(request_signature)
    if idx < 0:
        raise RuntimeError("ContainerWindow RequestRelayout signature missing")
    guard_method = """    private void ContainerWindow_LayoutUpdated(object? sender, EventArgs e)
    {
        if (_containerHwnd == IntPtr.Zero || ContentHost.HostWindowHandle == IntPtr.Zero)
            return;

        NativeMethods.RECT rect = GetContentAreaScreenRect();
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        if (_hasObservedContentRect
            && Math.Abs(rect.left - _lastObservedContentRect.left) <= 1
            && Math.Abs(rect.top - _lastObservedContentRect.top) <= 1
            && Math.Abs(rect.right - _lastObservedContentRect.right) <= 1
            && Math.Abs(rect.bottom - _lastObservedContentRect.bottom) <= 1)
        {
            return;
        }

        _lastObservedContentRect = rect;
        _hasObservedContentRect = true;
        RequestRelayout();
    }

"""
    text = text[:idx] + guard_method + text[idx:]

    close_anchor = "        try { TabsListBox.PreviewMouseLeftButtonDown -= TabsListBox_PreviewMouseLeftButtonDown; } catch { }\n"
    close_new = close_anchor + "        try { LayoutUpdated -= ContainerWindow_LayoutUpdated; } catch { }\n"
    text = replace_once(text, close_anchor, close_new, "ContainerWindow LayoutUpdated unsubscribe")
    return text

PATCHERS = {
    "Services/GroupManager.cs": patch_group_manager,
    "Services/WindowShepherdService.cs": patch_window_shepherd,
    "Services/PresentationLayoutCoordinator.cs": patch_layout_coordinator,
    "Infrastructure/NativeHwndHost.cs": patch_native_host,
    "Views/ContainerWindow.xaml": patch_container_xaml,
    "Views/ContainerWindow.SplitInteractionFix.cs": patch_split_partial,
    "Views/ContainerWindow.xaml.cs": patch_container_cs,
}

def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("repo", type=Path)
    mode = p.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true")
    mode.add_argument("--apply", action="store_true")
    args = p.parse_args()

    repo = args.repo.resolve()
    changed = {}
    for rel, fn in PATCHERS.items():
        path = repo / rel
        if not path.is_file():
            raise RuntimeError(f"missing expected file: {path}")
        old = path.read_text(encoding="utf-8")
        new = fn(old)
        if old == new:
            raise RuntimeError(f"{rel}: patch produced no change")
        changed[path] = (old, new)

    print(f"TabDock runtime hotfix baseline: {BASELINE}")
    print(f"Validated {len(changed)} files; all anchors matched.")
    for path, (old, new) in changed.items():
        plus = minus = 0
        for line in difflib.unified_diff(old.splitlines(), new.splitlines(), lineterm=""):
            if line.startswith("+") and not line.startswith("+++"):
                plus += 1
            elif line.startswith("-") and not line.startswith("---"):
                minus += 1
        print(f"  {path.relative_to(repo)}: +{plus} -{minus}")

    if args.check:
        print("CHECK ONLY: no files modified.")
        return 0

    for path, (_, new) in changed.items():
        path.write_text(new, encoding="utf-8", newline="\n")
    print("APPLIED. No git add/commit/push was performed.")
    return 0

if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(2)
