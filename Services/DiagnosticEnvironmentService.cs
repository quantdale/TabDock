using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>Read-only environment and persistence probes used by doctor/export.</summary>
public static class DiagnosticEnvironmentService
{
    public static WindowsEnvironmentSnapshot CaptureWindows()
    {
        var result = new WindowsEnvironmentSnapshot
        {
            OsVersion = Environment.OSVersion.VersionString,
            Runtime = RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
        };

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            result.RawProductName = ReadRegistryString(key, "ProductName");
            result.DisplayVersion = ReadRegistryString(key, "DisplayVersion");
            result.Build = ReadRegistryString(key, "CurrentBuild");
            result.Revision = ReadRegistryString(key, "UBR");
            result.ProductFamily = GetWindowsProductFamily(result.Build, result.RawProductName);
            result.ProductName = NormalizeWindowsProductName(result.RawProductName, result.Build);
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            result.RawProductName = "unavailable (registry-read-failed)";
            result.ProductName = result.RawProductName;
            result.ProductFamily = "unavailable (registry-read-failed)";
        }

        if (NativeMethods.IsCurrentProcessElevated(out bool elevated))
        {
            result.IsElevated = elevated;
            result.ElevationStatus = elevated ? "elevated" : "standard-user";
        }
        else
        {
            result.ElevationStatus = "unavailable (" + NativeMethods.FormatLastError() + ")";
        }

        try
        {
            result.SessionId = Process.GetCurrentProcess().SessionId;
        }
        catch
        {
            result.SessionId = -1;
        }
        return result;
    }

    public static List<MonitorSnapshot> CaptureMonitors()
        => EnvironmentFingerprint.CaptureMonitors();

    public static List<DisplayAdapterSnapshot> CaptureDisplayAdapters()
    {
        var adapters = new List<DisplayAdapterSnapshot>();
        try
        {
            for (uint i = 0; i < 32; i++)
            {
                var device = new NativeMethods.DISPLAY_DEVICE
                {
                    cb = (uint)Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>(),
                };
                if (!NativeMethods.EnumDisplayDevices(null, i, ref device, 0))
                    break;
                adapters.Add(new DisplayAdapterSnapshot
                {
                    Index = (int)i,
                    Name = EmptyAsUnavailable(device.DeviceName),
                    Description = EmptyAsUnavailable(device.DeviceString),
                    DeviceId = EmptyAsUnavailable(device.DeviceId),
                    DriverVersion = ReadDisplayDriverVersion(device.DeviceKey),
                });
            }
        }
        catch (Exception ex)
        {
            adapters.Add(new DisplayAdapterSnapshot
            {
                Index = 0,
                Status = $"unavailable ({Classify(ex)})",
            });
        }
        if (adapters.Count == 0)
        {
            adapters.Add(new DisplayAdapterSnapshot
            {
                Index = 0,
                Status = "unavailable (no display adapters returned)",
            });
        }
        return adapters;
    }

    public static PersistenceSnapshot InspectPersistence()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string directory = Path.Combine(appData, "TabDock");
        string statePath = Path.Combine(directory, "state.json");
        string journalPath = Path.Combine(directory, "hidden-windows.json");
        string logPath = Path.Combine(directory, "logs", "TabDock.log");
        var result = new PersistenceSnapshot
        {
            LogExists = File.Exists(logPath),
        };

        InspectJsonFile(statePath, isState: true, result);
        InspectJsonFile(journalPath, isState: false, result);
        return result;
    }

    public static string ReadRecentLogText(int maxCharacters = 200_000)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabDock", "logs", "TabDock.log");
        try
        {
            if (!File.Exists(path))
                return "unavailable (log-absent)";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > maxCharacters)
                stream.Seek(-maxCharacters, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string text = reader.ReadToEnd();
            return stream.Position > maxCharacters ? "[tail truncated]\r\n" + text : text;
        }
        catch (UnauthorizedAccessException)
        {
            return "unavailable (access-denied)";
        }
        catch (IOException)
        {
            return "unavailable (io-error)";
        }
    }

    /// <summary>
    /// Keeps only support-relevant tagged log lines and removes quoted values,
    /// user-profile paths, and known title/name-bearing records. The raw log is
    /// never placed in a support bundle.
    /// </summary>
    public static string ReadSanitizedRecentLogText(int maxCharacters = 200_000)
    {
        string raw = ReadRecentLogText(maxCharacters);
        if (raw.StartsWith("unavailable", StringComparison.OrdinalIgnoreCase))
            return raw;

        var lines = new List<string>();
        foreach (string rawLine in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine;
            if (line.Contains("Created group", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Shepherd-captured", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Shepherd-released", StringComparison.OrdinalIgnoreCase)
                || line.Contains("title changed", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Quarantined corrupt", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!line.Contains("BUILD[", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("ENV[", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("STATE[", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("LAYOUT[", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("SHEPHERD[", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("WinEvent", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("EVENT_", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("STARTUP[", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("SPLIT[", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("CHROME[", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("DIAGNOSTICS[", StringComparison.OrdinalIgnoreCase))
                continue;
            line = RedactPath(line);
            line = Regex.Replace(line, "'[^']*'", "'<redacted>'");
            lines.Add(line);
        }
        return string.Join(Environment.NewLine, lines.TakeLast(1200));
    }

    public static string RedactPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "unavailable";

        string result = path;
        var sensitivePaths = new List<(string Prefix, string Replacement)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
        };

        try
        {
            sensitivePaths.Add((Path.GetTempPath(), "%TEMP%"));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            // A diagnostic export must remain best-effort if the temp-path
            // provider is unavailable. The profile/AppData entries still apply.
        }

        // Replace the most-specific path first. AppData and LocalAppData are
        // normally nested below UserProfile; sorting prevents a path such as
        // C:\Users\Alice\AppData\Roaming\... from becoming the less useful
        // %USERPROFILE%\AppData\Roaming\... form. Regex provides a
        // case-insensitive replacement anywhere in a retained log line, not
        // merely when the path starts at character zero after a timestamp/tag.
        foreach ((string prefix, string replacement) in sensitivePaths
            .Where(item => !string.IsNullOrWhiteSpace(item.Prefix))
            .OrderByDescending(item => item.Prefix.Length)
            .DistinctBy(item => item.Prefix, StringComparer.OrdinalIgnoreCase))
        {
            result = Regex.Replace(
                result,
                Regex.Escape(prefix),
                replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return result;
    }

    public static string HashTitle(string? title)
    {
        string value = title ?? string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
    }

    public static string FormatHwnd(IntPtr hwnd)
        => hwnd == IntPtr.Zero ? "0x0" : $"0x{hwnd.ToInt64():X}";

    /// <summary>
    /// Windows 11 starts at build 22000. Some Windows 11 installations retain
    /// a registry ProductName beginning with "Windows 10", so the raw value is
    /// preserved separately while this value supplies an accurate display label.
    /// </summary>
    internal static string NormalizeWindowsProductName(string? rawProductName, string? build)
    {
        string raw = string.IsNullOrWhiteSpace(rawProductName) ? "unavailable" : rawProductName.Trim();
        if (!TryParseBuild(build, out int buildNumber) || buildNumber < 22000)
            return raw;

        int windows10Index = raw.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase);
        if (windows10Index >= 0)
        {
            return raw[..windows10Index] + "Windows 11" + raw[(windows10Index + "Windows 10".Length)..];
        }

        return raw.Contains("Windows 11", StringComparison.OrdinalIgnoreCase)
            ? raw
            : $"Windows 11 (build {buildNumber}; raw ProductName: {raw})";
    }

    internal static string GetWindowsProductFamily(string? build, string? rawProductName)
    {
        if (TryParseBuild(build, out int buildNumber))
            return buildNumber >= 22000 ? "Windows 11" : "Windows 10 or earlier";
        if (!string.IsNullOrWhiteSpace(rawProductName)
            && rawProductName.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
            return "Windows 11 (raw registry evidence)";
        if (!string.IsNullOrWhiteSpace(rawProductName)
            && rawProductName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
            return "Windows 10 (raw registry evidence)";
        return "unavailable";
    }

    private static bool TryParseBuild(string? build, out int buildNumber)
    {
        string firstPart = build?.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return int.TryParse(firstPart, out buildNumber);
    }

    internal static string ClassifyJsonText(string json, bool isState)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return "corrupt (root-not-object)";
            string property = isState ? "Groups" : "Entries";
            return root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Array
                ? "valid"
                : "corrupt (required-array-missing)";
        }
        catch (JsonException)
        {
            return "corrupt (malformed-json)";
        }
    }

    private static void InspectJsonFile(string path, bool isState, PersistenceSnapshot result)
    {
        string status = isState ? result.StateStatus : result.JournalStatus;
        if (!File.Exists(path))
        {
            if (isState) result.StateStatus = "absent";
            else result.JournalStatus = "absent";
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("root is not an object");

            if (isState)
            {
                if (!root.TryGetProperty("Groups", out JsonElement groups) || groups.ValueKind != JsonValueKind.Array)
                    throw new JsonException("Groups array is missing");
                result.SchemaVersion = root.TryGetProperty("Version", out JsonElement version) && version.TryGetInt32(out int value)
                    ? value : null;
                result.GroupCount = groups.GetArrayLength();
                foreach (JsonElement group in groups.EnumerateArray())
                {
                    if (group.ValueKind == JsonValueKind.Object
                        && group.TryGetProperty("Tabs", out JsonElement tabs)
                        && tabs.ValueKind == JsonValueKind.Array)
                        result.PersistedMemberMetadataCount += tabs.GetArrayLength();
                }
                result.StateStatus = "valid";
            }
            else
            {
                if (!root.TryGetProperty("Entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
                    throw new JsonException("Entries array is missing");
                result.JournalEntryCount = entries.GetArrayLength();
                result.JournalStatus = "valid";
            }
        }
        catch (JsonException)
        {
            if (isState) result.StateStatus = "corrupt (malformed-json)";
            else result.JournalStatus = "corrupt (malformed-json)";
        }
        catch (UnauthorizedAccessException)
        {
            if (isState) result.StateStatus = "unreadable (access-denied)";
            else result.JournalStatus = "unreadable (access-denied)";
        }
        catch (IOException)
        {
            if (isState) result.StateStatus = "unreadable (io-error)";
            else result.JournalStatus = "unreadable (io-error)";
        }
    }

    private static string ReadRegistryString(RegistryKey? key, string name)
        => key?.GetValue(name)?.ToString() ?? "unavailable";

    private static string ReadDisplayDriverVersion(string? deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            return "unavailable (registry-key-not-returned)";
        const string machinePrefix = @"\Registry\Machine\";
        if (!deviceKey.StartsWith(machinePrefix, StringComparison.OrdinalIgnoreCase))
            return "unavailable (registry-key-unrecognized)";
        try
        {
            string subKey = deviceKey[machinePrefix.Length..];
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(subKey);
            string? version = key?.GetValue("DriverVersion")?.ToString();
            return string.IsNullOrWhiteSpace(version) ? "unavailable (registry-value-not-returned)" : version;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return "unavailable (registry-read-failed)";
        }
    }

    private static string EmptyAsUnavailable(string? value)
        => string.IsNullOrWhiteSpace(value) ? "unavailable" : value.Trim();

    private static string Classify(Exception ex)
        => ex switch
        {
            UnauthorizedAccessException => "access-denied",
            IOException => "io-error",
            _ => "probe-failed",
        };
}
