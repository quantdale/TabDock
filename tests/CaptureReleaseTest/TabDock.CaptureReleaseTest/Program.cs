using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TabDock;

namespace TabDock.CaptureReleaseTest;

/// <summary>
/// End-to-end capture→release test for real third-party apps:
/// Paint (baseline), Windows Terminal, Chrome, and Cursor.
/// Verifies both restoration state and live rendering while embedded.
/// </summary>
internal static class Program
{
    private const int MaxSpawns = 5;
    private const int OverallTimeoutMs = 180000;
    private const string SingleInstanceMutexName = "Global\\TabDockCaptureReleaseTest";

    private static readonly object SpawnLock = new object();
    private static int _spawnCount = 0;
    private static readonly List<Process> SpawnedProcesses = new List<Process>();
    private static readonly CancellationTokenSource Cts = new CancellationTokenSource(OverallTimeoutMs);

    [STAThread]
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine($"[PID {Environment.ProcessId}] TabDock app rendering/capture→release test started.");

        using var mutex = new Mutex(true, SingleInstanceMutexName, out bool isNew);
        if (!isNew)
        {
            Console.WriteLine("Another instance is already running. Aborting.");
            return 2;
        }

        Console.CancelKeyPress += (_, e) =>
        {
            Console.WriteLine("Ctrl+C pressed. Aborting and cleaning up...");
            e.Cancel = true;
            Cts.Cancel();
            CleanupTrackedProcesses();
        };

        Console.WriteLine();
        Console.WriteLine("This test will run one scenario per app:");
        Console.WriteLine("  1. Paint (baseline)");
        Console.WriteLine("  2. Windows Terminal with a ticking clock");
        Console.WriteLine("  3. Chrome on a page with live content");
        Console.WriteLine("  4. Cursor (if installed)");
        Console.WriteLine("Each scenario captures the window into a host, verifies live rendering, releases it,");
        Console.WriteLine("and checks that the HWND state is restored.");
        Console.Write("Type 'y' and press Enter to proceed: ");
        var confirmation = Console.ReadLine();
        if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Aborted by user.");
            return 3;
        }

        var scenarios = new[]
        {
            // Paint is a static baseline: we just verify it is not black/frozen.
            new AppScenario("Paint", "mspaint.exe", "", "MSPaintApp", null, RequireLiveVariance: false, UseShellExecute: true),
            new AppScenario("Windows Terminal", "wt.exe", "powershell.exe -NoExit -Command \"while ($true) { Get-Date; Start-Sleep -Seconds 1 }\"", "CASCADIA_HOSTING_WINDOW_CLASS", null, RequireLiveVariance: true, UseShellExecute: false),
            new AppScenario("Chrome", "C:/Program Files/Google/Chrome/Application/chrome.exe", $"--user-data-dir=\"{Path.Combine(Path.GetTempPath(), "TabDockChromeProfile")}\" --disable-gpu --app=https://time.is", "Chrome_WidgetWin_1", null, RequireLiveVariance: true, UseShellExecute: true),
            new AppScenario("Cursor", "C:/Users/palac/AppData/Local/Programs/cursor/Cursor.exe", $"--user-data-dir=\"{Path.Combine(Path.GetTempPath(), "TabDockCursorProfile")}\" --disable-gpu", "Chrome_WidgetWin_1", (t) => t.Contains("Cursor"), RequireLiveVariance: true, UseShellExecute: true),
        };

        bool allPassed = true;
        foreach (var scenario in scenarios)
        {
            bool passed = await RunScenarioAsync(scenario, Cts.Token);
            allPassed &= passed;
            Console.WriteLine();
        }

        CleanupTrackedProcesses();

        Console.WriteLine(allPassed
            ? "ALL AVAILABLE SCENARIOS PASSED."
            : "ONE OR MORE SCENARIOS FAILED OR WERE SKIPPED.");
        return allPassed ? 0 : 5;
    }

    private static async Task<bool> RunScenarioAsync(AppScenario scenario, CancellationToken cancellationToken)
    {
        Console.WriteLine($"=== Scenario: {scenario.Name} ===");

        if (!CanLaunch(scenario.Executable))
        {
            Console.WriteLine($"SKIPPED: {scenario.Executable} not found on PATH.");
            return true; // Not a failure; app simply not installed.
        }

        Log("Enumerating pre-existing windows of target class...");
        var existingWindows = FindWindowsByClass(scenario.ClassName);
        Log($"Found {existingWindows.Count} pre-existing {scenario.ClassName} window(s).");

        Log($"Spawning {scenario.Name}...");
        Process? app = null;
        try
        {
            app = SpawnGuarded(() => Process.Start(new ProcessStartInfo(scenario.Executable, scenario.Arguments)
            {
                UseShellExecute = scenario.UseShellExecute,
                CreateNoWindow = false,
            })!);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED to start {scenario.Name}: {ex.Message}");
            return false;
        }

        Log("Waiting for a new top-level window...");
        IntPtr appHwnd = IntPtr.Zero;
        for (int i = 0; i < 200 && appHwnd == IntPtr.Zero; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            appHwnd = FindNewWindow(scenario.ClassName, existingWindows, scenario.TitleMatcher);
            if (appHwnd == IntPtr.Zero)
            {
                if (i % 10 == 0)
                    Log($"  attempt {i + 1}/200...");
                await Task.Delay(100, cancellationToken);
            }
        }

        if (appHwnd == IntPtr.Zero)
        {
            Console.WriteLine($"FAILED: Could not locate a new {scenario.Name} window.");
            return false;
        }

        uint pid = 0;
        NativeMethods.GetWindowThreadProcessId(appHwnd, out pid);
        Log($"{scenario.Name} HWND: 0x{appHwnd.ToInt64():X}, PID: {pid}");

        // Give the app a moment to render its initial content.
        await Task.Delay(2000, cancellationToken);

        if (NativeMethods.IsIconic(appHwnd))
            NativeMethods.ShowWindow(appHwnd, NativeMethods.SW_RESTORE);

        await Task.Delay(500, cancellationToken);

        WindowSnapshot before = Snapshot(appHwnd);
        Log("BEFORE capture:");
        Console.WriteLine(before.ToDetailedString());

        // Create host window.
        string hostClassName = "TabDockCaptureTestHost_" + Guid.NewGuid().ToString("N");
        var wndProc = new NativeMethods.WndProc(WindowProc);
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX)),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
            hInstance = Marshal.GetHINSTANCE(typeof(Program).Module),
            lpszClassName = hostClassName,
        };

        ushort atom = NativeMethods.RegisterClassEx(ref wc);
        if (atom == 0)
        {
            Console.WriteLine($"FAILED: RegisterClassEx failed: {NativeMethods.FormatLastError()}");
            return false;
        }

        IntPtr hostHwnd = NativeMethods.CreateWindowEx(
            0,
            hostClassName,
            $"TabDock Test Host ({scenario.Name})",
            NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_VISIBLE,
            unchecked((int)NativeMethods.CW_USEDEFAULT),
            unchecked((int)NativeMethods.CW_USEDEFAULT),
            900,
            700,
            IntPtr.Zero,
            IntPtr.Zero,
            wc.hInstance,
            IntPtr.Zero);

        if (hostHwnd == IntPtr.Zero)
        {
            Console.WriteLine($"FAILED: CreateWindowEx failed: {NativeMethods.FormatLastError()}");
            return false;
        }

        bool scenarioPassed;
        try
        {
            Log("Capturing into host...");
            Capture(appHwnd, hostHwnd);

            if (!NativeMethods.IsWindow(appHwnd))
            {
                Console.WriteLine("FAILED: target window disappeared after capture.");
                return false;
            }

            Log("Verifying live rendering while embedded (3-second observation)...");
            bool rendering = await VerifyLiveRenderingAsync(appHwnd, hostHwnd, scenario.RequireLiveVariance, cancellationToken);
            if (!rendering)
            {
                Console.WriteLine($"FAILED: {scenario.Name} is not rendering live content while embedded (black/frozen).");
                scenarioPassed = false;
            }
            else
            {
                Log($"{scenario.Name} is rendering live content while embedded.");

                Log("Releasing back to standalone...");
                Release(appHwnd, before);

                await Task.Delay(800, cancellationToken);

                WindowSnapshot after = Snapshot(appHwnd);
                Log("AFTER release:");
                Console.WriteLine(after.ToDetailedString());

                scenarioPassed = Compare(before, after, out string diff);
                if (scenarioPassed)
                    Console.WriteLine($"PASS: {scenario.Name} captured, rendered, and restored correctly.");
                else
                    Console.WriteLine($"FAIL: {scenario.Name} state mismatch after release. {diff}");
            }
        }
        finally
        {
            NativeMethods.DestroyWindow(hostHwnd);
            Log($"Killing {scenario.Name}...");
            try { if (app != null && !app.HasExited) app.Kill(entireProcessTree: true); } catch { }
        }

        return scenarioPassed;
    }

    // -------------------------------------------------------------------------
    // Rendering verification
    // -------------------------------------------------------------------------
    private static async Task<bool> VerifyLiveRenderingAsync(IntPtr hwnd, IntPtr hostHwnd, bool requireLiveVariance, CancellationToken cancellationToken)
    {
        // Wait briefly for any animation to start, then capture two frames ~1.5s apart.
        await Task.Delay(500, cancellationToken);
        int[]? frame0 = CaptureHostScreenArea(hostHwnd);
        if (frame0 == null)
            return false;

        await Task.Delay(1500, cancellationToken);
        int[]? frame1 = CaptureHostScreenArea(hostHwnd);
        if (frame1 == null)
            return false;

        if (frame0.Length != frame1.Length)
            return false;

        long diff = 0;
        long totalBrightness = 0;
        int len = frame0.Length;
        for (int i = 0; i < len; i++)
        {
            int a = frame0[i];
            int b = frame1[i];
            int ra = a & 0xFF, ga = (a >> 8) & 0xFF, ba = (a >> 16) & 0xFF;
            int rb = b & 0xFF, gb = (b >> 8) & 0xFF, bb = (b >> 16) & 0xFF;
            diff += Math.Abs(ra - rb) + Math.Abs(ga - gb) + Math.Abs(ba - bb);
            totalBrightness += ra + ga + ba;
        }

        double avgBrightness = totalBrightness / (double)(len * 3);
        double avgDiff = diff / (double)(len * 3);
        Log($"Rendering frame diff: total={diff}, avg-per-pixel-channel={avgDiff:F2}, avg-brightness={avgBrightness:F2}");

        if (avgBrightness < 1.0)
            return false; // Black or blank host area.

        if (requireLiveVariance)
            return avgDiff > 0.005; // Visible change between frames (a blinking cursor is enough).

        return true; // Static but visible content is enough.
    }

    /// <summary>
    /// Captures the host window's client area from the screen via BitBlt.
    /// This captures the DWM-composited result, which is more reliable for
    /// GPU-rendered children than PrintWindow.
    /// </summary>
    private static int[]? CaptureHostScreenArea(IntPtr hostHwnd)
    {
        if (!NativeMethods.IsWindow(hostHwnd))
            return null;

        NativeMethods.GetClientRect(hostHwnd, out NativeMethods.RECT rc);
        int width = rc.Width;
        int height = rc.Height;
        if (width <= 0 || height <= 0)
            return null;

        var pt = new NativeMethods.POINT { x = rc.left, y = rc.top };
        if (!NativeMethods.ClientToScreen(hostHwnd, ref pt))
            return null;

        IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
            return null;

        var bmi = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
                biSizeImage = (uint)(width * height * 4),
            }
        };

        IntPtr bits = IntPtr.Zero;
        IntPtr hbm = NativeMethods.CreateDIBSection(hdcScreen, ref bmi, NativeMethods.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
        if (hbm == IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
            return null;
        }

        IntPtr hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
        if (hdcMem == IntPtr.Zero)
        {
            NativeMethods.DeleteObject(hbm);
            NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
            return null;
        }

        NativeMethods.SelectObject(hdcMem, hbm);
        bool copied = NativeMethods.BitBlt(hdcMem, 0, 0, width, height, hdcScreen, pt.x, pt.y, NativeMethods.SRCCOPY);

        int[] pixels = new int[width * height];
        if (copied && bits != IntPtr.Zero)
        {
            Marshal.Copy(bits, pixels, 0, pixels.Length);
        }

        NativeMethods.DeleteDC(hdcMem);
        NativeMethods.DeleteObject(hbm);
        NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);

        if (!copied)
            return null;
        return pixels;
    }

    // -------------------------------------------------------------------------
    // Capture / release
    // -------------------------------------------------------------------------
    private static void Capture(IntPtr childHwnd, IntPtr hostHwnd)
    {
        IntPtr previousParent = NativeMethods.SetParent(childHwnd, hostHwnd);
        if (previousParent == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SetParent failed: {err} ({new System.ComponentModel.Win32Exception(err).Message})");
        }

        nint style = NativeMethods.GetWindowLongPtr(childHwnd, NativeMethods.GWL_STYLE);
        nint newStyle = (nint)(((long)style & ~(long)(
            NativeMethods.WS_POPUP |
            NativeMethods.WS_CAPTION |
            NativeMethods.WS_THICKFRAME |
            NativeMethods.WS_MINIMIZEBOX |
            NativeMethods.WS_MAXIMIZEBOX |
            NativeMethods.WS_SYSMENU)) |
            (long)(NativeMethods.WS_CHILD | NativeMethods.WS_CLIPSIBLINGS));
        NativeMethods.SetWindowLongPtr(childHwnd, NativeMethods.GWL_STYLE, newStyle);

        nint exStyle = NativeMethods.GetWindowLongPtr(childHwnd, NativeMethods.GWL_EXSTYLE);
        nint newExStyle = (nint)((long)exStyle & ~(long)(
            NativeMethods.WS_EX_WINDOWEDGE |
            NativeMethods.WS_EX_CLIENTEDGE |
            NativeMethods.WS_EX_DLGMODALFRAME));
        NativeMethods.SetWindowLongPtr(childHwnd, NativeMethods.GWL_EXSTYLE, newExStyle);

        NativeMethods.RECT rc;
        NativeMethods.GetClientRect(hostHwnd, out rc);
        NativeMethods.SetWindowPos(
            childHwnd,
            NativeMethods.HWND_TOP,
            0,
            0,
            rc.Width,
            rc.Height,
            NativeMethods.SWP_FRAMECHANGED |
            NativeMethods.SWP_SHOWWINDOW |
            NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOACTIVATE);
    }

    private static void Release(IntPtr childHwnd, WindowSnapshot original)
    {
        NativeMethods.ShowWindow(childHwnd, NativeMethods.SW_HIDE);

        NativeMethods.SetParent(childHwnd, original.Parent);

        NativeMethods.SetWindowLongPtr(childHwnd, NativeMethods.GWL_STYLE, (nint)original.Style);
        NativeMethods.SetWindowLongPtr(childHwnd, NativeMethods.GWL_EXSTYLE, (nint)original.ExStyle);

        NativeMethods.SetWindowPos(
            childHwnd,
            NativeMethods.HWND_TOP,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_FRAMECHANGED |
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOACTIVATE);

        NativeMethods.WINDOWPLACEMENT placement = original.Placement;
        placement.length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();

        if (original.ShowCmd == NativeMethods.SW_SHOWMAXIMIZED)
        {
            var normalPlacement = placement;
            normalPlacement.showCmd = NativeMethods.SW_SHOWNORMAL;
            normalPlacement.flags = 0;
            NativeMethods.SetWindowPlacement(childHwnd, ref normalPlacement);
            NativeMethods.ShowWindow(childHwnd, NativeMethods.SW_SHOWMAXIMIZED);
        }
        else
        {
            if (!NativeMethods.SetWindowPlacement(childHwnd, ref placement))
            {
                Log($"SetWindowPlacement failed: {NativeMethods.FormatLastError()}; falling back to SetWindowPos.");
                NativeMethods.SetWindowPos(
                    childHwnd,
                    NativeMethods.HWND_TOP,
                    original.Bounds.left,
                    original.Bounds.top,
                    original.Bounds.Width,
                    original.Bounds.Height,
                    NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
            }

            NativeMethods.ShowWindow(childHwnd, original.ShowCmd);
        }
    }

    // -------------------------------------------------------------------------
    // Snapshot / comparison
    // -------------------------------------------------------------------------
    private static WindowSnapshot Snapshot(IntPtr hwnd)
    {
        var placement = new NativeMethods.WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(hwnd, out placement))
            Log($"GetWindowPlacement warning: {NativeMethods.FormatLastError()}");

        NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT bounds);
        IntPtr parent = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_PARENT);
        nint style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);

        return new WindowSnapshot
        {
            Hwnd = hwnd,
            Title = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty,
            Parent = parent,
            Style = (long)style,
            ExStyle = (long)exStyle,
            Bounds = bounds,
            Placement = placement,
            ShowCmd = (int)placement.showCmd,
        };
    }

    private static bool Compare(WindowSnapshot before, WindowSnapshot after, out string diff)
    {
        var differences = new List<string>();

        if (before.Parent != after.Parent)
            differences.Add($"Parent changed: 0x{before.Parent.ToInt64():X} -> 0x{after.Parent.ToInt64():X}");

        if (before.Style != after.Style)
            differences.Add($"Style changed: 0x{before.Style:X} -> 0x{after.Style:X}");

        if (before.ExStyle != after.ExStyle)
            differences.Add($"ExStyle changed: 0x{before.ExStyle:X} -> 0x{after.ExStyle:X}");

        if (before.ShowCmd == NativeMethods.SW_SHOWMAXIMIZED && after.ShowCmd == NativeMethods.SW_SHOWMAXIMIZED)
        {
            if (!NativeMethods.IsZoomed(after.Hwnd))
                differences.Add($"After-release window is not zoomed/maximized. Bounds={FormatRect(after.Bounds)}");
        }
        else if (!RectEquals(before.Bounds, after.Bounds))
        {
            differences.Add($"Bounds changed: {FormatRect(before.Bounds)} -> {FormatRect(after.Bounds)}");
        }

        if (before.Placement.showCmd != after.Placement.showCmd)
            differences.Add($"ShowCmd changed: {before.Placement.showCmd} -> {after.Placement.showCmd}");

        if (before.Placement.flags != after.Placement.flags)
            differences.Add($"Placement flags changed: {before.Placement.flags} -> {after.Placement.flags}");

        if (!RectEquals(before.Placement.rcNormalPosition, after.Placement.rcNormalPosition))
            differences.Add($"rcNormalPosition changed: {FormatRect(before.Placement.rcNormalPosition)} -> {FormatRect(after.Placement.rcNormalPosition)}");

        if (before.Title != after.Title)
            differences.Add($"Title changed: '{before.Title}' -> '{after.Title}'");

        diff = string.Join("; ", differences);
        return differences.Count == 0;
    }

    private static bool RectEquals(NativeMethods.RECT a, NativeMethods.RECT b)
    {
        return a.left == b.left && a.top == b.top && a.right == b.right && a.bottom == b.bottom;
    }

    private static string FormatRect(NativeMethods.RECT r)
    {
        return $"({r.left},{r.top})-({r.right},{r.bottom})";
    }

    // -------------------------------------------------------------------------
    // Window finding
    // -------------------------------------------------------------------------
    private static HashSet<IntPtr> FindWindowsByClass(string className)
    {
        var set = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;

            var cn = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, cn, cn.Capacity);
            if (cn.ToString().Equals(className, StringComparison.OrdinalIgnoreCase))
                set.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return set;
    }

    private static IntPtr FindNewWindow(string className, HashSet<IntPtr> existingWindows, Func<string, bool>? titleMatcher)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;
            if (existingWindows.Contains(hwnd))
                return true;

            var cn = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, cn, cn.Capacity);
            if (!cn.ToString().Equals(className, StringComparison.OrdinalIgnoreCase))
                return true;

            var title = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0)
                return true;

            if (titleMatcher != null && !titleMatcher(title.ToString()))
                return true;

            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool CanLaunch(string executable)
    {
        if (File.Exists(executable))
            return true;

        try
        {
            var psi = new ProcessStartInfo("where.exe", executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(5000);
            return p.ExitCode == 0 && !string.IsNullOrWhiteSpace(p.StandardOutput.ReadToEnd());
        }
        catch
        {
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Guarded spawn
    // -------------------------------------------------------------------------
    private static Process SpawnGuarded(Func<Process> spawn)
    {
        lock (SpawnLock)
        {
            if (_spawnCount >= MaxSpawns)
                throw new InvalidOperationException($"Spawn cap of {MaxSpawns} exceeded — aborting.");

            Log($"Spawning process {_spawnCount + 1}/{MaxSpawns}...");
            Process p = spawn();
            if (p == null)
                throw new InvalidOperationException("SpawnGuarded received a null Process.");

            _spawnCount++;
            SpawnedProcesses.Add(p);
            Log($"Spawned PID {p.Id}: {p.ProcessName}");
            return p;
        }
    }

    private static void CleanupTrackedProcesses()
    {
        List<Process> copy;
        lock (SpawnLock)
        {
            copy = new List<Process>(SpawnedProcesses);
        }

        foreach (var p in copy)
        {
            try
            {
                if (!p.HasExited)
                {
                    Log($"Killing tracked process {p.Id} ({p.ProcessName})...");
                    p.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to kill process {p.Id}: {ex.Message}");
            }
        }
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_DESTROY)
        {
            NativeMethods.PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private record AppScenario(string Name, string Executable, string Arguments, string ClassName, Func<string, bool>? TitleMatcher, bool RequireLiveVariance, bool UseShellExecute);

    private struct WindowSnapshot
    {
        public IntPtr Hwnd;
        public string Title;
        public IntPtr Parent;
        public long Style;
        public long ExStyle;
        public NativeMethods.RECT Bounds;
        public NativeMethods.WINDOWPLACEMENT Placement;
        public int ShowCmd;

        public string ToDetailedString()
        {
            return $"HWND=0x{Hwnd.ToInt64():X}, Title='{Title}', Parent=0x{Parent.ToInt64():X}, " +
                   $"Style=0x{Style:X}, ExStyle=0x{ExStyle:X}, " +
                   $"Bounds={FormatRect(Bounds)}, ShowCmd={ShowCmd}, " +
                   $"rcNormalPosition={FormatRect(Placement.rcNormalPosition)}, Flags={Placement.flags}";
        }
    }
}
