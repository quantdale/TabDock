using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Deterministic coverage for the release transaction's identity boundary.
/// The fake native APIs count mutations, so an unverifiable strong probe can
/// be proven not to touch the possibly-wrong HWND while its journal remains.
/// </summary>
internal static class WindowReleaseSelfTest
{
    public static (int Checks, int Failures) Run()
    {
        int checks = 0;
        int failures = 0;
        void Check(bool condition)
        {
            checks++;
            if (!condition)
                failures++;
        }

        Check(ValidStrongIdentityReleases());
        Check(DefinitePidMismatchDoesNotMutate());
        Check(DefiniteTokenMismatchDoesNotMutate());
        Check(DefiniteStartMismatchDoesNotMutate());
        Check(UnavailableStartPreservesJournal());
        Check(ExecutableProbeFailurePreservesJournal());
        Check(NativeVerificationExceptionPreservesJournal());
        Check(DefiniteExecutableMismatchDoesNotMutate());
        Check(SameHwndChangedMetadataCannotClearReplacement());
        Check(OldSameHwndJournalCannotClearReplacement());
        Check(TokenRemovalFailureLeavesFutureCaptureClosed());
        Check(UnverifiableHiddenReleasePreservesJournal());
        Check(UnverifiableVisibleReleasePreservesJournal());
        Check(EmergencyReleaseContinuesPastPendingMember());
        Check(CloseGroupRetainsPendingMember());
        Check(LaterRetryCompletesPreviouslyUnverifiableRelease());
        return (checks, failures);
    }

    private static bool ValidStrongIdentityReleases()
    {
        using TestFixture fixture = TestFixture.Create();
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        return result == WindowReleaseOutcome.Released
            && fixture.Native.MutationCount > 0
            && fixture.ReadEntries().Count == 0
            && fixture.Identity.CaptureToken == IntPtr.Zero;
    }

    private static bool DefinitePidMismatchDoesNotMutate()
    {
        using TestFixture fixture = TestFixture.Create();
        fixture.Identity.Identity = new WindowProcessIdentity(99, fixture.Captured.WindowThreadId);
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        return result == WindowReleaseOutcome.TargetGoneOrRecycled
            && fixture.Native.MutationCount == 0
            && fixture.ReadEntries().Count == 0;
    }

    private static bool DefiniteTokenMismatchDoesNotMutate()
    {
        using TestFixture fixture = TestFixture.Create();
        fixture.Identity.CaptureToken = new IntPtr(2002);
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        return result == WindowReleaseOutcome.TargetGoneOrRecycled
            && fixture.Native.MutationCount == 0
            && fixture.ReadEntries().Count == 0;
    }

    private static bool DefiniteStartMismatchDoesNotMutate()
    {
        using TestFixture fixture = TestFixture.Create();
        fixture.Identity.ProcessStartTicks = 202;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        return result == WindowReleaseOutcome.TargetGoneOrRecycled
            && fixture.Native.MutationCount == 0
            && fixture.ReadEntries().Count == 0;
    }

    private static bool UnavailableStartPreservesJournal()
    {
        using TestFixture fixture = TestFixture.Create();
        fixture.Identity.ProcessStartTicks = 0;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        return result == WindowReleaseOutcome.RecoveryPending
            && fixture.Native.MutationCount == 0
            && fixture.ReadEntries().Count == 1;
    }

    private static bool ExecutableProbeFailurePreservesJournal()
    {
        using TestFixture fixture = TestFixture.Create();
        fixture.Identity.ThrowOnExecutableProbe = true;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        return result == WindowReleaseOutcome.RecoveryPending
            && fixture.Native.MutationCount == 0
            && fixture.ReadEntries().Count == 1;
    }

    private static bool NativeVerificationExceptionPreservesJournal()
    {
        using TestFixture fixture = TestFixture.Create();
        fixture.Identity.ThrowOnClassProbe = true;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        return result == WindowReleaseOutcome.RecoveryPending
            && fixture.Native.MutationCount == 0
            && fixture.ReadEntries().Count == 1;
    }

    private static bool DefiniteExecutableMismatchDoesNotMutate()
    {
        using TestFixture fixture = TestFixture.Create();
        fixture.Identity.ExePath = "replacement.exe";
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        return result == WindowReleaseOutcome.TargetGoneOrRecycled
            && fixture.Native.MutationCount == 0
            && fixture.ReadEntries().Count == 0;
    }

    private static bool SameHwndChangedMetadataCannotClearReplacement()
    {
        using TestFixture fixture = TestFixture.Create();
        HiddenWindowEntry replacement = new()
        {
            Hwnd = fixture.Entry.Hwnd,
            Pid = fixture.Entry.Pid,
            WindowThreadId = fixture.Entry.WindowThreadId,
            WindowIdentityToken = fixture.Entry.WindowIdentityToken,
            ExePath = "replacement.exe",
            ClassName = fixture.Entry.ClassName,
            ProcessStartTimeUtcTicks = fixture.Entry.ProcessStartTimeUtcTicks,
            OriginallyVisible = true,
            HasOriginalPlacement = true,
            OriginalShowCommand = NativeMethods.SW_SHOW,
            OriginalNormalRight = 400,
            OriginalNormalBottom = 300,
        };
        fixture.WriteEntries(replacement);
        fixture.Identity.ExePath = "replacement.exe";
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        List<HiddenWindowEntry> remaining = fixture.ReadEntries();
        return result == WindowReleaseOutcome.TargetGoneOrRecycled
            && fixture.Native.MutationCount == 0
            && remaining.Count == 1
            && remaining[0].ExePath == "replacement.exe";
    }

    private static bool OldSameHwndJournalCannotClearReplacement()
    {
        using TestFixture fixture = TestFixture.Create();
        HiddenWindowEntry replacement = new()
        {
            Hwnd = fixture.Entry.Hwnd,
            Pid = fixture.Entry.Pid,
            WindowThreadId = fixture.Entry.WindowThreadId,
            WindowIdentityToken = 2002,
            ExePath = "replacement.exe",
            ClassName = fixture.Entry.ClassName,
            ProcessStartTimeUtcTicks = 202,
            OriginallyVisible = true,
            HasOriginalPlacement = true,
            OriginalShowCommand = NativeMethods.SW_SHOW,
            OriginalNormalRight = 400,
            OriginalNormalBottom = 300,
        };
        fixture.WriteEntries(fixture.Entry, replacement);
        fixture.Identity.CaptureToken = new IntPtr(2002);
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        List<HiddenWindowEntry> remaining = fixture.ReadEntries();
        return result == WindowReleaseOutcome.TargetGoneOrRecycled
            && fixture.Native.MutationCount == 0
            && remaining.Count == 1
            && remaining[0].WindowIdentityToken == 2002
            && remaining[0].ExePath == "replacement.exe";
    }

    private static bool UnverifiableHiddenReleasePreservesJournal()
    {
        using TestFixture fixture = TestFixture.Create();
        fixture.Captured.OriginallyVisible = false;
        fixture.Identity.ProcessStartTicks = 0;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured, show: false);
        return result == WindowReleaseOutcome.RecoveryPending
            && fixture.Native.MutationCount == 0
            && fixture.ReadEntries().Count == 1;
    }

    private static bool TokenRemovalFailureLeavesFutureCaptureClosed()
    {
        using TestFixture fixture = TestFixture.Create();
        fixture.Identity.FailTokenRemoval = true;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured);
        return result == WindowReleaseOutcome.Released
            && fixture.ReadEntries().Count == 0
            && fixture.Identity.CaptureToken == new IntPtr(fixture.Captured.WindowIdentityToken)
            && !WindowIdentityGate.IsCaptureTokenAvailable(fixture.Captured.Hwnd, fixture.Identity);
    }

    private static bool UnverifiableVisibleReleasePreservesJournal()
    {
        using TestFixture fixture = TestFixture.Create();
        fixture.Identity.ProcessStartTicks = 0;
        WindowReleaseOutcome result = fixture.Service.Release(fixture.Captured, show: true);
        return result == WindowReleaseOutcome.RecoveryPending
            && fixture.Native.MutationCount == 0
            && fixture.ReadEntries().Count == 1;
    }

    private static bool EmergencyReleaseContinuesPastPendingMember()
    {
        using TestFixture fixture = TestFixture.Create(twoEntries: true);
        fixture.IdentityForSecond!.ProcessStartTicks = 0;
        var persistence = new PersistenceService(fixture.Log, fixture.StatePath);
        var groups = new GroupManager(fixture.Service, persistence, fixture.Log);
        var group = groups.CreateGroup("release-self-test");
        group.Members.Add(fixture.Captured);
        group.Members.Add(fixture.CapturedSecond!);

        groups.EmergencyReleaseAll();
        return group.Members.Count == 1
            && ReferenceEquals(group.Members[0], fixture.CapturedSecond)
            && fixture.Native.MutationCount > 0
            && fixture.ReadEntries().Count == 1
            && fixture.ReadEntries()[0].WindowIdentityToken == fixture.CapturedSecond!.WindowIdentityToken;
    }

    private static bool LaterRetryCompletesPreviouslyUnverifiableRelease()
    {
        using TestFixture fixture = TestFixture.Create();
        fixture.Identity.ProcessStartTicks = 0;
        WindowReleaseOutcome first = fixture.Service.Release(fixture.Captured);
        fixture.Identity.ProcessStartTicks = fixture.Captured.ProcessStartTimeUtcTicks;
        WindowReleaseOutcome second = fixture.Service.Release(fixture.Captured);
        return first == WindowReleaseOutcome.RecoveryPending
            && second == WindowReleaseOutcome.Released
            && fixture.ReadEntries().Count == 0
            && fixture.Native.MutationCount > 0;
    }

    private static bool CloseGroupRetainsPendingMember()
    {
        using TestFixture fixture = TestFixture.Create(twoEntries: true);
        fixture.IdentityForSecond!.ProcessStartTicks = 0;
        var persistence = new PersistenceService(fixture.Log, fixture.StatePath);
        var groups = new GroupManager(fixture.Service, persistence, fixture.Log);
        var group = groups.CreateGroup("close-self-test");
        group.Members.Add(fixture.Captured);
        group.Members.Add(fixture.CapturedSecond!);

        bool closed = groups.CloseGroup(group);
        return !closed
            && groups.Groups.Contains(group)
            && group.Members.Count == 1
            && ReferenceEquals(group.Members[0], fixture.CapturedSecond)
            && fixture.ReadEntries().Count == 1
            && fixture.Native.MutationCount > 0;
    }

    private sealed class TestFixture : IDisposable
    {
        private TestFixture(string root, string journalPath, string statePath, LoggingService log,
            FakeIdentityApi identity, FakeReleaseApi native, CapturedWindow captured,
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
        public FakeIdentityApi Identity { get; }
        public FakeIdentityApi? IdentityForSecond { get; private set; }
        public FakeReleaseApi Native { get; }
        public CapturedWindow Captured { get; }
        public CapturedWindow? CapturedSecond { get; private set; }
        public HiddenWindowEntry Entry { get; }
        public WindowShepherdService Service { get; }

        public static TestFixture Create(bool twoEntries = false)
        {
            string root = Path.Combine(Path.GetTempPath(), "TabDock-release-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string journalPath = Path.Combine(root, "hidden-windows.json");
            string statePath = Path.Combine(root, "state.json");
            LoggingService tempLog = new LoggingService(Path.Combine(root, "logs"));
            FakeIdentityApi identity = FakeIdentityApi.For(1, 11, 101, 1001);
            FakeReleaseApi native = new();
            CapturedWindow captured = CapturedWindowFor(1, 11, 1001, 101);
            HiddenWindowEntry entry = JournalEntryFor(captured);
            var entries = new List<HiddenWindowEntry> { entry };
            if (twoEntries)
            {
                CapturedWindow second = CapturedWindowFor(2, 22, 2002, 202);
                entries.Add(JournalEntryFor(second));
                FakeIdentityApi secondIdentity = FakeIdentityApi.For(2, 22, 202, 2002);
                identity.Add(secondIdentity);
                TestFixture fixture = new(root, journalPath, statePath, tempLog, identity, native, captured, entry,
                    new WindowShepherdService(tempLog, journalPath, identity, new FakeMonitorDpiProbe(), native));
                fixture.CapturedSecond = second;
                fixture.IdentityForSecond = secondIdentity;
                fixture.WriteEntries(entries.ToArray());
                fixture.Service.BindCapturedWindowForTesting(captured);
                fixture.Service.BindCapturedWindowForTesting(second);
                return fixture;
            }

            var result = new TestFixture(root, journalPath, statePath, tempLog, identity, native, captured, entry,
                new WindowShepherdService(tempLog, journalPath, identity, new FakeMonitorDpiProbe(), native));
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

        private static CapturedWindow CapturedWindowFor(long hwnd, uint pid, long token, long start)
            => new()
            {
                Hwnd = new IntPtr(hwnd),
                ProcessId = pid,
                WindowThreadId = pid + 1000,
                WindowIdentityToken = token,
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

        private static HiddenWindowEntry JournalEntryFor(CapturedWindow captured)
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

    private sealed class FakeIdentityApi : IWindowIdentityNativeApi
    {
        private readonly Dictionary<IntPtr, FakeIdentityApi> _identities = new();
        private readonly IntPtr _hwnd;
        private readonly uint _pid;
        public WindowProcessIdentity Identity { get; set; }
        public IntPtr CaptureToken { get; set; }
        public string? ExePath { get; set; }
        public string ClassName { get; set; } = "Pig";
        public long ProcessStartTicks { get; set; }
        public bool ThrowOnExecutableProbe { get; set; }
        public bool ThrowOnClassProbe { get; set; }
        public bool FailTokenRemoval { get; set; }

        private FakeIdentityApi(IntPtr hwnd, uint pid, long start, long token)
        {
            _hwnd = hwnd;
            _pid = pid;
            Identity = new WindowProcessIdentity(pid, pid + 1000);
            ProcessStartTicks = start;
            CaptureToken = new IntPtr(token);
            ExePath = $"guest-{pid}.exe";
        }

        public static FakeIdentityApi For(long hwnd, uint pid, long start, long token)
            => new(new IntPtr(hwnd), pid, start, token);

        public void Add(FakeIdentityApi other) => _identities[other._hwnd] = other;

        public IntPtr GetCaptureIdentityToken(IntPtr hwnd)
            => Find(hwnd).CaptureToken;

        public bool IsWindow(IntPtr hwnd) => FindOrNull(hwnd) != null;

        public WindowProcessIdentity GetProcessIdentity(IntPtr hwnd)
            => Find(hwnd).Identity;

        public string? GetProcessImagePath(uint pid)
        {
            FakeIdentityApi item = FindByPid(pid);
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
            if (FailTokenRemoval)
                return false;
            FakeIdentityApi item = Find(hwnd);
            if (item.CaptureToken != expectedToken)
                return false;
            item.CaptureToken = IntPtr.Zero;
            return true;
        }

        private FakeIdentityApi Find(IntPtr hwnd)
            => FindOrNull(hwnd) ?? throw new InvalidOperationException("unknown fake HWND");

        private FakeIdentityApi? FindOrNull(IntPtr hwnd)
            => hwnd == _hwnd ? this : (_identities.TryGetValue(hwnd, out FakeIdentityApi? item) ? item : null);

        private FakeIdentityApi FindByPid(uint pid)
        {
            if (_pid == pid)
                return this;
            return _identities.Values.First(item => item._pid == pid);
        }
    }

    private sealed class FakeReleaseApi : IWindowReleaseNativeApi
    {
        private readonly Dictionary<IntPtr, bool> _visible = new();
        public int MutationCount { get; private set; }
        public bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement)
        {
            MutationCount++;
            return true;
        }
        public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags)
        {
            MutationCount++;
            return true;
        }
        public bool ShowWindow(IntPtr hwnd, int command)
        {
            MutationCount++;
            _visible.TryGetValue(hwnd, out bool previous);
            _visible[hwnd] = command != NativeMethods.SW_HIDE;
            return previous;
        }
        public bool IsWindowVisible(IntPtr hwnd)
            => !_visible.TryGetValue(hwnd, out bool visible) || visible;
        public bool SetForegroundWindow(IntPtr hwnd)
        {
            MutationCount++;
            return true;
        }
        public IntPtr GetForegroundWindow() => IntPtr.Zero;
        public int SetTransitionsDisabled(IntPtr hwnd, int value)
        {
            MutationCount++;
            return 0;
        }
        public string DescribeWindow(IntPtr hwnd) => "fake";
    }

    private sealed class FakeMonitorDpiProbe : IMonitorDpiProbe
    {
        public uint GetEffectiveDpi(IntPtr monitor) => 96;
    }
}
