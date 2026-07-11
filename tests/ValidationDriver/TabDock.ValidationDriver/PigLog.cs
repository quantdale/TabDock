using System;
using System.IO;
using System.Text;

namespace TabDock.ValidationDriver;

/// <summary>
/// Reads the guinea-pig window-message logs written to
/// %TEMP%\TabDock-Validation\pig-&lt;pid&gt;.log by TabDock.GuineaPig.
/// </summary>
internal static class PigLog
{
    public static string PigLogPath(uint pid)
    {
        return Path.Combine(Path.GetTempPath(), "TabDock-Validation", $"pig-{pid}.log");
    }

    public static string[] ReadLines(uint pid)
    {
        try
        {
            using var fs = new FileStream(PigLogPath(pid), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            string content = sr.ReadToEnd();
            return content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static bool ContainsLine(uint pid, string substring)
    {
        foreach (string line in ReadLines(pid))
        {
            if (line.IndexOf(substring, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    public static bool WaitForPigLine(uint pid, string substring, int timeoutMs)
    {
        return Util.WaitUntil(() => ContainsLine(pid, substring), timeoutMs, 150);
    }
}
