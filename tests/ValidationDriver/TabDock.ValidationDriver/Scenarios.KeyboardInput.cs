using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace TabDock.ValidationDriver;

internal static partial class Scenarios
{
    // -------------------------------------------------------------------------
    // 17. contentinput (Test A): non-Chromium content-area input gate.
    //     Clicks the center counter button of a captured GuineaPig and verifies
    //     the guest actually receives the input.
    // -------------------------------------------------------------------------
    private static void ContentInput(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "CI", "--color", "blue", "--click-counter-button");
        Thread.Sleep(2000); // extra settle time for the button-hosted pig before picker enumeration
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;
        GuardedProc.Log($"  ContentInput: clicking center of host client area at ({cx},{cy}); hostClient={Util.FormatRect(hostClient)}.");

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured guest to the foreground — refusing to click blind.");

        Input.ClickAt(cx, cy);
        bool clicked = PigLog.WaitForPigLine(pig.Pid, "BUTTON_CLICK count=1", 2000);
        ctx.Check(clicked, "GuineaPig content-area button received the click (BUTTON_CLICK count=1)");

        // Also exercise drag: start well outside the button and drag across the
        // content area; the count must not increment from a drag that never
        // presses the button.
        Input.DragFromTo(cx - 150, cy, cx + 150, cy, 12);
        Thread.Sleep(200);
        bool noExtraClick = !PigLog.ContainsLine(pig.Pid, "BUTTON_CLICK count=2");
        ctx.Check(noExtraClick, "drag across the content area did not produce a second button click (guest saw mouse motion)");
    }

    // -------------------------------------------------------------------------
    // 18. chromeinput (Test B): Chromium input recovery after activation fix.
    // -------------------------------------------------------------------------
    private static void ChromeInput(Ctx ctx, Options opt)
    {
        string htmlPath = CreateChromeInputTestPage();
        GuestInfo chrome = SpawnClassGuest(ctx, ChromeExe,
            $"--user-data-dir=\"{FreshProfileDir("TabDockChromeProfile")}\" --disable-gpu --app=\"{htmlPath}\"",
            "Chrome_WidgetWin_1", useShellExecute: true);

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, chrome);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;
        GuardedProc.Log($"  ChromeInput: clicking center of host client area at ({cx},{cy}); hostClient={Util.FormatRect(hostClient)}.");

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured Chrome guest to the foreground — refusing to click blind.");

        // The page starts white; the centered button turns the background green.
        Input.ClickAt(cx, cy);
        Thread.Sleep(1000);

        int[]? frame = Pixels.CaptureHostScreenArea(host);
        char dominant = frame != null ? Pixels.DominantChannel(frame) : '?';
        GuardedProc.Log($"  ChromeInput: after click dominant channel='{dominant}'.");
        ctx.Check(dominant == 'g', $"Chrome page turned green after click (dominant channel='{dominant}')");
    }

    private static string CreateChromeInputTestPage()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TabDock-Validation");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "chrome-input-test.html");
        File.WriteAllText(path, @"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><style>
body { margin: 0; width: 100vw; height: 100vh; background: white; display: flex; align-items: center; justify-content: center; }
button { padding: 24px 48px; font-size: 24px; }
</style></head>
<body>
<button id='btn'>Click me</button>
<script>
document.getElementById('btn').addEventListener('click', function() {
    document.body.style.backgroundColor = '#00aa00';
});
</script>
</body>
</html>");
        return path;
    }

    // -------------------------------------------------------------------------
    // 19. alttabinput (Test D): container reactivation after alt-tab away/back.
    // -------------------------------------------------------------------------
    private static void AltTabInput(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "AT", "--color", "blue", "--click-counter-button");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured guest to the foreground — refusing to click blind.");

        // Baseline click: establish the guest is responsive.
        Input.ClickAt(cx, cy);
        ctx.Check(PigLog.WaitForPigLine(pig.Pid, "BUTTON_CLICK count=1", 2000),
            "baseline click received (count=1)");

        // Switch focus away from the container to the driver's own console window.
        IntPtr driverHwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (driverHwnd == IntPtr.Zero)
        {
            // Fallback: spawn a Notepad to receive focus. Use the safe helper so we
            // never capture or kill an existing user Notepad.
            GuestInfo notepad = SpawnNotepad(ctx);
            driverHwnd = notepad.Hwnd;
        }

        Input.ForceForegroundRoot(driverHwnd);
        Thread.Sleep(800);

        // Switch focus back to the container (simulates alt-tab back).
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container back to the foreground.");
        Thread.Sleep(500);

        // Click the guest again; the WM_ACTIVATE-forwarding path should have re-activated it.
        Input.ClickAt(cx, cy);
        ctx.Check(PigLog.WaitForPigLine(pig.Pid, "BUTTON_CLICK count=2", 2000),
            "click after alt-tab-back received (count=2)");
    }

    // -------------------------------------------------------------------------
    // 20. keyboardinput (H8 baseline): real keyboard typing must land in a
    //     captured non-Chromium guest's editable control.
    // -------------------------------------------------------------------------
    private static void KeyboardInput(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "KI", "--color", "blue", "--text-box");
        Thread.Sleep(2000); // let the text-box control realize before picker enumeration
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;
        GuardedProc.Log($"  KeyboardInput: clicking center of host client area at ({cx},{cy}); hostClient={Util.FormatRect(hostClient)}.");

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured guest to the foreground — refusing to type blind.");

        Input.ClickAt(cx, cy);
        Thread.Sleep(300);

        const string typed = "H8TEST";
        Input.TypeText(typed);
        bool received = PigLog.WaitForPigLine(pig.Pid, $"TEXTBOX text='{typed}'", 3000);
        ctx.Check(received, $"GuineaPig text box received typed string '{typed}'");
    }

    // -------------------------------------------------------------------------
    // 21. keyboardinput-chrome (H8 baseline): real keyboard typing must land
    //     in a captured Chrome guest's <input> field.
    // -------------------------------------------------------------------------
    private static void KeyboardInputChrome(Ctx ctx, Options opt)
    {
        string htmlPath = CreateChromeKeyboardTestPage();
        GuestInfo chrome = SpawnClassGuest(ctx, ChromeExe,
            $"--user-data-dir=\"{FreshProfileDir("TabDockChromeProfile")}\" --disable-gpu --app=\"{htmlPath}\"",
            "Chrome_WidgetWin_1", useShellExecute: true);

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, chrome);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;
        GuardedProc.Log($"  KeyboardInputChrome: clicking center of host client area at ({cx},{cy}); hostClient={Util.FormatRect(hostClient)}.");

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured Chrome guest to the foreground — refusing to type blind.");

        Input.ClickAt(cx, cy);
        Thread.Sleep(300);

        const string typed = "H8TEST";
        Input.TypeText(typed);

        string titlePrefix = $"TYPED:{typed}";
        bool titleChanged = Util.WaitUntil(() =>
            (NativeMethods.GetWindowTextString(chrome.Hwnd) ?? string.Empty).StartsWith(titlePrefix, StringComparison.Ordinal),
            3000);
        ctx.Check(titleChanged, $"Chrome page title reflects typed string '{typed}' (title prefix '{titlePrefix}')");
    }

    // -------------------------------------------------------------------------
    // keyboardinput-{chrome,edge}-altswitch: reproduces the reported bug
    // directly — type into a captured browser's content <input>, switch focus
    // to an external app TabDock never captured (a genuine alt-tab-away, NOT
    // another TabDock tab), switch back, and type again. Unlike
    // keyboardinput-rapid-switch (which only switches between two TABS within
    // the same container, always via SW_HIDE/SW_SHOW and NativeHwndHost's
    // SwitchActiveWindow) this exercises ContainerWindow's own
    // WM_ACTIVATE(WA_ACTIVE/WA_INACTIVE) handling, which is the code path
    // implicated by the user report: "keyboard input works the first time...
    // after switching to another app and then returning to the browser app,
    // I can no longer type" (reproduced live with real Edge and Chrome search
    // bars). The click target is the host's geometric center (like
    // keyboardinput-chrome) — a UIA-based lookup of the actual <input>
    // element's BoundingRectangle was tried and rejected: for this captured
    // guest, Chrome/Edge's own UIA provider reliably reports
    // Rect.Empty for that element (never becomes valid, even after minutes),
    // so it cannot be used as a click target here. Full browser-chrome mode
    // (needed to test the omnibox itself) was also tried and rejected — on
    // this dev machine it's entangled with the signed-in Edge/Chrome
    // profile's own enterprise tab-sync, which reproducibly opens unrelated
    // real tabs (confirmed via screenshot) within seconds of spawning even a
    // "fresh" --user-data-dir window, making it unusable for deterministic
    // verification. This still targets the exact same WM_ACTIVATE code path
    // as the omnibox.
    // -------------------------------------------------------------------------
    // -------------------------------------------------------------------------
    // keyboardinput-chrome-omnibox-altswitch (DIAGNOSTIC): same alt-tab-away/
    // back cycle, but against the omnibox itself (full browser-chrome mode,
    // Chrome only — Edge on this dev machine is entangled with enterprise
    // tab-sync that reproducibly steals the window). The omnibox is native
    // Views UI drawn by the browser process's own thread, not web content
    // routed through a renderer process — it may not share whatever deeper
    // reparenting limitation affects typing into a page <input> (see
    // KeyboardInputBrowserAltSwitch's notes).
    // -------------------------------------------------------------------------
    private static void KeyboardInputChromeOmniboxAltSwitch(Ctx ctx, Options opt)
    {
        string pageA = CreateNamedTestPage("TDVAL-OMNI-A");
        string pageB = CreateNamedTestPage("TDVAL-OMNI-B");
        string uriA = new Uri(pageA).AbsoluteUri;
        string uriB = new Uri(pageB).AbsoluteUri;

        GuestInfo browser = SpawnClassGuest(ctx, ChromeExe,
            $"--user-data-dir=\"{FreshProfileDir("TabDockOmniChromeProfile")}\" --no-first-run --no-default-browser-check --disable-session-crashed-bubble --disable-sync about:blank",
            "Chrome_WidgetWin_1", useShellExecute: true);

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, browser);
        Thread.Sleep(2500);

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured Chrome guest to the foreground — refusing to type blind.");

        Input.SendCtrlL();
        Thread.Sleep(300);
        Input.TypeText(uriA);
        Thread.Sleep(200);
        Input.SendKey(Input.VK_RETURN);
        bool baselineOk = Util.WaitUntil(() =>
            (NativeMethods.GetWindowTextString(browser.Hwnd) ?? string.Empty).Contains("TDVAL-OMNI-A", StringComparison.Ordinal),
            5000);
        ctx.Check(baselineOk, "Chrome omnibox: typed URL navigated correctly before any app switch");

        IntPtr externalHwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (externalHwnd == IntPtr.Zero)
        {
            GuestInfo notepad = SpawnNotepad(ctx);
            externalHwnd = notepad.Hwnd;
        }
        Input.ForceForegroundRoot(externalHwnd);
        Thread.Sleep(800);

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container back to the foreground after switching away.");
        Thread.Sleep(600);

        Input.SendCtrlL();
        Thread.Sleep(300);
        Input.TypeText(uriB);
        Thread.Sleep(200);
        Input.SendKey(Input.VK_RETURN);
        bool postSwitchOk = Util.WaitUntil(() =>
            (NativeMethods.GetWindowTextString(browser.Hwnd) ?? string.Empty).Contains("TDVAL-OMNI-B", StringComparison.Ordinal),
            5000);
        ctx.Check(postSwitchOk, "Chrome omnibox: typed URL navigated correctly after switching to an external app and back — THE REPORTED BUG");
    }

    private static void KeyboardInputChromeAltSwitch(Ctx ctx, Options opt) =>
        KeyboardInputBrowserAltSwitch(ctx, ChromeExe, "Chrome_WidgetWin_1", "Chrome");

    private static void KeyboardInputEdgeAltSwitch(Ctx ctx, Options opt) =>
        KeyboardInputBrowserAltSwitch(ctx, EdgeExe, "Chrome_WidgetWin_1", "Edge");

    private static void KeyboardInputBrowserAltSwitch(Ctx ctx, string exe, string className, string label)
    {
        string htmlPath = CreateChromeKeyboardTestPage();
        GuestInfo browser = SpawnClassGuest(ctx, exe,
            $"--user-data-dir=\"{FreshProfileDir("TabDockAltSwitchProfile")}\" --disable-gpu --no-first-run --no-default-browser-check --disable-session-crashed-bubble --app=\"{htmlPath}\"",
            className, useShellExecute: true);

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, browser);

        // Let post-capture settling finish (render-health check, debounced
        // persistence save, any foreground contention against the terminal
        // that launched this driver) before touching the guest, so the
        // baseline measurement isn't itself confounded by that transient
        // activity.
        Thread.Sleep(2500);

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException($"Could not bring the captured {label} guest to the foreground — refusing to type blind.");

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;

        // Baseline: click + type must land.
        Input.ClickAt(cx, cy);
        Thread.Sleep(300);
        Input.TypeText("PRESWITCH");
        bool baselineOk = Util.WaitUntil(() =>
            (NativeMethods.GetWindowTextString(browser.Hwnd) ?? string.Empty).Contains("TYPED:PRESWITCH", StringComparison.Ordinal),
            5000);
        ctx.Check(baselineOk, $"{label}: baseline typed text landed before any app switch");

        // Switch focus to a genuinely external app TabDock never captured —
        // the driver's own console window, falling back to a throwaway
        // Notepad. This must NOT be another TabDock tab (that path hides/
        // shows via SW_HIDE and never touches ContainerWindow's own
        // WM_ACTIVATE handler, which is what this scenario targets).
        IntPtr externalHwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (externalHwnd == IntPtr.Zero)
        {
            GuestInfo notepad = SpawnNotepad(ctx);
            externalHwnd = notepad.Hwnd;
        }
        Input.ForceForegroundRoot(externalHwnd);
        Thread.Sleep(800);

        // Switch back — the exact user action ("returning to the browser
        // app"): alt-tab/click back to the TabDock container itself, no
        // tab-strip click involved.
        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container back to the foreground after switching away.");
        Thread.Sleep(600);

        // Type WITHOUT clicking first: the input field already had both
        // Win32 focus and the page's own caret before the switch away, so if
        // activation/focus genuinely round-trips, no re-click should be
        // required. This is the precise assertion for the reported bug.
        Input.TypeText("POSTSWITCH");
        bool postSwitchNoClickOk = Util.WaitUntil(() =>
            (NativeMethods.GetWindowTextString(browser.Hwnd) ?? string.Empty).Contains("POSTSWITCH", StringComparison.Ordinal),
            5000);
        ctx.Check(postSwitchNoClickOk, $"{label}: typed text (no re-click) landed after switching to an external app and back — THE REPORTED BUG");

        // Then try again after an explicit click, to distinguish "totally
        // dead" from "needs a click to re-arm" (still a real bug, just a
        // lesser one than the no-click case above).
        Input.ClickAt(cx, cy);
        Thread.Sleep(300);
        Input.TypeText("POSTCLICK");
        bool postClickOk = Util.WaitUntil(() =>
            (NativeMethods.GetWindowTextString(browser.Hwnd) ?? string.Empty).Contains("POSTCLICK", StringComparison.Ordinal),
            5000);
        ctx.Check(postClickOk, $"{label}: typed text landed after an explicit re-click following the app switch");
    }

    /// <summary>A minimal local HTML page with a distinctive, fixed &lt;title&gt; used to detect a successful omnibox navigation.</summary>
    private static string CreateNamedTestPage(string title)
    {
        string dir = Path.Combine(Path.GetTempPath(), "TabDock-Validation");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"{title}.html");
        File.WriteAllText(path, $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>{title}</title></head><body>{title}</body></html>");
        return path;
    }

    // -------------------------------------------------------------------------
    // 22. keyboardinput-notepad (H8 isolation): real keyboard typing must land
    //     in a captured Notepad edit/document control. This isolates whether the
    //     non-Chromium failure is specific to the WinForms guinea pig or general.
    // -------------------------------------------------------------------------
    private static void KeyboardInputNotepad(Ctx ctx, Options opt)
    {
        GuestInfo notepad = SpawnNotepad(ctx);
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, notepad);

        NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
        int cx = hostClient.left + hostClient.Width / 2;
        int cy = hostClient.top + hostClient.Height / 2;
        GuardedProc.Log($"  KeyboardInputNotepad: clicking center of host client area at ({cx},{cy}); hostClient={Util.FormatRect(hostClient)}.");

        if (!Input.ForceForegroundRoot(host))
            throw new InvalidOperationException("Could not bring the captured Notepad guest to the foreground — refusing to type blind.");

        Input.ClickAt(cx, cy);
        Thread.Sleep(300);

        const string typed = "H8TEST";
        Input.TypeText(typed);

        // Read the Notepad edit/document control via UIA ValuePattern.
        string? value = null;
        int editCount = 0;
        bool readOk = Util.WaitUntil(() =>
        {
            AutomationElement? root = Uia.FromHwnd(notepad.Hwnd);
            if (root == null)
                return false;
            AutomationElement? edit = Uia.FindEditOrDocument(root, out editCount);
            if (edit == null)
                return false;
            value = Uia.GetValue(edit);
            return value != null;
        }, 3000, 150);

        GuardedProc.Log($"  KeyboardInputNotepad: editCount={editCount}, readOk={readOk}, value='{value ?? "<null>"}'.");
        ctx.Check(readOk, "Notepad edit/document control value read via UIA");
        ctx.Check(value != null && value.Contains(typed, StringComparison.Ordinal),
            $"Notepad edit control contains typed string '{typed}' (value='{value ?? "<null>"}')");
    }

    private static string CreateChromeKeyboardTestPage()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TabDock-Validation");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "chrome-keyboard-test.html");
        File.WriteAllText(path, @"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><title>Chrome Keyboard Test</title><style>
body { margin: 0; width: 100vw; height: 100vh; background: white; display: flex; align-items: center; justify-content: center; }
input { padding: 16px 24px; font-size: 24px; width: 60vw; }
</style></head>
<body>
<input id='txt' autofocus autocomplete='off' autocapitalize='off' spellcheck='false'>
<script>
var input = document.getElementById('txt');
function claimFocus() { input.focus(); }
window.addEventListener('load', claimFocus);
document.body.addEventListener('click', claimFocus);
input.addEventListener('input', function() {
    document.title = 'TYPED:' + input.value;
});
</script>
</body>
</html>");
        return path;
    }

    // -------------------------------------------------------------------------
    // 23. keyboardinput-rapid-switch (H8 stress): keyboard input must land after
    //     switching between two captured guests. Exercises the attach/detach
    //     lifecycle so a leak or stale attachment cannot hide behind a single tab.
    // -------------------------------------------------------------------------
    private static void KeyboardInputRapidSwitch(Ctx ctx, Options opt)
    {
        GuestInfo pig = SpawnPig(ctx, "KIRS", "--color", "blue", "--text-box");
        GuestInfo notepad = SpawnNotepad(ctx);
        Thread.Sleep(3000); // let guests realize before picker enumeration
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pig, notepad);
        Thread.Sleep(1500); // let the container's UIA tab tree settle before switching

        void TypeIntoHost(string text)
        {
            NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
            int cx = hostClient.left + hostClient.Width / 2;
            int cy = hostClient.top + hostClient.Height / 2;
            if (!Input.ForceForegroundRoot(host))
                throw new InvalidOperationException("Could not bring the captured guest to the foreground — refusing to type blind.");
            Input.ClickAt(cx, cy);
            Thread.Sleep(300);
            Input.TypeText(text);
        }

        void SwitchToTab(string title)
        {
            // Find the selectable ListBoxItem directly; walking up from the inner
            // Text element is fragile when virtualization has not materialized the
            // ancestor. Fall back to text+ancestor only if the ListItem search fails.
            AutomationElement? item = null;
            string lastError = "not searched";
            for (int attempt = 0; attempt < 10 && item == null; attempt++)
            {
                try
                {
                    AutomationElement? list = GetTabList(container);
                    if (list != null)
                    {
                        item = Uia.FindDescendantByName(list, ControlType.ListItem, null, title, out int listCount);
                        if (item == null)
                            lastError = $"ListItem not found (count={listCount})";
                    }
                    else
                    {
                        lastError = "tab list not found";
                    }

                    if (item == null)
                    {
                        AutomationElement? text = FindTabText(container, title, out int textCount);
                        if (text != null && textCount == 1)
                            item = Uia.NearestAncestorOfType(text, ControlType.ListItem);
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
                if (item == null)
                    Thread.Sleep(200);
            }
            if (item == null)
                throw new InvalidOperationException($"Tab '{title}' ListBoxItem not found ({lastError}).");

            // Keep the action on the same real-input path as a user click. UIA is
            // read-only discovery/assertion infrastructure in this harness.
            bool selected = false;
            for (int attempt = 0; attempt < 3 && !selected; attempt++)
            {
                if (!Input.ForceForeground(container))
                    throw new InvalidOperationException("Could not bring the container to the foreground — refusing to select blind.");

                (int tx, int ty) = Uia.Center(item);
                Input.ClickAt(tx, ty);
                Thread.Sleep(350);
                selected = Uia.IsSelected(item) == true;
            }
            if (!selected)
                throw new InvalidOperationException($"Tab '{title}' did not become selected.");
        }

        string? ReadNotepadValue()
        {
            string? value = null;
            Util.WaitUntil(() =>
            {
                AutomationElement? root = Uia.FromHwnd(notepad.Hwnd);
                if (root == null)
                    return false;
                AutomationElement? edit = Uia.FindEditOrDocument(root, out _);
                if (edit == null)
                    return false;
                value = Uia.GetValue(edit);
                return value != null;
            }, 3000, 150);
            return value;
        }

        // Start on Notepad.
        SwitchToTab(notepad.Title);
        TypeIntoHost("NOTEPAD-A");
        string? valueAfterA = ReadNotepadValue();
        ctx.Check(valueAfterA != null && valueAfterA.Contains("NOTEPAD-A", StringComparison.Ordinal),
            $"Notepad contains 'NOTEPAD-A' after initial type (value='{valueAfterA ?? "<null>"}')");

        // Switch to the pig tab and type there.
        SwitchToTab(pig.Title);
        TypeIntoHost("PIG-B");
        bool pigReceived = PigLog.WaitForPigLine(pig.Pid, "TEXTBOX text='PIG-B'", 3000);
        ctx.Check(pigReceived, "GuineaPig text box received 'PIG-B' after switch");

        // Switch back to Notepad and type again.
        SwitchToTab(notepad.Title);
        TypeIntoHost("NOTEPAD-C");
        string? valueAfterC = ReadNotepadValue();
        ctx.Check(valueAfterC != null && valueAfterC.Contains("NOTEPAD-C", StringComparison.Ordinal),
            $"Notepad contains 'NOTEPAD-C' after switch-back (value='{valueAfterC ?? "<null>"}')");
    }

    // -------------------------------------------------------------------------
    // 25. realworkflow-altswitch: the closest automated proxy to the originally
    //     reported real-world workflow — a real captured browser AND a second
    //     real captured guest (Notepad) in ONE group, with a genuine
    //     external-app alt-tab in between (never just a TabDock-internal tab
    //     switch). Exercises the exact reported bug (typing after
    //     alt-tab-away/back with no re-click) interleaved with ordinary
    //     tab-strip switching between two real captured guests.
    // -------------------------------------------------------------------------
    private static void RealWorkflowAltSwitch(Ctx ctx, Options opt)
    {
        string htmlPath = CreateChromeKeyboardTestPage();
        GuestInfo browser = SpawnClassGuest(ctx, ChromeExe,
            $"--user-data-dir=\"{FreshProfileDir("TabDockRealWorkflowProfile")}\" --disable-gpu --no-first-run --no-default-browser-check --disable-session-crashed-bubble --app=\"{htmlPath}\"",
            "Chrome_WidgetWin_1", useShellExecute: true);
        GuestInfo notepad = SpawnNotepad(ctx);

        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, browser, notepad);
        Thread.Sleep(1500); // let post-capture settling (render-health, debounced save) finish

        // A genuinely external app TabDock never captured (never another
        // TabDock tab) — the driver's own console window, falling back to a
        // throwaway pig. NOT a second Notepad: Windows 11's built-in Notepad
        // is a single-instance, multi-tab app, so a second "notepad.exe <file>"
        // launch just opens another tab in the SAME process as the Notepad
        // already captured above, rather than a genuinely separate window —
        // confirmed live (SpawnNotepad's own "reused existing process"
        // warning fired with the identical PID as the captured guest).
        IntPtr externalHwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (externalHwnd == IntPtr.Zero)
        {
            GuestInfo externalPig = SpawnPig(ctx, "RWAEXT", "--color", "white");
            externalHwnd = externalPig.Hwnd;
            // A newly-launched process's first window is typically granted
            // automatic foreground by Windows as it appears — reclaim it for
            // the container now, before the iteration loop assumes the
            // container/browser already has real foreground from capture.
            if (!Input.ForceForeground(container))
                throw new InvalidOperationException("Could not reclaim the foreground for the container after spawning the external pig.");
            Thread.Sleep(300);
        }

        // The browser's own window title (and therefore its tab-strip label,
        // which mirrors the live window title) changes completely every time
        // CreateChromeKeyboardTestPage's page reflects newly typed text into
        // document.title, so it cannot be re-found by its original title the
        // way FindTabText does elsewhere. Instead, find "the tab that is NOT
        // Notepad" by elimination against Notepad's own stable title.
        AutomationElement? FindOtherTab(string excludeNameContains, out int count)
        {
            count = 0;
            AutomationElement? found = null;
            AutomationElement? list = GetTabList(container);
            if (list == null)
                return null;
            try
            {
                AutomationElementCollection all = list.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                foreach (AutomationElement el in all)
                {
                    string name;
                    try { name = el.Current.Name ?? string.Empty; }
                    catch { continue; }
                    if (name.IndexOf(excludeNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    count++;
                    found ??= el;
                }
            }
            catch
            {
            }
            return found;
        }

        void EnsureBrowserActive()
        {
            if (IsDocked(browser.Hwnd, host))
                return;
            if (!Input.ForceForeground(container))
                throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
            AutomationElement? tab = FindOtherTab(notepad.Title, out int count);
            if (tab == null || count != 1)
                throw new InvalidOperationException($"Browser tab not found uniquely by elimination against Notepad's tab (count={count}).");
            (int tx, int ty) = Uia.Center(tab);
            Input.ClickAt(tx, ty);
            Util.WaitUntil(() => IsDocked(browser.Hwnd, host), 3000);
        }

        void SwitchToNotepad()
        {
            if (!Input.ForceForeground(container))
                throw new InvalidOperationException("Could not bring the container to the foreground — refusing to click blind.");
            AutomationElement? tab = FindTabText(container, notepad.Title, out int count);
            if (tab == null || count != 1)
                throw new InvalidOperationException($"Notepad tab not found uniquely (count={count}).");
            (int tx, int ty) = Uia.Center(tab);
            Input.ClickAt(tx, ty);
            Util.WaitUntil(() => IsDocked(notepad.Hwnd, host), 3000);
        }

        string? ReadNotepadValue()
        {
            string? value = null;
            Util.WaitUntil(() =>
            {
                AutomationElement? root = Uia.FromHwnd(notepad.Hwnd);
                if (root == null)
                    return false;
                AutomationElement? edit = Uia.FindEditOrDocument(root, out _);
                if (edit == null)
                    return false;
                value = Uia.GetValue(edit);
                return value != null;
            }, 3000, 150);
            return value;
        }

        void ClickHostCenterAndType(string text)
        {
            NativeMethods.RECT hostClient = Discover.GetClientScreenRect(host);
            int cx = hostClient.left + hostClient.Width / 2;
            int cy = hostClient.top + hostClient.Height / 2;
            if (!Input.ForceForegroundRoot(host))
                throw new InvalidOperationException("Could not bring the captured guest to the foreground — refusing to type blind.");
            Input.ClickAt(cx, cy);
            Thread.Sleep(300);
            Input.TypeText(text);
        }

        const int iterations = 3;
        for (int i = 1; i <= iterations; i++)
        {
            GuardedProc.Log($"  --- realworkflow-altswitch iteration {i}/{iterations} ---");

            // 1) Ensure the browser tab is active, click into its input, type, verify.
            EnsureBrowserActive();
            ctx.Check(IsDocked(browser.Hwnd, host), $"iteration {i}: browser tab is active/docked before typing");
            string typedA = $"RWA{i}";
            ClickHostCenterAndType(typedA);
            ctx.Check(Util.WaitUntil(() => (NativeMethods.GetWindowTextString(browser.Hwnd) ?? string.Empty).Contains(typedA, StringComparison.Ordinal), 5000),
                $"iteration {i}: browser typed text '{typedA}' landed after a direct click into its input");

            // 2) Alt-tab away to a genuinely external app (never another TabDock tab).
            Input.ForceForegroundRoot(externalHwnd);
            Thread.Sleep(800);

            // 3) Alt-tab back to the container and type WITHOUT clicking first —
            //    this is the precise reported-bug assertion.
            if (!Input.ForceForeground(container))
                throw new InvalidOperationException("Could not bring the container back to the foreground after switching away.");
            Thread.Sleep(600);
            string typedB = $"RWB{i}";
            Input.TypeText(typedB);
            ctx.Check(Util.WaitUntil(() => (NativeMethods.GetWindowTextString(browser.Hwnd) ?? string.Empty).Contains(typedB, StringComparison.Ordinal), 5000),
                $"iteration {i}: browser typed text '{typedB}' (NO re-click) landed after an external alt-tab away and back — THE REPORTED BUG");

            // 4) Switch to the OTHER tab (Notepad), click into it, type, verify.
            SwitchToNotepad();
            ctx.Check(IsDocked(notepad.Hwnd, host), $"iteration {i}: Notepad tab is active/docked before typing");
            string typedNotepad = $"RWN{i}";
            ClickHostCenterAndType(typedNotepad);
            string? notepadValue = ReadNotepadValue();
            ctx.Check(notepadValue != null && notepadValue.Contains(typedNotepad, StringComparison.Ordinal),
                $"iteration {i}: Notepad contains typed text '{typedNotepad}' (value='{notepadValue ?? "<null>"}')");

            // 5) Switch back to the browser tab, click into it, type, verify.
            EnsureBrowserActive();
            ctx.Check(IsDocked(browser.Hwnd, host), $"iteration {i}: browser tab is active/docked again after switching back from Notepad");
            string typedC = $"RWC{i}";
            ClickHostCenterAndType(typedC);
            ctx.Check(Util.WaitUntil(() => (NativeMethods.GetWindowTextString(browser.Hwnd) ?? string.Empty).Contains(typedC, StringComparison.Ordinal), 5000),
                $"iteration {i}: browser typed text '{typedC}' landed after switching back from the Notepad tab");
        }

        ctx.Check(browser.Proc != null && !browser.Proc.HasExited && notepad.Proc != null && !notepad.Proc.HasExited,
            "both the browser and Notepad survived the whole alt-switch workflow");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }

    // -------------------------------------------------------------------------
    // 38. keyboard-only-tab-navigation: ContainerWindow_PreviewKeyDown
    //     implements Ctrl+Tab / Ctrl+Shift+Tab as an explicit keyboard shortcut
    //     that cycles ActiveTab — the real "keyboard-only tab switch" mechanism
    //     (TabsListBox itself has Focusable="False" in Views/ContainerWindow.xaml,
    //     confirmed by reading the XAML rather than by running the app, so
    //     plain Tab/Shift+Tab focus traversal can never reach it or drive
    //     arrow-key selection on it). Plain Tab/Shift+Tab is exercised too, but
    //     only as a "must not crash/hang" check, per that same finding.
    // -------------------------------------------------------------------------
    private static void KeyboardOnlyTabNavigation(Ctx ctx, Options opt)
    {
        GuestInfo pigA = SpawnPig(ctx, "KNA", "--color", "red");
        GuestInfo pigB = SpawnPig(ctx, "KNB", "--color", "blue");
        GuestInfo pigC = SpawnPig(ctx, "KNC", "--color", "green");
        (IntPtr container, IntPtr host) = CaptureIntoGroup(ctx, pigA, pigB, pigC);
        ctx.Check(TabCount(container) == 3, "3 tabs after capture");

        if (!Input.ForceForeground(container))
            throw new InvalidOperationException("Could not bring the container to the foreground — refusing to send keyboard input blind.");

        GuestInfo[] pigs = { pigA, pigB, pigC };
        IntPtr FindDocked()
        {
            foreach (GuestInfo g in pigs)
                if (IsDocked(g.Hwnd, host))
                    return g.Hwnd;
            return IntPtr.Zero;
        }

        IntPtr initiallyDocked = FindDocked();
        ctx.Check(initiallyDocked != IntPtr.Zero, "exactly one guest is docked/active before any keyboard switch");

        bool ctrlDown = false;
        try
        {
            Input.SendKeyDown(Input.VK_CONTROL);
            ctrlDown = true;
            Input.SendKey(Input.VK_TAB);
        }
        finally
        {
            if (ctrlDown)
                Input.SendKeyUp(Input.VK_CONTROL);
        }
        Thread.Sleep(400);
        IntPtr dockedAfterOne = FindDocked();
        ctx.Check(dockedAfterOne != IntPtr.Zero && dockedAfterOne != initiallyDocked,
            "Ctrl+Tab changed the active/docked tab with zero mouse clicks on the tab strip");

        ctrlDown = false;
        bool shiftDown = false;
        try
        {
            Input.SendKeyDown(Input.VK_CONTROL);
            ctrlDown = true;
            Input.SendKeyDown(Input.VK_SHIFT);
            shiftDown = true;
            Input.SendKey(Input.VK_TAB);
        }
        finally
        {
            if (shiftDown)
                Input.SendKeyUp(Input.VK_SHIFT);
            if (ctrlDown)
                Input.SendKeyUp(Input.VK_CONTROL);
        }
        Thread.Sleep(400);
        IntPtr dockedAfterBack = FindDocked();
        ctx.Check(dockedAfterBack == initiallyDocked, "Ctrl+Shift+Tab cycled back to the originally-active tab (reverse direction works)");

        // Plain Tab/Shift+Tab focus traversal (no Ctrl) must not throw/hang,
        // even though TabsListBox itself cannot receive focus.
        for (int i = 0; i < 4; i++)
        {
            Input.SendKey(Input.VK_TAB);
            Thread.Sleep(100);
        }
        bool plainShiftDown = false;
        try
        {
            Input.SendKeyDown(Input.VK_SHIFT);
            plainShiftDown = true;
            for (int i = 0; i < 4; i++)
            {
                Input.SendKey(Input.VK_TAB);
                Thread.Sleep(100);
            }
        }
        finally
        {
            if (plainShiftDown)
                Input.SendKeyUp(Input.VK_SHIFT);
        }
        ctx.Check(NativeMethods.IsWindow(container) && NativeMethods.IsWindowEnabled(container),
            "container survived plain Tab/Shift+Tab focus traversal (no crash/hang)");

        ctx.Check(pigA.Proc != null && !pigA.Proc.HasExited && pigB.Proc != null && !pigB.Proc.HasExited && pigC.Proc != null && !pigC.Proc.HasExited,
            "all three pigs alive throughout");
        ctx.Check(TabDockLog.CountNewLines(ctx.LogOffset, "EXCEPTION") == 0, "no EXCEPTION lines in TabDock log");
    }
}
