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

    /// <summary>All new lines since the offset as one dumpable block (empty string when none).</summary>
    public static string DumpNewLines(long offset)
    {
        return string.Join(Environment.NewLine, ReadNewLines(offset));
    }

    /// <summary>
    /// Checks whether any real (non-"skipped"/"giving up") LAYOUT[drift] correction
    /// fired for a guest with no LAYOUT[movesize] line for that same guest HWND in
    /// the preceding <paramref name="windowMs"/> milliseconds. See
    /// docs/internal/TEST_PLAN.md section 5.5: this is the decider for whether the
    /// 1s drift watchdog is doing real work (catching programmatic self-moves the
    /// MOVESIZEEND hook cannot see) or is redundant with it. Returns the offending
    /// line(s) so a caller can log concrete evidence either way.
    /// </summary>
    public static List<string> FindDriftWithoutPrecedingMovesize(long offset, int windowMs = 2000)
    {
        var offenders = new List<string>();
        string[] lines = ReadNewLines(offset);

        var movesizeTimesByGuest = new Dictionary<string, List<DateTime>>();
        var parsed = new List<(DateTime Time, string Line)>();
        foreach (string line in lines)
        {
            DateTime? t = ParseTimestamp(line);
            if (t == null)
                continue;
            parsed.Add((t.Value, line));
            if (line.Contains("LAYOUT[movesize]", StringComparison.Ordinal))
            {
                string? guest = ExtractGuestToken(line);
                if (guest != null)
                {
                    if (!movesizeTimesByGuest.TryGetValue(guest, out var list))
                        movesizeTimesByGuest[guest] = list = new List<DateTime>();
                    list.Add(t.Value);
                }
            }
        }

        foreach (var (time, line) in parsed)
        {
            if (!line.Contains("LAYOUT[drift]", StringComparison.Ordinal))
                continue;
            if (line.Contains("skipped", StringComparison.OrdinalIgnoreCase) || line.Contains("giving up", StringComparison.OrdinalIgnoreCase))
                continue; // Not a real correction — no guest was actually repositioned.

            string? guest = ExtractGuestToken(line);
            bool hasRecentMovesize = guest != null
                && movesizeTimesByGuest.TryGetValue(guest, out var times)
                && times.Exists(mt => mt <= time && (time - mt).TotalMilliseconds <= windowMs);

            if (!hasRecentMovesize)
                offenders.Add(line);
        }
        return offenders;
    }

    /// <summary>Extracts the "guest=0x...." token (hwnd only, ignoring the rest of the descriptor) for correlation.</summary>
    private static string? ExtractGuestToken(string line)
    {
        int idx = line.IndexOf("guest=0x", StringComparison.Ordinal);
        if (idx < 0)
            return null;
        int start = idx + "guest=0x".Length;
        int end = start;
        while (end < line.Length && Uri.IsHexDigit(line[end]))
            end++;
        return end > start ? line.Substring(start, end - start) : null;
    }

    private static DateTime? ParseTimestamp(string line)
    {
        // Lines are "[yyyy-MM-dd HH:mm:ss.fff] message...".
        if (line.Length < 25 || line[0] != '[')
            return null;
        int close = line.IndexOf(']');
        if (close < 0)
            return null;
        string stamp = line.Substring(1, close - 1);
        return DateTime.TryParse(stamp, out DateTime dt) ? dt : (DateTime?)null;
    }
}
