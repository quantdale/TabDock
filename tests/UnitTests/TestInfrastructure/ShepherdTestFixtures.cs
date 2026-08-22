using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.UnitTests.TestInfrastructure;

/// <summary>
/// Shared hermetic fixture for WindowShepherdService release/hide/stabilization
/// coverage (migrated from the former WindowReleaseSelfTest, Wave 4). The fake
/// native APIs count mutations, so an unverifiable strong probe can be proven
/// not to touch the possibly-wrong HWND while its journal remains.
/// </summary>
internal sealed class ReleaseTestFixture : IDisposable
{
    private ReleaseTestFixture(string root, string journalPath, string statePath, LoggingService log,
        ShepherdFakeIdentityApi identity, ShepherdFakeReleaseApi native, CapturedWindow captured,
        HiddenWindowEntry entry, WindowShepherdService service)
    {
        Root = root;
        JournalPath = journalPath;
        StatePath = statePath;
        Log = log;
        Identity = identity;
        Native = native;
        Captured = captured;
        Entry = entry;
        Service = service;
    }

    public string Root { get; }
    public string JournalPath { get; }
    public string StatePath { get; }
    public LoggingService Log { get; }
    public ShepherdFakeIdentityApi Identity { get; }
    public ShepherdFakeIdentityApi? IdentityForSecond { get; private set; }
    public ShepherdFakeReleaseApi Native { get; }
    public CapturedWindow Captured { get; }
    public CapturedWindow? CapturedSecond { get; private set; }
    public HiddenWindowEntry Entry { get; }
    public WindowShepherdService Service { get; }

    public static ReleaseTestFixture Create(
        bool twoEntries = false,
        Action<string, ShepherdFakeIdentityApi>? sequencingHook = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-release-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string journalPath = Path.Combine(root, "hidden-windows.json");
        string statePath = Path.Combine(root, "state.json");
        LoggingService tempLog = new LoggingService(Path.Combine(root, "logs"));
        ShepherdFakeIdentityApi identity = ShepherdFakeIdentityApi.For(1, 11, 101, 1001);
        ShepherdFakeReleaseApi native = new();
        CapturedWindow captured = CapturedWindowFor(1, 11, 1001, 101);
        HiddenWindowEntry entry = JournalEntryFor(captured);
        var entries = new List<HiddenWindowEntry> { entry };
        if (twoEntries)
        {
            CapturedWindow second = CapturedWindowFor(2, 22, 2002, 202);
            entries.Add(JournalEntryFor(second));
            ShepherdFakeIdentityApi secondIdentity = ShepherdFakeIdentityApi.For(2, 22, 202, 2002);
            identity.Add(secondIdentity);
            ReleaseTestFixture fixture = new(root, journalPath, statePath, tempLog, identity, native, captured, entry,
                new WindowShepherdService(
                    tempLog,
                    journalPath,
                    identity,
                    new FakeMonitorDpiProbe(),
                    native,
                    testSequencingHook: sequencingHook == null
                        ? null
                        : stage => sequencingHook(stage, identity)));
            fixture.CapturedSecond = second;
            fixture.IdentityForSecond = secondIdentity;
            fixture.WriteEntries(entries.ToArray());
            fixture.Service.BindCapturedWindowForTesting(captured);
            fixture.Service.BindCapturedWindowForTesting(second);
            return fixture;
        }

        var result = new ReleaseTestFixture(root, journalPath, statePath, tempLog, identity, native, captured, entry,
            new WindowShepherdService(
                tempLog,
                journalPath,
                identity,
                new FakeMonitorDpiProbe(),
                native,
                testSequencingHook: sequencingHook == null
                    ? null
                    : stage => sequencingHook(stage, identity)));
        result.WriteEntries(entries.ToArray());
        result.Service.BindCapturedWindowForTesting(captured);
        return result;
    }

    public List<HiddenWindowEntry> ReadEntries()
    {
        if (!File.Exists(JournalPath))
            return new List<HiddenWindowEntry>();
        HiddenWindowJournalFile file = JsonSerializer.Deserialize(
            File.ReadAllText(JournalPath),
            TabDockJsonContext.Default.HiddenWindowJournalFile)!;
        return file.Entries;
    }

    public void WriteEntries(params HiddenWindowEntry[] entries)
    {
        var file = new HiddenWindowJournalFile { Version = 3, Entries = entries.ToList() };
        File.WriteAllText(JournalPath, JsonSerializer.Serialize(file, TabDockJsonContext.Default.HiddenWindowJournalFile));
    }

    public void Dispose()
    {
        Log.Dispose();
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch { }
    }

    internal static CapturedWindow CapturedWindowFor(long hwnd, uint pid, long token, long start)
        => new()
        {
            Hwnd = new IntPtr(hwnd),
            ProcessId = pid,
            WindowThreadId = pid + 1000,
            WindowIdentityToken = token,
            ReleasedCloseNonce = 0x4E4F4E430000 + hwnd,
            ProcessStartTimeUtcTicks = start,
            ExePath = $"guest-{pid}.exe",
            OriginalClassName = "Pig",
            OriginalTitle = "Guest",
            OriginallyVisible = true,
            HasValidPlacement = true,
            OriginalPlacement = new NativeMethods.WINDOWPLACEMENT
            {
                showCmd = NativeMethods.SW_SHOW,
                rcNormalPosition = new NativeMethods.RECT { right = 400, bottom = 300 },
            },
            OriginalBounds = new NativeMethods.RECT { right = 400, bottom = 300 },
        };

    internal static HiddenWindowEntry JournalEntryFor(CapturedWindow captured)
        => new()
        {
            Hwnd = captured.Hwnd.ToInt64(),
            Pid = captured.ProcessId,
            WindowThreadId = captured.WindowThreadId,
            WindowIdentityToken = captured.WindowIdentityToken,
            ExePath = captured.ExePath,
            ClassName = captured.OriginalClassName,
            ProcessStartTimeUtcTicks = captured.ProcessStartTimeUtcTicks,
            OriginallyVisible = captured.OriginallyVisible,
            HasOriginalPlacement = true,
            OriginalShowCommand = NativeMethods.SW_SHOW,
            OriginalNormalRight = 400,
            OriginalNormalBottom = 300,
        };
}

/// <summary>Recording IWindowIdentityNativeApi keyed by HWND (multi-window capable).</summary>
internal sealed class ShepherdFakeIdentityApi : IWindowIdentityNativeApi
{
    private readonly Dictionary<IntPtr, ShepherdFakeIdentityApi> _identities = new();
    private readonly IntPtr _hwnd;
    private readonly uint _pid;
    public WindowProcessIdentity Identity { get; set; }
    public IntPtr CaptureToken { get; set; }
    public IntPtr ReleasedCloseNonce { get; set; }
    public string? ExePath { get; set; }
    public string ClassName { get; set; } = "Pig";
    public long ProcessStartTicks { get; set; }
    public bool ThrowOnExecutableProbe { get; set; }
    public bool ThrowOnClassProbe { get; set; }
    public bool FailTokenRemoval { get; set; }
    public bool FailNonceInstallation { get; set; }
    public bool IsWindowAlive { get; set; } = true;
    public int TokenRemovalCount { get; private set; }
    public int NonceConsumptionCount { get; private set; }

    public void ReplaceGeneration()
        => CaptureToken = new IntPtr(2002);

    private ShepherdFakeIdentityApi(IntPtr hwnd, uint pid, long start, long token)
    {
        _hwnd = hwnd;
        _pid = pid;
        Identity = new WindowProcessIdentity(pid, pid + 1000);
        ProcessStartTicks = start;
        CaptureToken = new IntPtr(token);
        ExePath = $"guest-{pid}.exe";
    }

    public static ShepherdFakeIdentityApi For(long hwnd, uint pid, long start, long token)
        => new(new IntPtr(hwnd), pid, start, token);

    public void Add(ShepherdFakeIdentityApi other) => _identities[other._hwnd] = other;

    public IntPtr GetCaptureIdentityToken(IntPtr hwnd)
        => Find(hwnd).CaptureToken;

    public bool IsWindow(IntPtr hwnd) => IsWindowAlive && FindOrNull(hwnd) != null;

    public WindowProcessIdentity GetProcessIdentity(IntPtr hwnd)
        => Find(hwnd).Identity;

    public string? GetProcessImagePath(uint pid)
    {
        ShepherdFakeIdentityApi item = FindByPid(pid);
        if (item.ThrowOnExecutableProbe)
            throw new InvalidOperationException("synthetic executable probe failure");
        return item.ExePath;
    }

    public string? GetClassName(IntPtr hwnd)
    {
        if (Find(hwnd).ThrowOnClassProbe)
            throw new InvalidOperationException("synthetic class probe failure");
        return Find(hwnd).ClassName;
    }

    public long GetProcessStartTimeUtcTicks(uint pid) => FindByPid(pid).ProcessStartTicks;

    public bool RemoveCaptureIdentityToken(IntPtr hwnd, IntPtr expectedToken)
    {
        TokenRemovalCount++;
        if (FailTokenRemoval)
            return false;
        ShepherdFakeIdentityApi item = Find(hwnd);
        if (item.CaptureToken != expectedToken)
            return false;
        item.CaptureToken = IntPtr.Zero;
        return true;
    }

    public IntPtr GetReleasedCloseNonce(IntPtr hwnd)
        => Find(hwnd).ReleasedCloseNonce;

    public bool InstallReleasedCloseNonce(IntPtr hwnd, IntPtr nonce)
    {
        if (FailNonceInstallation || nonce == IntPtr.Zero)
            return false;
        Find(hwnd).ReleasedCloseNonce = nonce;
        return true;
    }

    public bool ConsumeReleasedCloseNonce(IntPtr hwnd, IntPtr expectedNonce)
    {
        NonceConsumptionCount++;
        ShepherdFakeIdentityApi item = Find(hwnd);
        if (item.ReleasedCloseNonce != expectedNonce || expectedNonce == IntPtr.Zero)
            return false;
        item.ReleasedCloseNonce = IntPtr.Zero;
        return true;
    }

    private ShepherdFakeIdentityApi Find(IntPtr hwnd)
        => FindOrNull(hwnd) ?? throw new InvalidOperationException("unknown fake HWND");

    private ShepherdFakeIdentityApi? FindOrNull(IntPtr hwnd)
        => hwnd == _hwnd ? this : (_identities.TryGetValue(hwnd, out ShepherdFakeIdentityApi? item) ? item : null);

    private ShepherdFakeIdentityApi FindByPid(uint pid)
    {
        if (_pid == pid)
            return this;
        return _identities.Values.First(item => item._pid == pid);
    }
}

/// <summary>Counting IWindowReleaseNativeApi with optional mid-sequence hooks.</summary>
internal sealed class ShepherdFakeReleaseApi : IWindowReleaseNativeApi
{
    private readonly Dictionary<IntPtr, bool> _visible = new();
    public int MutationCount { get; private set; }
    public int PlacementCount { get; private set; }
    public int ShowWindowCount { get; private set; }
    public int ForegroundCount { get; private set; }
    public int TransitionCount { get; private set; }
    public Action? AfterPlacement { get; set; }
    public Action? AfterTransitions { get; set; }
    public bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement)
    {
        MutationCount++;
        PlacementCount++;
        AfterPlacement?.Invoke();
        return true;
    }
    public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags)
    {
        MutationCount++;
        PlacementCount++;
        return true;
    }
    public bool ShowWindow(IntPtr hwnd, int command)
    {
        MutationCount++;
        ShowWindowCount++;
        bool previous = !_visible.TryGetValue(hwnd, out bool visible) || visible;
        _visible[hwnd] = command != NativeMethods.SW_HIDE;
        return previous;
    }
    public bool IsWindowVisible(IntPtr hwnd)
        => !_visible.TryGetValue(hwnd, out bool visible) || visible;
    public bool SetForegroundWindow(IntPtr hwnd)
    {
        MutationCount++;
        ForegroundCount++;
        return true;
    }
    public IntPtr GetForegroundWindow() => IntPtr.Zero;
    public int SetTransitionsDisabled(IntPtr hwnd, int value)
    {
        MutationCount++;
        TransitionCount++;
        AfterTransitions?.Invoke();
        return 0;
    }
    public string DescribeWindow(IntPtr hwnd) => "fake";
}

internal sealed class FakeMonitorDpiProbe : IMonitorDpiProbe
{
    public uint GetEffectiveDpi(IntPtr monitor) => 96;
}
