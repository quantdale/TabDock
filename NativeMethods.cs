using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace TabDock;

/// <summary>
/// Central container for every P/Invoke declaration used by TabDock.
/// All native interop lives here so the rest of the codebase can stay fully managed.
/// </summary>
public static partial class NativeMethods
{
    // -------------------------------------------------------------------------
    // Delegates
    // -------------------------------------------------------------------------
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
    public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    // -------------------------------------------------------------------------
    // user32.dll
    // -------------------------------------------------------------------------
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    // DRIVER-ONLY: used by tests/ValidationDriver via link-include
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    // Atomic multi-window positioning (used to move both split panes plus
    // the container in a single compositor transaction, eliminating the
    // visible pane separation that separate SetWindowPos calls produce).
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr BeginDeferWindowPos(int nNumWindows);

    // HDWP is an opaque pointer-sized handle. DeferWindowPos may return a new
    // HDWP when it grows the internal transaction, so callers must carry the
    // returned value into the next append and into EndDeferWindowPos.
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr DeferWindowPos(IntPtr hWinPosInfo, IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);

    // DRIVER-ONLY: used by tests/ValidationDriver via link-include
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true, EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true, EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Test-harness only: not called by production code (see ValidationDriver's Input.ForceForeground).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    // DRIVER-ONLY: used by tests/ValidationDriver via link-include
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // DRIVER-ONLY: used by tests/ValidationDriver via link-include
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetDC(IntPtr hWnd);

    // DRIVER-ONLY: used by tests/ValidationDriver via link-include
    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    // DRIVER-ONLY: used by tests/ValidationDriver via link-include
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // PROD: used by the size-constraint probe (WindowShepherdService) to query a
    // captured guest's effective native minimum track size via WM_GETMINMAXINFO.
    // SendMessageTimeout (never SendMessage) so a hung guest can never block the
    // UI thread; SMTO_ABORTIFHUNG bounds the wait. UIPI blocks this message only
    // across an integrity boundary, which TabDock already refuses to capture
    // (elevation guard), so non-elevated guests respond normally.
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    // The desktop is the parent window whose client object reports top-level
    // z-order changes through EVENT_OBJECT_REORDER.
    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 — the -4 sentinel.</summary>
    public static readonly IntPtr DpiAwarenessContextPerMonitorV2 = new IntPtr(-4);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiAwarenessContext);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AreDpiAwarenessContextsEqual(IntPtr dpiContextA, IntPtr dpiContextB);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetDpiForSystem();

    /// <summary>DPI_AWARENESS_CONTEXT_UNAWARE — the -1 sentinel (legacy, DWM-virtualized coordinate space).</summary>
    public static readonly IntPtr DpiAwarenessContextUnaware = new IntPtr(-1);

    /// <summary>USER_DEFAULT_SCREEN_DPI — the base 96-DPI space a DPI-unaware
    /// window inhabits. The physical scale factor applied to an unaware guest's
    /// content and its logical coordinate space is (monitorEffectiveDpi / 96).</summary>
    public const uint USER_DEFAULT_SCREEN_DPI = 96;

    /// <summary>MDT_EFFECTIVE_DPI — GetDpiForMonitor's effective-DPI type. This is
    /// the correct per-monitor scale source for a PerMonitorV2 caller:
    /// GetDpiForWindow(monitor handle) returns 0, and GetDpiForWindow(hwnd) on an
    /// unaware window returns 96 by definition, so neither is usable here.</summary>
    public const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MONITORENUMPROC lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FreeConsole();

    public delegate bool MONITORENUMPROC(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    /// <summary>MONITORINFOF_PRIMARY — the monitor is the primary display.</summary>
    public const uint MONITORINFOF_PRIMARY = 0x1;

    /// <summary>SM_CMONITORS — number of display monitors (includes pseudo-monitors).</summary>
    public const int SM_CMONITORS = 80;

    // DRIVER-ONLY: used by tests/ValidationDriver via link-include
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetCursorPos(int x, int y);

    // DRIVER-ONLY: used by tests/ValidationDriver via link-include
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // -------------------------------------------------------------------------
    // kernel32.dll
    // -------------------------------------------------------------------------
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll")]
    public static extern uint GetLastError();

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentProcessId();

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    /// <summary>
    /// Process-tree ancestry snapshot (used by the ValidationDriver to refuse
    /// killing a tracked process that turns out to be an ancestor of the
    /// driver itself, e.g. a shared-instance host like Windows Terminal's
    /// monarch process, rather than an isolated spawned child).
    /// </summary>
    public const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true, EntryPoint = "Process32FirstW")]
    public static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true, EntryPoint = "Process32NextW")]
    public static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    // -------------------------------------------------------------------------
    // advapi32.dll (system token helpers, not a third-party package)
    // -------------------------------------------------------------------------
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    // -------------------------------------------------------------------------
    // shell32.dll
    // -------------------------------------------------------------------------
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    // -------------------------------------------------------------------------
    // gdi32.dll
    // -------------------------------------------------------------------------
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    // DRIVER-ONLY: used by tests/ValidationDriver via link-include
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    public static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

    // DRIVER-ONLY: used by tests/ValidationDriver via link-include
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    // -------------------------------------------------------------------------
    // dwmapi.dll
    // -------------------------------------------------------------------------
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out bool pvAttribute, uint cbAttribute);

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int GWLP_USERDATA = -21;
    public const int GWLP_HWNDPARENT = -8;
    public const int GWLP_ID = -12;

    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_MINIMIZE = 0x20000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_DISABLED = 0x08000000;
    public const uint WS_CLIPSIBLINGS = 0x04000000;
    public const uint WS_CLIPCHILDREN = 0x02000000;
    public const uint WS_MAXIMIZE = 0x01000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_BORDER = 0x00800000;
    public const uint WS_DLGFRAME = 0x00400000;
    public const uint WS_VSCROLL = 0x00200000;
    public const uint WS_HSCROLL = 0x00100000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_GROUP = 0x00020000;
    public const uint WS_TABSTOP = 0x00010000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;
    public const uint WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;

    public const uint WS_EX_DLGMODALFRAME = 0x00000001;
    public const uint WS_EX_NOPARENTNOTIFY = 0x00000004;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_ACCEPTFILES = 0x00000010;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_MDICHILD = 0x00000040;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_WINDOWEDGE = 0x00000100;
    public const uint WS_EX_CLIENTEDGE = 0x00000200;
    public const uint WS_EX_CONTEXTHELP = 0x00000400;
    public const uint WS_EX_RIGHT = 0x00001000;
    public const uint WS_EX_RTLREADING = 0x00002000;
    public const uint WS_EX_LEFTSCROLLBAR = 0x00004000;
    public const uint WS_EX_CONTROLPARENT = 0x00010000;
    public const uint WS_EX_STATICEDGE = 0x00020000;
    public const uint WS_EX_APPWINDOW = 0x00040000;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOINHERITLAYOUT = 0x00100000;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const uint WS_EX_LAYOUTRTL = 0x00400000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOREDRAW = 0x0008;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_NOCOPYBITS = 0x0100;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_NOSENDCHANGING = 0x0400;

    public static readonly IntPtr HWND_TOP = new IntPtr(0);
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    /// <summary>Parent for message-only windows (CreateWindowEx / HwndSourceParameters).</summary>
    public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    public const uint CW_USEDEFAULT = 0x80000000;

    public const uint GA_PARENT = 1;
    public const uint GA_ROOT = 2;
    public const uint GA_ROOTOWNER = 3;

    public const uint GW_HWNDNEXT = 2;
    public const uint GW_HWNDPREV = 3;
    public const uint GW_OWNER = 4;

    public const uint WM_NULL = 0x0000;
    public const uint WM_CREATE = 0x0001;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_MOVE = 0x0003;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_SHOWWINDOW = 0x0018;
    public const uint WM_ACTIVATEAPP = 0x001C;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_ENABLE = 0x000A;
    public const uint WM_SETREDRAW = 0x000B;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint SC_MAXIMIZE = 0xF030;
    public const uint SC_RESTORE = 0xF120;
    public const uint WA_INACTIVE = 0;
    public const uint WA_ACTIVE = 1;
    public const uint WA_CLICKACTIVE = 2;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_NCACTIVATE = 0x0086;
    public const uint WM_GETMINMAXINFO = 0x0024;
    // SendMessageTimeout flags (WM_GETMINMAXINFO size-constraint probe).
    public const uint SMTO_NORMAL = 0x0000;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const uint WM_NCCALCSIZE = 0x0083;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_HOTKEY = 0x0312;
        public const uint WM_WINDOWPOSCHANGED = 0x0047;
        public const uint WM_ENTERSIZEMOVE = 0x0231;
        public const uint WM_EXITSIZEMOVE = 0x0232;

    public const uint MA_ACTIVATE = 1;
    public const uint MA_ACTIVATEANDEAT = 2;

    // WM_SIZE wParam values.
    public const int SIZE_RESTORED = 0;
    public const int SIZE_MINIMIZED = 1;
    public const int SIZE_MAXIMIZED = 2;
    public const int SIZE_MAXSHOW = 3;
    public const int SIZE_MAXHIDE = 4;

    public const uint RDW_INVALIDATE = 0x0001;
    public const uint RDW_ERASE = 0x0004;
    public const uint RDW_FRAME = 0x0400;
    public const uint RDW_ALLCHILDREN = 0x0080;
    public const uint RDW_UPDATENOW = 0x0100;

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWMINIMIZED = 2;
    public const int SW_SHOWMAXIMIZED = 3;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_SHOW = 5;
    public const int SW_MINIMIZE = 6;
    public const int SW_SHOWMINNOACTIVE = 7;
    public const int SW_SHOWNA = 8;
    public const int SW_RESTORE = 9;
    public const int SW_DEFAULT = 10;
    public const int SW_FORCEMINIMIZE = 11;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_REORDER = 0x8004;
    public const uint EVENT_OBJECT_FOCUS = 0x8005;
    public const uint EVENT_OBJECT_SELECTION = 0x8006;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;

    public const int OBJID_CLIENT = -4;
    public const int CHILDID_SELF = 0;

    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNTHREAD = 0x0001;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const uint WINEVENT_INCONTEXT = 0x0004;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint VK_G = 0x47;
    public const uint VK_D = 0x44;
    public const uint VK_MENU = 0x12;
    public const int ASFW_ANY = -1;
    public const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    public const uint PW_CLIENTONLY = 0x00000001;
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    public const uint DIB_RGB_COLORS = 0;
    public const uint DIB_PAL_COLORS = 1;

    public const uint SRCCOPY = 0x00CC0020;

    public const uint MONITOR_DEFAULTTONULL = 0x00000000;
    public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    public const uint DWMWA_CLOAKED = 14;
    // DRIVER-ONLY: read-only qualification of the no-DWM-mutation contract.
    public const uint DWMWA_TRANSITIONS_FORCEDISABLED = 3;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x00001000;
    public const uint TOKEN_QUERY = 0x0008;

    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    public const int ERROR_INVALID_WINDOW_HANDLE = 1400;

    public static readonly IntPtr IDC_ARROW = new IntPtr(32512);
    public static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);

    // -------------------------------------------------------------------------
    // Structs
    // -------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;

        public int Width => right - left;
        public int Height => bottom - top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public uint length;
        public uint flags;
        public uint showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
        public RECT rcDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_ELEVATION
    {
        public uint TokenIsElevated;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    public enum TOKEN_INFORMATION_CLASS
    {
        TokenElevation = 20,
    }

    // -------------------------------------------------------------------------
    // Managed helpers around the raw imports
    // -------------------------------------------------------------------------

    /// <summary>
    /// This process's ID, resolved once. It cannot change for the lifetime of
    /// the process, and the no-nesting checks query it for every window the
    /// capture picker enumerates (GroupManager.IsOwnWindow).
    /// </summary>
    public static readonly uint CurrentProcessId = GetCurrentProcessId();

    public static string? GetWindowTextString(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len <= 0)
            return string.Empty;

        var sb = new StringBuilder(len + 1);
        int copied = GetWindowText(hWnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : string.Empty;
    }

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true, EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    public static string? GetClassNameString(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : null;
    }

    public static string? GetProcessImagePath(uint pid)
    {
        IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero)
            return null;
        try
        {
            // Long executable paths are valid on Windows. A fixed 1024-char
            // buffer turns those paths into a null identity, which prevents a
            // hidden guest from being journaled/rescued reliably.
            const int InitialCapacity = 1024;
            const int MaximumCapacity = 32768;
            for (int capacity = InitialCapacity; capacity <= MaximumCapacity; capacity *= 2)
            {
                var sb = new StringBuilder(capacity);
                uint size = (uint)sb.Capacity;
                if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    return sb.ToString();

                int error = Marshal.GetLastWin32Error();
                if (error != ERROR_INSUFFICIENT_BUFFER || capacity == MaximumCapacity)
                    return null;
            }
            return null;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Determines whether the process <paramref name="pid"/> runs with an
    /// elevated token. The return value reports whether the CHECK succeeded;
    /// <paramref name="elevated"/> is meaningful only when it returns true.
    /// Callers that treat "check failed" as "not elevated" fail open — the
    /// overload with <paramref name="errorDetail"/> makes the failure reason
    /// visible instead.
    /// </summary>
    public static bool IsProcessElevated(uint pid, out bool elevated)
    {
        return IsProcessElevated(pid, out elevated, out _);
    }

    /// <summary>
    /// Same check as <see cref="IsProcessElevated(uint, out bool)"/>, but on
    /// failure also reports the native error via <paramref name="errorDetail"/>.
    /// The error is captured immediately after each failing call, before any
    /// cleanup (CloseHandle etc.) can clobber the thread's last-error value.
    /// </summary>
    public static bool IsProcessElevated(uint pid, out bool elevated, out string? errorDetail)
    {
        elevated = false;
        errorDetail = null;
        IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero)
        {
            errorDetail = $"OpenProcess: {FormatLastError()}";
            return false;
        }
        try
        {
            if (!OpenProcessToken(hProcess, TOKEN_QUERY, out IntPtr hToken))
            {
                errorDetail = $"OpenProcessToken: {FormatLastError()}";
                return false;
            }
            try
            {
                uint len = 0;
                GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenElevation, IntPtr.Zero, 0, out len);
                if (len == 0)
                {
                    errorDetail = $"GetTokenInformation(size): {FormatLastError()}";
                    return false;
                }

                IntPtr buf = Marshal.AllocHGlobal((int)len);
                try
                {
                    if (!GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenElevation, buf, len, out _))
                    {
                        errorDetail = $"GetTokenInformation: {FormatLastError()}";
                        return false;
                    }

                    var te = Marshal.PtrToStructure<TOKEN_ELEVATION>(buf);
                    elevated = te.TokenIsElevated != 0;
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            finally
            {
                CloseHandle(hToken);
            }
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    public static bool IsCurrentProcessElevated(out bool elevated)
    {
        return IsProcessElevated(GetCurrentProcessId(), out elevated);
    }

    public static string FormatLastError()
    {
        int err = Marshal.GetLastWin32Error();
        if (err == 0)
            return "No error";
        return $"Win32 {err} ({new Win32Exception(err).Message})";
    }

    /// <summary>One-token diagnostic description of a window's rect and state.</summary>
    public static string DescribeWindow(IntPtr hwnd)
    {
        if (!IsWindow(hwnd))
            return $"0x{hwnd.ToInt64():X}(dead)";
        GetWindowRect(hwnd, out RECT r);
        return $"0x{hwnd.ToInt64():X}(rect={r.left},{r.top},{r.Width}x{r.Height} iconic={IsIconic(hwnd)} zoomed={IsZoomed(hwnd)} visible={IsWindowVisible(hwnd)})";
    }
}
