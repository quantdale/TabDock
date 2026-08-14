using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Best-effort, read-only HWND observer. Every query is isolated so a destroyed
/// window or an inaccessible process degrades one row instead of aborting a
/// support report. It never sends messages or changes native state.
/// </summary>
public sealed class NativeSnapshotService
{
    private readonly IReadOnlyList<MonitorSnapshot> _monitors;
    private readonly Dictionary<uint, ProcessDetails> _processCache = new();

    public NativeSnapshotService(IReadOnlyList<MonitorSnapshot> monitors)
    {
        _monitors = monitors;
    }

    public List<TabDockProcessSnapshot> CaptureTabDockProcesses(IReadOnlyList<NativeWindowSnapshot> nativeWindows)
    {
        var rows = new List<TabDockProcessSnapshot>();
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("TabDock");
        }
        catch (Exception)
        {
            return rows;
        }

        foreach (Process process in processes)
        {
            try
            {
                uint pid = unchecked((uint)process.Id);
                ProcessDetails details = GetProcessDetails(pid, process);
                NativeWindowSnapshot? main = nativeWindows.FirstOrDefault(w => w.ProcessId == pid && w.Hwnd == process.MainWindowHandle.ToInt64())
                    ?? nativeWindows.FirstOrDefault(w => w.ProcessId == pid && w.Visible);
                bool elevatedOk = NativeMethods.IsProcessElevated(pid, out bool elevated, out string? elevationError);
                rows.Add(new TabDockProcessSnapshot
                {
                    ProcessId = pid,
                    ExecutableName = details.Name,
                    ExecutablePath = details.Path,
                    StartTimeUtc = details.StartTimeUtc,
                    Architecture = pid == NativeMethods.CurrentProcessId
                        ? RuntimeInformation.ProcessArchitecture.ToString()
                        : "unavailable (not queried)",
                    Elevation = elevatedOk ? (elevated ? "elevated" : "standard-user") : "unavailable (" + (elevationError ?? "probe-failed") + ")",
                    SessionId = process.SessionId,
                    MainHwnd = main?.Hwnd ?? process.MainWindowHandle.ToInt64(),
                    MainHwndVisible = main?.Visible ?? false,
                    MainHwndIconic = main?.Iconic ?? false,
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                rows.Add(new TabDockProcessSnapshot
                {
                    ProcessId = unchecked((uint)process.Id),
                    Status = "degraded (process-exited-during-query)",
                });
            }
            finally
            {
                process.Dispose();
            }
        }
        return rows;
    }

    public List<NativeWindowSnapshot> CaptureTabDockWindows(IReadOnlyList<LogicalPresentationSnapshot>? logical = null)
    {
        var logicalByHwnd = logical?.Where(s => s.ContainerHwnd != 0)
            .ToDictionary(s => s.ContainerHwnd) ?? new Dictionary<long, LogicalPresentationSnapshot>();
        var tabDockPids = GetTabDockProcessIds();
        var rows = new List<NativeWindowSnapshot>();
        try
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                try
                {
                    NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                    if (!tabDockPids.Contains(pid))
                        return true;
                    rows.Add(CaptureWindow(hwnd, logicalByHwnd.TryGetValue(hwnd.ToInt64(), out LogicalPresentationSnapshot? state) ? state : null, "tabdock-window"));
                }
                catch (Exception ex)
                {
                    rows.Add(new NativeWindowSnapshot
                    {
                        Hwnd = hwnd.ToInt64(),
                        Status = "probe-failed (" + Classify(ex) + ")",
                    });
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            rows.Add(new NativeWindowSnapshot
            {
                Status = "enumeration-failed (" + Classify(ex) + ")",
            });
        }
        return rows;
    }

    public NativeWindowSnapshot CaptureWindow(IntPtr hwnd, LogicalPresentationSnapshot? logical = null, string role = "unknown")
    {
        var snapshot = new NativeWindowSnapshot
        {
            Role = role,
            Hwnd = hwnd.ToInt64(),
            Foreground = hwnd != IntPtr.Zero && NativeMethods.GetForegroundWindow() == hwnd,
        };
        if (!NativeMethods.IsWindow(hwnd))
        {
            snapshot.Status = "destroyed-during-snapshot";
            return snapshot;
        }

        snapshot.IsWindow = true;
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            snapshot.ProcessId = pid;
            ProcessDetails details = GetProcessDetails(pid, null);
            snapshot.ProcessName = details.Name;
            snapshot.ProcessPath = details.Path;
            snapshot.ProcessStartTimeUtc = details.StartTimeUtc;
            snapshot.WindowClass = NativeMethods.GetClassNameString(hwnd) ?? "unavailable (class-query)";
            string title = NativeMethods.GetWindowTextString(hwnd) ?? string.Empty;
            snapshot.TitleLength = title.Length;
            snapshot.TitleSha256 = DiagnosticEnvironmentService.HashTitle(title);
            snapshot.Visible = NativeMethods.IsWindowVisible(hwnd);
            snapshot.Iconic = NativeMethods.IsIconic(hwnd);
            snapshot.Zoomed = NativeMethods.IsZoomed(hwnd);
            snapshot.Foreground = NativeMethods.GetForegroundWindow() == hwnd;
            snapshot.Topmost = (NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64()
                & NativeMethods.WS_EX_TOPMOST) != 0;
            snapshot.Cloaked = TryGetCloaked(hwnd);
            if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
                snapshot.Rect = DiagnosticRect.From(rect);
            else
                snapshot.Status = "degraded (GetWindowRect)";
            snapshot.ClientRectScreen = GetClientRectScreen(hwnd);
            snapshot.Monitor = DescribeMonitor(hwnd);
            snapshot.EffectiveDpi = NativeMethods.GetDpiForWindow(hwnd);
            snapshot.DpiAwarenessContext = DescribeDpiAwareness(hwnd);
            snapshot.OwnerHwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER).ToInt64();
            snapshot.PreviousZOrderHwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDPREV).ToInt64();
            snapshot.NextZOrderHwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT).ToInt64();
            if (NativeMethods.IsProcessElevated(pid, out bool elevated, out string? error))
                snapshot.Elevation = elevated ? "elevated" : "standard-user";
            else
                snapshot.Elevation = "unavailable (" + (error ?? "probe-failed") + ")";
            snapshot.PointProbes = BuildPointProbes(hwnd, logical);
        }
        catch (Exception ex)
        {
            snapshot.Status = "probe-failed (" + Classify(ex) + ")";
        }
        return snapshot;
    }

    private HashSet<uint> GetTabDockProcessIds()
    {
        var result = new HashSet<uint>();
        try
        {
            foreach (Process process in Process.GetProcessesByName("TabDock"))
            {
                result.Add(unchecked((uint)process.Id));
                process.Dispose();
            }
        }
        catch { }
        return result;
    }

    private ProcessDetails GetProcessDetails(uint pid, Process? existing)
    {
        if (_processCache.TryGetValue(pid, out ProcessDetails? cached))
            return cached;
        ProcessDetails details = new();
        try
        {
            string? path = NativeMethods.GetProcessImagePath(pid);
            details.Path = DiagnosticEnvironmentService.RedactPath(path);
            details.Name = string.IsNullOrWhiteSpace(path) ? "unavailable" : Path.GetFileName(path);
        }
        catch { }
        try
        {
            if (existing != null)
            {
                details.StartTimeUtc = existing.StartTime.ToUniversalTime().ToString("O");
                if (details.Name == "unavailable")
                    details.Name = existing.ProcessName;
            }
            else
            {
                using Process process = Process.GetProcessById(unchecked((int)pid));
                details.StartTimeUtc = process.StartTime.ToUniversalTime().ToString("O");
                if (details.Name == "unavailable")
                    details.Name = process.ProcessName;
            }
        }
        catch { }
        _processCache[pid] = details;
        return details;
    }

    private List<WindowPointProbe> BuildPointProbes(IntPtr hwnd, LogicalPresentationSnapshot? logical)
    {
        var points = new List<(string Name, NativeMethods.POINT Point)>();
        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT outer))
            return new List<WindowPointProbe> { new() { Name = "container", Status = "unavailable (rect-query)" } };

        points.Add(("header-center", new NativeMethods.POINT
        {
            x = outer.left + Math.Max(0, outer.Width / 2),
            y = outer.top + Math.Min(Math.Max(1, outer.Height / 2), 24),
        }));
        NativeMethods.RECT content = EstimateContentRect(hwnd, outer);
        points.Add(("content-center", Center(content)));
        if (logical?.SplitPresented == true && logical.ExpectedPaneRects.Count >= 2)
        {
            points.Add(("split-left-center", Center(ToNativeRect(logical.ExpectedPaneRects[0]))));
            points.Add(("split-right-center", Center(ToNativeRect(logical.ExpectedPaneRects[1]))));
        }
        else
        {
            var (left, right) = SplitEstimated(content);
            points.Add(("split-left-center", Center(left)));
            points.Add(("split-right-center", Center(right)));
        }

        return points.Select(p => Probe(p.Name, p.Point)).ToList();
    }

    private WindowPointProbe Probe(string name, NativeMethods.POINT point)
    {
        var result = new WindowPointProbe { Name = name, X = point.x, Y = point.y };
        try
        {
            IntPtr returned = NativeMethods.WindowFromPoint(point);
            result.ReturnedHwnd = returned.ToInt64();
            if (returned == IntPtr.Zero)
            {
                result.Status = "no-window";
                return result;
            }
            NativeMethods.GetWindowThreadProcessId(returned, out uint pid);
            result.ReturnedPid = pid;
            result.ReturnedClass = NativeMethods.GetClassNameString(returned) ?? "unavailable";
            result.ReturnedProcess = GetProcessDetails(pid, null).Name;
        }
        catch (Exception ex)
        {
            result.Status = "probe-failed (" + Classify(ex) + ")";
        }
        return result;
    }

    private string DescribeMonitor(IntPtr hwnd)
    {
        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        string handle = DiagnosticEnvironmentService.FormatHwnd(monitor);
        MonitorSnapshot? match = _monitors.FirstOrDefault(m => string.Equals(m.MonitorHandle, handle, StringComparison.OrdinalIgnoreCase));
        return match == null ? handle : $"index={match.Index};handle={handle};dpi={match.EffectiveDpiX}x{match.EffectiveDpiY};primary={match.Primary}";
    }

    private static string DescribeDpiAwareness(IntPtr hwnd)
    {
        try
        {
            IntPtr context = NativeMethods.GetWindowDpiAwarenessContext(hwnd);
            if (context == IntPtr.Zero)
                return "unavailable";
            int awareness = NativeMethods.GetAwarenessFromDpiAwarenessContext(context);
            return $"awareness={awareness};context=0x{context.ToInt64():X}";
        }
        catch
        {
            return "unavailable (probe-failed)";
        }
    }

    private static string TryGetCloaked(IntPtr hwnd)
    {
        try
        {
            int hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out bool cloaked, sizeof(int));
            return hr == 0 ? cloaked.ToString() : $"unavailable (HRESULT 0x{hr:X8})";
        }
        catch (Exception ex)
        {
            return "unavailable (" + Classify(ex) + ")";
        }
    }

    private static DiagnosticRect? GetClientRectScreen(IntPtr hwnd)
    {
        if (!NativeMethods.GetClientRect(hwnd, out NativeMethods.RECT client))
            return null;
        var point = new NativeMethods.POINT { x = 0, y = 0 };
        if (!NativeMethods.ClientToScreen(hwnd, ref point))
            return null;
        return new DiagnosticRect
        {
            Left = point.x,
            Top = point.y,
            Width = client.Width,
            Height = client.Height,
        };
    }

    private static NativeMethods.RECT EstimateContentRect(IntPtr hwnd, NativeMethods.RECT outer)
    {
        DiagnosticRect? client = GetClientRectScreen(hwnd);
        if (client != null)
            return new NativeMethods.RECT
            {
                left = client.Left,
                top = client.Top,
                right = client.Left + client.Width,
                bottom = client.Top + client.Height,
            };
        return outer;
    }

    private static (NativeMethods.RECT Left, NativeMethods.RECT Right) SplitEstimated(NativeMethods.RECT content)
    {
        int split = content.left + Math.Max(0, content.Width / 2);
        return (
            new NativeMethods.RECT { left = content.left, top = content.top, right = split, bottom = content.bottom },
            new NativeMethods.RECT { left = split, top = content.top, right = content.right, bottom = content.bottom });
    }

    private static NativeMethods.POINT Center(NativeMethods.RECT rect)
        => new() { x = rect.left + Math.Max(0, rect.Width / 2), y = rect.top + Math.Max(0, rect.Height / 2) };

    private static NativeMethods.RECT ToNativeRect(DiagnosticRect rect)
        => new() { left = rect.Left, top = rect.Top, right = rect.Left + rect.Width, bottom = rect.Top + rect.Height };

    private static string Classify(Exception ex)
        => ex switch
        {
            UnauthorizedAccessException => "access-denied",
            System.ComponentModel.Win32Exception => "win32-error",
            InvalidOperationException => "destroyed-during-snapshot",
            _ => "probe-failed",
        };

    private sealed class ProcessDetails
    {
        public string Name { get; set; } = "unavailable";
        public string Path { get; set; } = "unavailable";
        public string StartTimeUtc { get; set; } = "unavailable";
    }
}
