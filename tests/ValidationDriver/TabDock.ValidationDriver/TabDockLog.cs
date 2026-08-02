using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TabDock.ValidationDriver;

/// <summary>
/// Tails %APPDATA%\TabDock\logs\TabDock.log from a recorded offset so scenarios can
/// assert on exactly the lines TabDock wrote during an action window.
/// </summary>
internal static class TabDockLog
{
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TabDock", "logs", "TabDock.log");

    /// <summary>Current length of the log file; use as the offset for later reads.</summary>
    public static long RecordLogLength()
    {
        try
        {
            var fi = new FileInfo(LogPath);
            return fi.Exists ? fi.Length : 0L;
        }
        catch
        {
            return 0L;
        }
    }

    /// <summary>Lines appended since <paramref name="offset"/>. Handles the 1 MB rotation (file shrunk → reread from 0).</summary>
    public static string[] ReadNewLines(long offset)
    {
        try
        {
            using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < offset)
                offset = 0; // Log rotated underneath us.
            fs.Seek(offset, SeekOrigin.Begin);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            string content = sr.ReadToEnd();

            var lines = new List<string>();
            foreach (string raw in content.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length > 0)
                    lines.Add(line);
            }
            return lines.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static bool ContainsNewLine(long offset, string substring)
    {
        foreach (string line in ReadNewLines(offset))
        {
            if (line.IndexOf(substring, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    public static int CountNewLines(long offset, string substring)
    {
        int count = 0;
        foreach (string line in ReadNewLines(offset))
        {
            if (line.IndexOf(substring, StringComparison.OrdinalIgnoreCase) >= 0)
                count++;
        }
        return count;
    }

    public static bool WaitForLogLine(long offset, string substring, int timeoutMs)
    {
        return Util.WaitUntil(() => ContainsNewLine(offset, substring), timeoutMs, 150);
    }

    /// <summary>
    /// Waits until the churn identified by <paramref name="substring"/> has BOTH started
    /// and then gone quiet since <paramref name="offset"/>, up to <paramref name="timeoutMs"/>.
    /// The container's WM_ACTIVATE guest reassert (<c>SHEPHERD[bring-to-front]</c>) fires
    /// at an unbounded delay after a minimize->restore (observed ~1.5s to ~8.4s, see
    /// SelfMinimizeTimerVsTeardown), so merely waiting for a quiet window can return
    /// BEFORE the churn starts. Requiring at least one matching line first guarantees we
    /// only declare "settled" after the churn has actually fired and stopped — the state
    /// a popup context menu needs to survive its open->click lifetime (an open menu gets
    /// closed by an in-flight reassert). Returns false only if no start-then-quiet
    /// sequence completed within the timeout.
    /// </summary>
    public static bool WaitForChurnToSettle(long offset, string substring, int quietMs, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool seen = false;
        int quietCount = 0;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (CountNewLines(offset, substring) > 0)
            {
                seen = true;
                quietCount = 0;
            }
            else if (seen)
            {
                quietCount += 100;
                if (quietCount >= quietMs)
                    return true;
            }
            offset = RecordLogLength();
            System.Threading.Thread.Sleep(100);
        }
        return false;
    }

    /// <summary>All new lines since the offset as one dumpable block (empty string when none).</summary>
    public static string DumpNewLines(long offset)
    {
        return string.Join(Environment.NewLine, ReadNewLines(offset));
    }

    /// <summary>
    /// Analyzes the `Reordered tab {old}->{new} in group ...` lines (GroupManager.MoveTab,
    /// Services/GroupManager.cs) appended since <paramref name="offset"/>: returns the total
    /// reorder count and the number of immediate flip-back pairs — a reorder X→Y directly
    /// followed by Y→X for the same dragged tab. That reversal is the structural signature
    /// of the H2 drag-reorder oscillation (a tab dragged across a neighbor's midpoint
    /// re-fires MoveTab on every mouse tick, swapping back and forth), and it is
    /// machine-speed-independent, unlike a raw count. The count is a secondary churn
    /// ceiling: a correct frozen-midpoint drag produces a small bounded number of reorders,
    /// while the H2 bug produced hundreds of A↔B flips per drag.
    ///
    /// Hand-simulated sanity check (task 2.4): the lines "Reordered tab 1->0 in group g"
    /// then "Reordered tab 0->1 in group g" parse to (old=1,new=0) then (old=0,new=1);
    /// prev.Old(1)==newIndex(1) and prev.New(0)==oldIndex(0) → one flip-back pair, count=2.
    /// The same direction twice ("1->0","1->0") → count=2, flips=0. A single "1->0" →
    /// count=1, flips=0. All as expected.
    /// </summary>
    public static (int ReorderCount, int FlipBackPairs) AnalyzeReorders(long offset)
    {
        int count = 0;
        int flips = 0;
        (int Old, int New)? prev = null;
        foreach (string line in ReadNewLines(offset))
        {
            int idx = line.IndexOf("Reordered tab ", StringComparison.Ordinal);
            if (idx < 0)
                continue;
            if (!TryParseReorder(line, idx + "Reordered tab ".Length, out int oldIndex, out int newIndex))
                continue;
            count++;
            if (prev.HasValue && prev.Value.Old == newIndex && prev.Value.New == oldIndex)
                flips++;
            prev = (oldIndex, newIndex);
        }
        return (count, flips);
    }

    private static bool TryParseReorder(string line, int start, out int oldIndex, out int newIndex)
    {
        oldIndex = 0;
        newIndex = 0;
        int i = start;
        if (!TryReadInt(line, ref i, out oldIndex))
            return false;
        if (i + 1 >= line.Length || line[i] != '-' || line[i + 1] != '>')
            return false;
        i += 2;
        return TryReadInt(line, ref i, out newIndex);
    }

    private static bool TryReadInt(string line, ref int i, out int value)
    {
        value = 0;
        int begin = i;
        while (i < line.Length && (uint)(line[i] - '0') <= 9u)
        {
            value = value * 10 + (line[i] - '0');
            i++;
        }
        return i > begin;
    }
}
