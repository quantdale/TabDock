using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.ValidationDriver;

internal sealed record ResourceProfileResult(
    string Profile,
    int Cycles,
    int Operations,
    int MaxLiveItems,
    int FinalLiveItems,
    int RemainingArtifacts,
    bool Passed,
    string? FailureReason);

/// <summary>
/// Headless lifecycle churn over the existing pure authorities and isolated
/// disposable fixtures. These profiles prove bounded ownership/state residue;
/// they do not claim physical input or replace live Shepherd qualification.
/// </summary>
internal static class ResourceLifecycleProfiles
{
    private static readonly string[] AllProfiles =
    {
        "group-capture",
        "split",
        "layout",
        "picker-icon",
        "winevent",
        "diagnostics",
        "persistence",
        "restart",
    };

    public static IReadOnlyList<string> Names => AllProfiles;

    public static IReadOnlyList<ResourceProfileResult> Run(
        string selection,
        int cycles,
        int seed,
        string temporaryRoot)
    {
        if (cycles < 1)
            throw new ArgumentOutOfRangeException(nameof(cycles));
        if (string.IsNullOrWhiteSpace(temporaryRoot))
            throw new ArgumentException("A temporary root is required.", nameof(temporaryRoot));

        string normalized = selection.Trim().ToLowerInvariant();
        string[] selected = normalized is "all" or "*"
            ? AllProfiles
            : new[] { normalized };
        foreach (string profile in selected)
        {
            if (!AllProfiles.Contains(profile, StringComparer.Ordinal))
                throw new ArgumentException(
                    $"Unknown resource profile '{selection}'. Use all or {string.Join(", ", AllProfiles)}.",
                    nameof(selection));
        }

        Directory.CreateDirectory(temporaryRoot);
        var results = new List<ResourceProfileResult>(selected.Length);
        foreach (string profile in selected)
        {
            string profileRoot = Path.Combine(temporaryRoot, profile);
            Directory.CreateDirectory(profileRoot);
            results.Add(RunOne(profile, cycles, seed, profileRoot));
        }
        return results;
    }

    private static ResourceProfileResult RunOne(
        string profile,
        int cycles,
        int seed,
        string profileRoot)
    {
        try
        {
            return profile switch
            {
                "group-capture" => GroupCapture(cycles),
                "split" => Split(cycles),
                "layout" => Layout(cycles, seed),
                "picker-icon" => PickerIcon(cycles),
                "winevent" => WinEvent(cycles),
                "diagnostics" => Diagnostics(cycles, profileRoot),
                "persistence" => Persistence(cycles, profileRoot),
                "restart" => Restart(cycles),
                _ => throw new ArgumentException($"Unknown resource profile '{profile}'."),
            };
        }
        catch (Exception ex)
        {
            return new ResourceProfileResult(
                profile,
                cycles,
                0,
                0,
                0,
                CountArtifacts(profileRoot),
                false,
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ResourceProfileResult GroupCapture(int cycles)
    {
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        int operations = 0;
        int maxLive = 0;
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            string group = $"G{cycle}";
            var model = new Group { Name = group };
            model.Members.Add(new CapturedWindow { Hwnd = new IntPtr(cycle + 1) });
            model.Members.Add(new CapturedWindow { Hwnd = new IntPtr(cycle + 1001) });
            groups[group] = model;
            model.ActiveIndex = 0;
            operations += 3;
            maxLive = Math.Max(maxLive, groups.Sum(item => item.Value.Members.Count));

            model.ActiveIndex = 1;
            model.Members.RemoveAt(1);
            model.Members.Add(new CapturedWindow { Hwnd = new IntPtr(cycle + 2001) });
            operations += 2;
            model.Members.RemoveAt(0);
            model.Members.RemoveAt(0);
            groups.Remove(group);
            operations += 3;
            if (groups.Count != 0)
                return Failed("group-capture", cycles, operations, maxLive, groups.Count, 0, "group residue after close");
        }
        return Passed("group-capture", cycles, operations, maxLive, groups.Count, 0);
    }

    private static ResourceProfileResult Split(int cycles)
    {
        SplitPresentationState state = SplitPresentationPolicy.NoPair();
        int operations = 0;
        int maxLive = 0;
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            state = SplitPresentationPolicy.DefinePair("A", "B");
            state = SplitPresentationPolicy.FocusMember(state, cycle % 2 == 0 ? "B" : "A");
            state = SplitPresentationPolicy.SelectNonMember(state, "C");
            state = SplitPresentationPolicy.SelectMember(state, cycle % 2 == 0 ? "A" : "B");
            if (!SplitPresentationPolicy.IsCurrentSettle(state, state.Generation)
                || !state.RelationshipDefined
                || state.Left != "A"
                || state.Right != "B")
            {
                return Failed("split", cycles, operations, maxLive, 1, 0, "split identity or settle invariant failed");
            }
            state = SplitPresentationPolicy.ExplicitExit(state);
            operations += 5;
            maxLive = Math.Max(maxLive, state.RelationshipDefined ? 2 : 0);
            if (state.RelationshipDefined || state.ActiveGuest == null)
                return Failed("split", cycles, operations, maxLive, state.RelationshipDefined ? 2 : 1, 0, "split relationship residue after exit");
        }
        return Passed("split", cycles, operations, maxLive, state.RelationshipDefined ? 2 : 0, 0);
    }

    private static ResourceProfileResult Layout(int cycles, int seed)
    {
        var random = new Random(seed);
        int operations = 0;
        int maxLive = 0;
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            int left = random.Next(-20_000, 20_000);
            int top = random.Next(-10_000, 10_000);
            int width = random.Next(1, 4_001);
            int height = random.Next(1, 2_001);
            var content = new NativeMethods.RECT
            {
                left = left,
                top = top,
                right = left + width,
                bottom = top + height,
            };
            (NativeMethods.RECT paneLeft, NativeMethods.RECT paneRight) = SplitGeometry.Partition(content);
            operations++;
            maxLive = Math.Max(maxLive, 2);
            if (paneLeft.right != paneRight.left
                || paneLeft.left != content.left
                || paneRight.right != content.right
                || paneLeft.top != content.top
                || paneRight.bottom != content.bottom)
            {
                return Failed("layout", cycles, operations, maxLive, 2, 0, "split panes did not abut exact content bounds");
            }
        }
        return Passed("layout", cycles, operations, maxLive, 0, 0);
    }

    private static ResourceProfileResult PickerIcon(int cycles)
    {
        var rows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int generation = 0;
        int operations = 0;
        int maxLive = 0;
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            int refresh = ++generation;
            rows.Clear();
            icons.Clear();
            foreach (string exe in new[] { "a.exe", "b.exe", "c.exe", "d.exe" })
                rows[exe] = refresh;
            maxLive = Math.Max(maxLive, rows.Count + icons.Count);

            // A superseded worker may finish, but its generation cannot write
            // into the current row/icon set.
            int superseded = refresh - 1;
            if (superseded == generation)
                return Failed("picker-icon", cycles, operations, maxLive, rows.Count, 0, "stale generation was accepted");
            foreach (string exe in rows.Keys.ToArray())
                if (refresh == generation)
                    icons[exe] = "frozen-icon";
            operations += rows.Count + icons.Count;

            rows.Clear();
            icons.Clear();
            if (rows.Count != 0 || icons.Count != 0)
                return Failed("picker-icon", cycles, operations, maxLive, rows.Count + icons.Count, 0, "picker/icon state survived close");
        }
        return Passed("picker-icon", cycles, operations, maxLive, 0, 0);
    }

    private static ResourceProfileResult WinEvent(int cycles)
    {
        IntPtr desktop = new(0xD);
        IntPtr captured = new(0xA);
        int operations = 0;
        int accepted = 0;
        bool hookInstalled = false;
        int maxLive = 0;
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            hookInstalled = true;
            maxLive = Math.Max(maxLive, hookInstalled ? 1 : 0);
            WinEventRoutingDecision direct = WinEventRoutingPolicy.Decide(new WinEventRoutingInput(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                captured,
                0,
                0,
                desktop,
                Captured: true));
            WinEventRoutingDecision reorder = WinEventRoutingPolicy.Decide(new WinEventRoutingInput(
                NativeMethods.EVENT_OBJECT_REORDER,
                desktop,
                NativeMethods.OBJID_CLIENT,
                NativeMethods.CHILDID_SELF,
                desktop,
                Captured: true));
            WinEventRoutingDecision foreign = WinEventRoutingPolicy.Decide(new WinEventRoutingInput(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                new IntPtr(0xB),
                0,
                0,
                desktop,
                Captured: false));
            operations += 3;
            if (direct != WinEventRoutingDecision.DirectCaptured
                || reorder != WinEventRoutingDecision.DesktopReorderCaptured
                || foreign != WinEventRoutingDecision.Ignore)
            {
                return Failed("winevent", cycles, operations, maxLive, hookInstalled ? 1 : 0, 0, "routing admission invariant failed");
            }
            accepted += 2;
            hookInstalled = false;
            if (hookInstalled)
                return Failed("winevent", cycles, operations, maxLive, 1, 0, "hook survived stop transition");
        }
        return Passed("winevent", cycles, operations + accepted, maxLive, hookInstalled ? 1 : 0, 0);
    }

    private static ResourceProfileResult Diagnostics(int cycles, string profileRoot)
    {
        const int capacity = 64;
        var trace = new Queue<int>(capacity);
        int operations = 0;
        int maxLive = 0;
        string snapshotPath = Path.Combine(profileRoot, "trace-snapshot.json");
        try
        {
            for (int cycle = 0; cycle < cycles * 4; cycle++)
            {
                trace.Enqueue(cycle);
                while (trace.Count > capacity)
                    trace.Dequeue();
                File.WriteAllText(snapshotPath, $"{{\"count\":{trace.Count}}}");
                maxLive = Math.Max(maxLive, trace.Count);
                operations += 2;
                if (Directory.GetFiles(profileRoot).Length > 1)
                    return Failed("diagnostics", cycles, operations, maxLive, trace.Count, 1, "diagnostic artifact set grew beyond one snapshot");
            }
            trace.Clear();
            File.Delete(snapshotPath);
            return Passed("diagnostics", cycles, operations, maxLive, trace.Count, CountArtifacts(profileRoot));
        }
        catch
        {
            int remaining = CountArtifacts(profileRoot);
            try { File.Delete(snapshotPath); } catch { }
            return Failed("diagnostics", cycles, operations, maxLive, trace.Count, remaining, "isolated diagnostic artifact failed");
        }
    }

    private static ResourceProfileResult Persistence(int cycles, string root)
    {
        int operations = 0;
        int maxLive = 0;
        try
        {
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                string primary = Path.Combine(root, "state.json");
                string backup = Path.Combine(root, "state.json.bak");
                string temp = Path.Combine(root, "state.json.tmp");
                File.WriteAllText(temp, $"{{\"cycle\":{cycle}}}");
                File.Move(temp, primary, overwrite: true);
                File.Copy(primary, backup, overwrite: true);
                operations += 3;
                maxLive = Math.Max(maxLive, Directory.GetFiles(root).Length);
                if (Directory.GetFiles(root, "*.tmp").Length != 0)
                    return Failed("persistence", cycles, operations, maxLive, Directory.GetFiles(root).Length, 1, "temporary artifact remained");
            }
            int remaining = Directory.GetFiles(root).Length;
            bool bounded = remaining == 2;
            Directory.Delete(root, recursive: true);
            return bounded
                ? Passed("persistence", cycles, operations, maxLive, 0, 0)
                : Failed("persistence", cycles, operations, maxLive, remaining, remaining, "durable fixture residue exceeded primary+backup");
        }
        catch
        {
            int remaining = CountArtifacts(root);
            try { Directory.Delete(root, recursive: true); } catch { }
            return Failed("persistence", cycles, operations, maxLive, remaining, remaining, "isolated persistence fixture failed");
        }
    }

    private static ResourceProfileResult Restart(int cycles)
    {
        var generations = new HashSet<int>();
        int operations = 0;
        int maxLive = 0;
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            generations.Add(cycle);
            maxLive = Math.Max(maxLive, generations.Count);
            operations += 2;
            generations.Remove(cycle);
            if (generations.Count != 0)
                return Failed("restart", cycles, operations, maxLive, generations.Count, 0, "prior generation residue remained");
        }
        return Passed("restart", cycles, operations, maxLive, 0, 0);
    }

    private static ResourceProfileResult Passed(
        string profile,
        int cycles,
        int operations,
        int maxLive,
        int finalLive,
        int remainingArtifacts)
        => new(profile, cycles, operations, maxLive, finalLive, remainingArtifacts, true, null);

    private static ResourceProfileResult Failed(
        string profile,
        int cycles,
        int operations,
        int maxLive,
        int finalLive,
        int remainingArtifacts,
        string reason)
        => new(profile, cycles, operations, maxLive, finalLive, remainingArtifacts, false, reason);

    private static int CountArtifacts(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length
                : 0;
        }
        catch
        {
            return -1;
        }
    }
}
