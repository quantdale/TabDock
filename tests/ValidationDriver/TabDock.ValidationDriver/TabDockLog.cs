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
}
