using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TabDock.ValidationDriver;

/// <summary>
/// Real user input, exclusively via SetCursorPos + SendInput. Nothing in this class
/// posts synthetic messages to specific windows — everything goes through the OS input
/// queue exactly as a human's mouse/keyboard would.
/// </summary>
internal static class Input
{
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_G = 0x47;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_ESCAPE = 0x1B;

    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const int WHEEL_DELTA = 120;

    /// <summary>Real mouse-wheel scroll at (x,y). Positive notches scroll up, negative scroll down.</summary>
    public static void ScrollWheel(int x, int y, int notches)
    {
        MoveTo(x, y);
        Thread.Sleep(30);
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
        input.u.mi = new NativeMethods.MOUSEINPUT
        {
            dwFlags = MOUSEEVENTF_WHEEL,
            mouseData = unchecked((uint)(notches * WHEEL_DELTA)),
        };
        Send(input);
        Thread.Sleep(120);
    }

    /// <summary>
    /// Brings a window verifiably to the foreground before real-input targeting.
    /// The driver usually runs from a terminal/IDE that owns the foreground, so
    /// freshly spawned TabDock windows open BEHIND it and plain
    /// SetForegroundWindow is denied — real clicks at UIA-read coordinates would
    /// then land in whatever covers the target (observed: clicks landing in the
    /// IDE). A benign key-up via SendInput makes this process the last input
    /// source, which grants foreground rights; a TOPMOST pulse is the fallback.
    /// Callers must treat false as "do NOT click".
    /// </summary>
    public static bool ForceForeground(IntPtr hwnd)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (!NativeMethods.IsWindow(hwnd))
                return false;
            if (NativeMethods.GetForegroundWindow() == hwnd)
                return true;

            SendVk(VK_MENU, up: true); // benign key-up; grants foreground-change rights
            Thread.Sleep(30);
            NativeMethods.SetForegroundWindow(hwnd);
            Thread.Sleep(150);
            if (NativeMethods.GetForegroundWindow() == hwnd)
                return true;

            // Fallback: pulse TOPMOST to rise above the covering window, then drop
            // back to the normal band and try again.
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            NativeMethods.SetForegroundWindow(hwnd);
            Thread.Sleep(150);
        }
        bool ok = NativeMethods.GetForegroundWindow() == hwnd;
        if (!ok)
            GuardedProc.Log($"WARNING: could not bring 0x{hwnd.ToInt64():X} to the foreground.");
        return ok;
    }

    /// <summary>ForceForeground on the top-level root of (possibly child) <paramref name="hwnd"/>.</summary>
    public static bool ForceForegroundRoot(IntPtr hwnd)
    {
        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        return ForceForeground(root == IntPtr.Zero ? hwnd : root);
    }

    private static NativeMethods.POINT _savedCursor;
    private static bool _cursorSaved;

    /// <summary>Records the cursor position at run start so it can be restored at run end.</summary>
    public static void SaveCursor()
    {
        _cursorSaved = NativeMethods.GetCursorPos(out _savedCursor);
    }

    public static void RestoreCursor()
    {
        if (_cursorSaved)
            NativeMethods.SetCursorPos(_savedCursor.x, _savedCursor.y);
    }

    public static void MoveTo(int x, int y)
    {
        NativeMethods.SetCursorPos(x, y);
        // Zero-delta nudge so apps see a genuine WM_MOUSEMOVE from the input queue.
        SendMouse(NativeMethods.MOUSEEVENTF_MOVE);
        Thread.Sleep(30);
    }

    public static void ClickAt(int x, int y)
    {
        MoveTo(x, y);
        Thread.Sleep(40);
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(40);
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTUP);
        Thread.Sleep(60);
    }

    /// <summary>
    /// Two clicks well inside the default double-click time. An initial click
    /// activates/gives focus to the target window first, then the actual
    /// double-click is delivered with tight, fixed-position timing so WPF
    /// reports ClickCount==2 reliably from synthetic input.
    /// </summary>
    public static void DoubleClickAt(int x, int y)
    {
        // Pre-activate: a first standalone click so the window is foreground and
        // the following pair isn't consumed by activation.
        MoveTo(x, y);
        Thread.Sleep(40);
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(30);
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTUP);
        Thread.Sleep(250);

        // The double-click pair, same pixel, tight gaps.
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(20);
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTUP);
        Thread.Sleep(40);
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(20);
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTUP);
        Thread.Sleep(60);
    }

    public static void RightClickAt(int x, int y)
    {
        MoveTo(x, y);
        Thread.Sleep(40);
        SendMouse(NativeMethods.MOUSEEVENTF_RIGHTDOWN);
        Thread.Sleep(40);
        SendMouse(NativeMethods.MOUSEEVENTF_RIGHTUP);
        Thread.Sleep(60);
    }

    /// <summary>Press at (x1,y1), interpolate at least 8 move steps (15 ms apart), release at (x2,y2).</summary>
    public static void DragFromTo(int x1, int y1, int x2, int y2, int steps = 10)
    {
        if (steps < 8)
            steps = 8;

        MoveTo(x1, y1);
        Thread.Sleep(60);
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTDOWN);
        for (int i = 1; i <= steps; i++)
        {
            int x = x1 + (x2 - x1) * i / steps;
            int y = y1 + (y2 - y1) * i / steps;
            NativeMethods.SetCursorPos(x, y);
            SendMouse(NativeMethods.MOUSEEVENTF_MOVE);
            Thread.Sleep(15);
        }
        SendMouse(NativeMethods.MOUSEEVENTF_LEFTUP);
        Thread.Sleep(60);
    }

    /// <summary>Types text as KEYEVENTF_UNICODE down/up pairs, one character at a time.</summary>
    public static void TypeText(string text)
    {
        foreach (char ch in text)
        {
            SendUnicode(ch, up: false);
            SendUnicode(ch, up: true);
            Thread.Sleep(15);
        }
    }

    public static void SendKey(ushort vk)
    {
        SendVk(vk, up: false);
        Thread.Sleep(20);
        SendVk(vk, up: true);
        Thread.Sleep(30);
    }

    public static void SendKeyDown(ushort vk)
    {
        SendVk(vk, up: false);
        Thread.Sleep(20);
    }

    public static void SendKeyUp(ushort vk)
    {
        SendVk(vk, up: true);
        Thread.Sleep(20);
    }

    public static void SendHotkeyCtrlAltG()
    {
        SendVk(VK_CONTROL, up: false);
        Thread.Sleep(20);
        SendVk(VK_MENU, up: false);
        Thread.Sleep(20);
        SendVk(VK_G, up: false);
        Thread.Sleep(20);
        SendVk(VK_G, up: true);
        Thread.Sleep(20);
        SendVk(VK_MENU, up: true);
        Thread.Sleep(20);
        SendVk(VK_CONTROL, up: true);
        Thread.Sleep(50);
    }

    private static void SendMouse(uint flags)
    {
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
        input.u.mi = new NativeMethods.MOUSEINPUT { dwFlags = flags };
        Send(input);
    }

    private static void SendVk(ushort vk, bool up)
    {
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
        input.u.ki = new NativeMethods.KEYBDINPUT
        {
            wVk = vk,
            dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0,
        };
        Send(input);
    }

    private static void SendUnicode(char ch, bool up)
    {
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
        input.u.ki = new NativeMethods.KEYBDINPUT
        {
            wVk = 0,
            wScan = ch,
            dwFlags = NativeMethods.KEYEVENTF_UNICODE | (up ? NativeMethods.KEYEVENTF_KEYUP : 0),
        };
        Send(input);
    }

    private static void Send(NativeMethods.INPUT input)
    {
        uint sent = NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != 1)
            GuardedProc.Log($"WARNING: SendInput failed: {NativeMethods.FormatLastError()}");
    }
}
