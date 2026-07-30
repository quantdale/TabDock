using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TabDock.Spike;

/// <summary>
/// Survival spike: reparent Notepad into a throwaway host window, kill the host,
/// and check whether Notepad's HWND is still alive afterwards.
/// </summary>
internal static class Program
{
    // -------------------------------------------------------------------------
    // Guardrails
    // -------------------------------------------------------------------------
    private const int MaxTotalSpawns = 4; // 1 cmd + 1 host + 1 checker + 1 taskkill
    private const int OverallTimeoutMs = 15000;
    private const string SingleInstanceMutexName = "Global\\TabDockSurvivalSpike";

    private static readonly object SpawnLock = new object();
    private static int _spawnCount = 0;
    private static readonly List<Process> SpawnedProcesses = new List<Process>();
    private static readonly CancellationTokenSource Cts = new CancellationTokenSource(OverallTimeoutMs);

    [STAThread]
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine($"[PID {Environment.ProcessId}] TabDock Survival Spike started. Args: {string.Join(' ', args)}");

        if (args.Length > 0 && args[0] == "--host")
            return RunHost(args);
        if (args.Length > 0 && args[0] == "--checker")
            return RunChecker(args);

        using var mutex = new Mutex(true, SingleInstanceMutexName, out bool isNew);
        if (!isNew)
        {
            Console.WriteLine("Another instance of the survival spike is already running. Aborting.");
            return 2;
        }

        Console.CancelKeyPress += (_, e) =>
        {
            Console.WriteLine("Ctrl+C pressed. Aborting and cleaning up...");
            e.Cancel = true;
            Cts.Cancel();
            KillAllTracked();
        };

        Console.WriteLine();
        Console.WriteLine("This spike will:");
        Console.WriteLine("  1. Spawn one Command Prompt (cmd.exe) process.");
        Console.WriteLine("  2. Create a native host window and reparent the Command Prompt into it.");
        Console.WriteLine("  3. Spawn a checker that waits for the host to die.");
        Console.WriteLine("  4. Kill the host process with taskkill /F.");
        Console.WriteLine("  5. Report whether the Command Prompt HWND survived.");
        Console.Write("Type 'y' and press Enter to proceed: ");
        var confirmation = Console.ReadLine();
        if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Aborted by user.");
            return 3;
        }

        try
        {
            return await RunOrchestratorAsync(Cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Operation cancelled or timed out after {OverallTimeoutMs} ms.");
            return 4;
        }
        finally
        {
            Console.WriteLine("Cleaning up all tracked processes...");
            KillAllTracked();
        }
    }

    // -------------------------------------------------------------------------
    // Orchestrator
    // -------------------------------------------------------------------------
    private static async Task<int> RunOrchestratorAsync(CancellationToken cancellationToken)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "TabDockSpike");
        Directory.CreateDirectory(tempDir);
        string hostPidFile = Path.Combine(tempDir, "host.pid");
        string resultFile = Path.Combine(tempDir, "result.txt");
        File.Delete(hostPidFile);
        File.Delete(resultFile);

        Log("Spawning Command Prompt...");
        Process cmd = SpawnGuarded(() => Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true })!);
        if (cmd == null)
            throw new InvalidOperationException("Failed to start Command Prompt.");

        Log("Waiting for Command Prompt's main window...");
        IntPtr cmdHwnd = IntPtr.Zero;
        int cmdPid = 0;
        for (int i = 0; i < 100 && cmdHwnd == IntPtr.Zero; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cmdHwnd = FindCmdWindow(cmd.Id, out cmdPid);
            if (cmdHwnd == IntPtr.Zero)
            {
                Log($"  attempt {i + 1}/100...");
                await Task.Delay(100, cancellationToken);
            }
        }

        if (cmdHwnd == IntPtr.Zero)
            throw new InvalidOperationException("Could not locate Command Prompt's main window.");

        Log($"Command Prompt HWND: 0x{cmdHwnd.ToInt64():X}, PID: {cmdPid}");

        string exePath = GetOwnExePath();
        Log($"Using spike executable: {exePath}");
        if (!File.Exists(exePath))
            throw new FileNotFoundException("Spike executable not found. Build the project first.", exePath);

        Log("Spawning host process...");
        Process host = SpawnGuarded(() => StartChildProcess(exePath, $"--host {cmdHwnd.ToInt64()} {cmdPid} \"{hostPidFile}\""));

        Log("Waiting for host PID file...");
        int hostPid = 0;
        for (int i = 0; i < 100 && hostPid == 0; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(hostPidFile) && int.TryParse(File.ReadAllText(hostPidFile).Trim(), out hostPid))
                break;
            await Task.Delay(50, cancellationToken);
        }

        if (hostPid == 0)
        {
            Log("Host process did not write its PID. Reading any host output:");
            await DrainOutputAsync(host);
            throw new InvalidOperationException("Host process did not write its PID.");
        }
        Log($"Host PID: {hostPid}");

        Log("Spawning checker process...");
        Process checker = SpawnGuarded(() => StartChildProcess(exePath, $"--checker {cmdHwnd.ToInt64()} {hostPid} \"{resultFile}\""));

        Log("Giving checker time to attach wait handle...");
        await Task.Delay(1500, cancellationToken);

        Log($"Killing host process {hostPid}...");
        Process kill = SpawnGuarded(() => StartChildProcess("taskkill", $"/F /PID {hostPid}"));
        kill.WaitForExit(5000);

        Log("Waiting for checker result...");
        await checker.WaitForExitAsync(cancellationToken);
        await DrainOutputAsync(checker);

        if (File.Exists(resultFile))
        {
            Log("RESULT:");
            Console.WriteLine(File.ReadAllText(resultFile));
        }
        else
        {
            Log("No result file produced.");
        }

        // Interpret the result for the caller.
        if (File.Exists(resultFile))
        {
            string text = File.ReadAllText(resultFile);
            if (text.Contains("IsWindow=True"))
            {
                Console.WriteLine();
                Console.WriteLine("SURVIVAL SPIKE OUTCOME: Command Prompt HWND survived the host kill.");
                return 0;
            }
            else if (text.Contains("IsWindow=False"))
            {
                Console.WriteLine();
                Console.WriteLine("SURVIVAL SPIKE OUTCOME: Command Prompt HWND died with the host.");
                return 0;
            }
        }

        Console.WriteLine();
        Console.WriteLine("SURVIVAL SPIKE OUTCOME: inconclusive.");
        return 5;
    }

    // -------------------------------------------------------------------------
    // Host mode
    // -------------------------------------------------------------------------
    private static int RunHost(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("Usage: --host <childHwnd> <childPid> <pidFile>");
            return 1;
        }

        // The child HWND/PID come in over the command line and are SetParent'd and
        // restyled below — validate before touching anything: a malformed value
        // must fail cleanly, and a window that is not the orchestrator's own
        // spawned cmd.exe must never be reparented.
        if (!long.TryParse(args[1], out long hwndValue) || hwndValue == 0)
        {
            Console.WriteLine($"Malformed --host HWND argument: '{args[1]}'");
            return 1;
        }
        if (!int.TryParse(args[2], out int expectedChildPid) || expectedChildPid <= 0)
        {
            Console.WriteLine($"Malformed --host PID argument: '{args[2]}'");
            return 1;
        }

        IntPtr childHwnd = new IntPtr(hwndValue);
        string pidFile = args[3];

        if (!IsWindow(childHwnd))
        {
            Console.WriteLine($"Refusing --host: 0x{hwndValue:X} is not a window.");
            return 1;
        }

        var childClass = new StringBuilder(256);
        GetClassName(childHwnd, childClass, childClass.Capacity);
        GetWindowThreadProcessId(childHwnd, out uint childPid);
        // Console windows are owned by conhost.exe; the spawned cmd is its
        // parent (see FindCmdWindow), so accept either the cmd PID itself or a
        // conhost whose parent is the expected cmd.
        bool ownedByExpected = (int)childPid == expectedChildPid
            || GetParentProcessId((int)childPid) == expectedChildPid;
        if (!ownedByExpected
            || !childClass.ToString().Equals("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Refusing --host: 0x{hwndValue:X} is class '{childClass}' owned by PID {childPid}, expected ConsoleWindowClass owned by PID {expectedChildPid}.");
            return 1;
        }

        try
        {
            string className = "TabDockSpikeHost_" + Guid.NewGuid().ToString("N");
            var wndProc = new WndProcDelegate(WindowProc);
            WNDCLASSEX wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
                hInstance = Marshal.GetHINSTANCE(typeof(Program).Module),
                lpszClassName = className,
            };

            ushort atom = RegisterClassEx(ref wc);
            if (atom == 0)
            {
                Console.WriteLine($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
                return 1;
            }

            IntPtr hostHwnd = CreateWindowEx(
                0,
                className,
                "TabDock Survival Spike Host",
                NativeConstants.WS_OVERLAPPEDWINDOW | NativeConstants.WS_VISIBLE,
                unchecked((int)NativeConstants.CW_USEDEFAULT),
                unchecked((int)NativeConstants.CW_USEDEFAULT),
                900,
                700,
                IntPtr.Zero,
                IntPtr.Zero,
                wc.hInstance,
                IntPtr.Zero);

            if (hostHwnd == IntPtr.Zero)
            {
                Console.WriteLine($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
                return 1;
            }

            // Reparent Notepad into the host and strip its caption/border styles.
            SetParent(childHwnd, hostHwnd);

            nint style = GetWindowLongPtr(childHwnd, NativeConstants.GWL_STYLE);
            nint newStyle = (nint)(((long)style & ~(long)(NativeConstants.WS_POPUP
                | NativeConstants.WS_CAPTION
                | NativeConstants.WS_THICKFRAME
                | NativeConstants.WS_MINIMIZEBOX
                | NativeConstants.WS_MAXIMIZEBOX
                | NativeConstants.WS_SYSMENU))
                | (long)(NativeConstants.WS_CHILD | NativeConstants.WS_CLIPSIBLINGS));
            SetWindowLongPtr(childHwnd, NativeConstants.GWL_STYLE, newStyle);

            nint exStyle = GetWindowLongPtr(childHwnd, NativeConstants.GWL_EXSTYLE);
            nint newExStyle = (nint)((long)exStyle & ~(long)(NativeConstants.WS_EX_WINDOWEDGE
                | NativeConstants.WS_EX_CLIENTEDGE
                | NativeConstants.WS_EX_DLGMODALFRAME));
            SetWindowLongPtr(childHwnd, NativeConstants.GWL_EXSTYLE, newExStyle);

            RECT rc;
            GetClientRect(hostHwnd, out rc);
            SetWindowPos(
                childHwnd,
                IntPtr.Zero,
                0,
                0,
                rc.right - rc.left,
                rc.bottom - rc.top,
                NativeConstants.SWP_FRAMECHANGED
                | NativeConstants.SWP_SHOWWINDOW
                | NativeConstants.SWP_NOZORDER
                | NativeConstants.SWP_NOACTIVATE);

            ShowWindow(hostHwnd, NativeConstants.SW_SHOW);
            UpdateWindow(hostHwnd);

            File.WriteAllText(pidFile, Process.GetCurrentProcess().Id.ToString());
            Console.WriteLine($"Host ready. PID {Process.GetCurrentProcess().Id}, HWND 0x{hostHwnd.ToInt64():X}");

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Host mode exception: {ex}");
            return 1;
        }
    }

    // -------------------------------------------------------------------------
    // Checker mode
    // -------------------------------------------------------------------------
    private static int RunChecker(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("Usage: --checker <childHwnd> <hostPid> <resultFile>");
            return 1;
        }

        IntPtr childHwnd;
        int hostPid;
        if (!long.TryParse(args[1], out long hwndValue) || hwndValue == 0)
        {
            Console.WriteLine($"Malformed --checker HWND argument: '{args[1]}'");
            return 1;
        }
        if (!int.TryParse(args[2], out hostPid) || hostPid <= 0)
        {
            Console.WriteLine($"Malformed --checker PID argument: '{args[2]}'");
            return 1;
        }
        childHwnd = new IntPtr(hwndValue);
        string resultFile = args[3];

        Console.WriteLine($"Checker started. Watching host PID {hostPid}, child HWND 0x{childHwnd.ToInt64():X}");

        string result;
        IntPtr hProcess = OpenProcess(NativeConstants.PROCESS_SYNCHRONIZE, false, (uint)hostPid);
        if (hProcess == IntPtr.Zero)
        {
            result = $"OpenProcess failed: {Marshal.GetLastWin32Error()}";
        }
        else
        {
            uint waitResult = WaitForSingleObject(hProcess, 20000);
            CloseHandle(hProcess);

            // Give the OS a moment to finish any cascade destruction.
            Thread.Sleep(1000);

            bool alive = IsWindow(childHwnd);
            bool visible = IsWindowVisible(childHwnd);
            uint childPid = 0;
            GetWindowThreadProcessId(childHwnd, out childPid);

            result = $"WaitForSingleObject={waitResult}, IsWindow={alive}, IsWindowVisible={visible}, ChildPid={childPid}";
        }

        Console.WriteLine($"Checker result: {result}");
        File.WriteAllText(resultFile, result);
        return 0;
    }

    // -------------------------------------------------------------------------
    // Spawn guardrails
    // -------------------------------------------------------------------------
    private static Process SpawnGuarded(Func<Process> spawn)
    {
        lock (SpawnLock)
        {
            if (_spawnCount >= MaxTotalSpawns)
                throw new InvalidOperationException($"Spawn cap of {MaxTotalSpawns} exceeded — aborting.");

            Log($"Spawning process {_spawnCount + 1}/{MaxTotalSpawns}...");
            Process p = spawn();
            if (p == null)
                throw new InvalidOperationException("SpawnGuarded received a null Process.");

            _spawnCount++;
            SpawnedProcesses.Add(p);
            Log($"Spawned PID {p.Id}: {p.ProcessName}");
            return p;
        }
    }

    private static Process StartChildProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        Process p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine($"[{p.Id} OUT] {e.Data}"); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine($"[{p.Id} ERR] {e.Data}"); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private static void KillAllTracked()
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
                    Console.WriteLine($"Killing tracked process {p.Id} ({p.ProcessName})...");
                    p.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to kill process {p.Id}: {ex.Message}");
            }
        }
    }

    private static async Task DrainOutputAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                await process.WaitForExitAsync(CancellationToken.None);
        }
        catch { }
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    private static string GetOwnExePath()
    {
        string? assembly = typeof(Program).Assembly.Location;
        if (string.IsNullOrEmpty(assembly))
            throw new InvalidOperationException("Cannot determine spike executable path.");
        return Path.ChangeExtension(assembly, ".exe");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    /// <summary>
    /// Finds the main window of the specific <paramref name="expectedPid"/> cmd.exe
    /// process this orchestrator spawned. Class/title matching alone is not enough:
    /// a pre-existing console (or any window titled "...cmd.exe") that the user owns
    /// would otherwise be reparented, restyled, and killed by the spike. The PID
    /// check via GetWindowThreadProcessId is what confines the spike to its own
    /// spawned process.
    ///
    /// Note: on Windows 10/11 a console window is owned by conhost.exe, not by
    /// cmd.exe itself, so the window's PID is never cmd.Id directly. The spawned
    /// cmd allocates a fresh console, whose conhost has the spawned cmd as its
    /// parent — so ownership is verified as "window PID == cmd.Id, or window
    /// PID's parent == cmd.Id" (parent looked up via a Toolhelp process
    /// snapshot). A pre-existing console fails both arms: its conhost's parent
    /// is a different cmd.exe.
    /// </summary>
    private static IntPtr FindCmdWindow(int expectedPid, out int processId)
    {
        processId = 0;
        IntPtr found = IntPtr.Zero;
        int localPid = 0;
        // One snapshot per call, consulted only for windows that already match
        // on class/title — building it per window made each retry attempt cost
        // seconds and starved the overall timeout.
        Dictionary<int, int> parentByPid = SnapshotParentPids();

        EnumWindows((hwnd, lParam) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);

            bool isCmd = className.ToString().Equals("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase)
                || title.ToString().Contains("cmd.exe", StringComparison.OrdinalIgnoreCase);
            if (!isCmd || title.Length == 0)
                return true;

            uint pid = 0;
            GetWindowThreadProcessId(hwnd, out pid);
            bool owned = (int)pid == expectedPid
                || (parentByPid.TryGetValue((int)pid, out int parentPid) && parentPid == expectedPid);
            if (!owned)
                return true; // Not our spawned cmd.exe's console — never touch it.

            found = hwnd;
            localPid = (int)pid;
            return false;
        }, IntPtr.Zero);
        processId = localPid;
        return found;
    }

    /// <summary>
    /// Maps every PID in a Toolhelp32 process snapshot to its parent PID.
    /// </summary>
    private static Dictionary<int, int> SnapshotParentPids()
    {
        var map = new Dictionary<int, int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(NativeConstants.TH32CS_SNAPPROCESS, 0);
        if (snapshot == new IntPtr(-1) || snapshot == IntPtr.Zero)
            return map;
        try
        {
            PROCESSENTRY32 entry = new PROCESSENTRY32 { dwSize = Marshal.SizeOf(typeof(PROCESSENTRY32)) };
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                }
                while (Process32Next(snapshot, ref entry));
            }
            return map;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    /// <summary>
    /// Returns the parent PID of <paramref name="pid"/> via a Toolhelp32 process
    /// snapshot, or 0 if the process cannot be found.
    /// </summary>
    private static int GetParentProcessId(int pid)
    {
        var map = SnapshotParentPids();
        return map.TryGetValue(pid, out int parentPid) ? parentPid : 0;
    }

    private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeConstants.WM_DESTROY)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // -------------------------------------------------------------------------
    // P/Invoke
    // -------------------------------------------------------------------------
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
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
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public int dwSize;
        public int cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public int cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
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

    private static class NativeConstants
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        public const uint WS_VISIBLE = 0x10000000;
        public const uint WS_CHILD = 0x40000000;
        public const uint WS_POPUP = 0x80000000;
        public const uint WS_CAPTION = 0x00C00000;
        public const uint WS_THICKFRAME = 0x00040000;
        public const uint WS_MINIMIZEBOX = 0x00020000;
        public const uint WS_MAXIMIZEBOX = 0x00010000;
        public const uint WS_SYSMENU = 0x00080000;
        public const uint WS_CLIPSIBLINGS = 0x04000000;

        public const uint WS_EX_WINDOWEDGE = 0x00000100;
        public const uint WS_EX_CLIENTEDGE = 0x00000200;
        public const uint WS_EX_DLGMODALFRAME = 0x00000001;

        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const int SW_SHOW = 5;
        public const uint CW_USEDEFAULT = 0x80000000;

        public const uint WM_DESTROY = 0x0002;
        public const uint GW_OWNER = 4;
        public const uint PROCESS_SYNCHRONIZE = 0x00100000;
        public const uint TH32CS_SNAPPROCESS = 0x00000002;
    }
}
