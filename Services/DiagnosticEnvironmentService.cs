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
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>Read-only environment and persistence probes used by doctor/export.</summary>
public static class DiagnosticEnvironmentService
{
    private static readonly Regex s_absolutePath = new(
        @"(?<![A-Za-z])(?:[A-Za-z]:[\\/]|\\\\)[^\r\n""'<>|]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex s_bearerValue = new(
        @"\bBearer\s+[^,\s}\]]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex s_obviousSecretToken = new(
        @"(?<![A-Za-z0-9_])(?:secret|token|api[-_]?key)[-][A-Za-z0-9_-]+(?![A-Za-z0-9_])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // Credential keywords use a trailing lookaround boundary instead of \b
    // because \b treats '_' as a word character and therefore never matches
    // before "client_secret" or after "my_api_key". No prefix boundary: an
    // underscore-prefixed form such as my_api_key must still match, and a
    // keyword directly followed by ':' or '=' is a credential assignment.
    private static readonly Regex s_secretValue = new(
        @"(?:password|passwd|pass|pwd|token|secret|client[-_]?secret|api[-_]?key|authorization|credential)(?![-_A-Za-z0-9])\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^,\s}\]]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex s_urlCredentials = new(
        @"([A-Za-z][A-Za-z0-9+.\-]*://)[^\s/:@]+:[^\s/@]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Explicit title-marker convention for log emission: window titles are
    /// wrapped as <c>title<'...'></c> so export sanitization can redact them
    /// structurally. Any line carrying the marker loses its title content
    /// during SanitizeText, independent of the log-tail allow/deny lists.
    /// </summary>
    public const string TitleMarkerPrefix = "title<'";
    public const string TitleMarkerSuffix = "'>";

    private static readonly Regex s_titleMarker = new(
        Regex.Escape(TitleMarkerPrefix) + "[^']*" + Regex.Escape(TitleMarkerSuffix),
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Wraps a window title in the documented redaction marker.</summary>
    public static string FormatTitleMarker(string? title)
        => TitleMarkerPrefix + (title ?? string.Empty).Replace("'", string.Empty) + TitleMarkerSuffix;

    // The raw username is replaced only as a whole token (path segments,
    // quoted values, standalone words). A plain substring replace corrupted
    // unrelated words that merely contained the username.
    private static Regex UsernameTokenRegex(string userName)
        => new("(?i)(?<![A-Za-z0-9_])" + Regex.Escape(userName) + "(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);

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
            result.ProductName = NormalizeWindowsProductName(result.RawProductName, result.Build);
            result.ProductFamily = GetWindowsProductFamily(result.Build, result.RawProductName);
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            result.RawProductName = "unavailable (registry-read-failed)";
            result.ProductName = "unavailable (registry-read-failed)";
            result.ProductFamily = "unavailable (registry-read-failed)";
        }

        if (NativeMethods.IsProcessElevated(
                NativeMethods.GetCurrentProcessId(), out bool elevated, out string? elevationError))
        {
            result.IsElevated = elevated;
            result.ElevationStatus = elevated ? "elevated" : "standard-user";
        }
        else
        {
            // The error detail comes straight from the probe overload; calling
            // FormatLastError here would read whatever the last P/Invoke left
            // behind, not the elevation probe's failure.
            result.ElevationStatus = "unavailable (" + (elevationError ?? "probe-failed") + ")";
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
                    cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>(),
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
        try
        {
            PendingRecoveryCatalog pending = PendingRecoveryService.Discover(directory);
            int pendingCount = pending.Files.Count(file => file.HasUnresolvedEvidence);
            result.PendingJournalFileCount = pendingCount;
            result.PendingJournalStatus = pending.Error
                ?? (pendingCount == 0 ? "absent" : "manual-recovery-pending");
        }
        catch (UnauthorizedAccessException)
        {
            result.PendingJournalStatus = "unreadable (access-denied)";
        }
        catch (IOException)
        {
            result.PendingJournalStatus = "unreadable (io-error)";
        }
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
            {
                stream.Seek(-maxCharacters, SeekOrigin.End);
                // Align to the next line boundary so the tail never begins with
                // the truncated half of a multi-byte UTF-8 sequence (U+FFFD).
                int byteRead = stream.ReadByte();
                while (byteRead != -1 && byteRead != '\n')
                    byteRead = stream.ReadByte();
            }
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
        return SanitizeLogTail(raw);
    }

    /// <summary>
    /// Pure line filter over raw log text so self-tests can exercise the
    /// allow/deny and retention rules without touching the real log file.
    /// </summary>
    internal static string SanitizeLogTail(string raw)
    {
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
            // Exception evidence always survives: a crash report without its
            // exception lines is useless to support. Stack-frame lines
            // ("   at Type.Method(...)") are retained alongside EXCEPTION tags.
            bool exceptionShaped = line.Contains("EXCEPTION", StringComparison.OrdinalIgnoreCase)
                || line.TrimStart().StartsWith("at ", StringComparison.Ordinal);
            if (!exceptionShaped
                && !line.Contains("BUILD[", StringComparison.OrdinalIgnoreCase)
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
            line = SanitizeText(line);
            line = Regex.Replace(line, "'[^']*'", "'<redacted>'");
            lines.Add(line);
        }
        return string.Join(Environment.NewLine, lines.TakeLast(1200));
    }

    /// <summary>
    /// Sanitizes a value that is known to be a path. Kept as an alias for
    /// SanitizeText for call-site readability; the general sanitizer already
    /// handles profile-root substitution, embedded absolute paths, and
    /// credential-like content, so no separate path-only rule exists.
    /// </summary>
    public static string RedactPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "unavailable";
        return SanitizeText(path);
    }

    /// <summary>
    /// Sanitizes arbitrary report text, not just a value known to be a path.
    /// Support data contains timestamped log lines, JSON-escaped strings, and
    /// diagnostic exception text, so prefix-only replacement is insufficient.
    /// Known profile roots are replaced in both Windows separator forms and a
    /// conservative absolute-path pass catches embedded executable/temp paths.
    /// Credential-like values are removed before any bundle entry is written.
    /// </summary>
    public static string SanitizeText(string? text)
    {
        if (text == null)
            return "unavailable";

        string result = text;
        // Structural title redaction: any documented title<'...'> marker loses
        // its content before any other pass, independent of allow/deny lists.
        result = s_titleMarker.Replace(result, TitleMarkerPrefix + "<redacted-title>" + TitleMarkerSuffix);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            result = ReplacePathVariants(result, appData, "%APPDATA%");
        if (!string.IsNullOrWhiteSpace(userProfile))
            result = ReplacePathVariants(result, userProfile, "%USERPROFILE%");

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            result = ReplacePathVariants(result, localAppData, "%LOCALAPPDATA%");

        string userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
            result = UsernameTokenRegex(userName).Replace(result, "<user>");

        string domainUser = Environment.UserDomainName + "\\" + Environment.UserName;
        if (!string.IsNullOrWhiteSpace(Environment.UserDomainName)
            && !string.IsNullOrWhiteSpace(Environment.UserName))
        {
            result = ReplacePathVariants(result, domainUser, "<user>");
        }

        result = s_absolutePath.Replace(result, "<path>");
        result = s_urlCredentials.Replace(result, "$1<redacted>@");
        // Bearer values are removed before the keyword pass so an
        // "Authorization: Bearer <token>" pair cannot leak the token as the
        // keyword rule's captured value.
        result = s_bearerValue.Replace(result, "Bearer <redacted>");
        result = s_secretValue.Replace(result, match =>
        {
            int separator = match.Value.IndexOfAny(new[] { ':', '=' });
            return separator >= 0 ? match.Value[..(separator + 1)] + "<redacted>" : "<redacted>";
        });
        result = s_obviousSecretToken.Replace(result, "<redacted>");
        return result;
    }

    /// <summary>Sanitizes JSON values while preserving a parseable JSON document.</summary>
    internal static string SanitizeJsonText(string json)
    {
        try
        {
            JsonNode? root = JsonNode.Parse(json);
            if (root == null)
                return SanitizeText(json);
            root = SanitizeJsonNode(root)!;
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return SanitizeText(json);
        }
    }

    /// <summary>
    /// Sanitizes one JSONL record and returns it as a single compact line.
    /// The pretty-printing overload above would break the JSONL framing by
    /// re-indenting each record across multiple lines.
    /// </summary>
    internal static string SanitizeJsonLine(string json)
    {
        try
        {
            JsonNode? root = JsonNode.Parse(json);
            if (root == null)
                return SanitizeText(json);
            root = SanitizeJsonNode(root)!;
            return root.ToJsonString();
        }
        catch (JsonException)
        {
            return SanitizeText(json);
        }
    }

    private static JsonNode? SanitizeJsonNode(JsonNode? node)
    {
        if (node == null)
            return null;
        if (node is JsonObject obj)
        {
            var sanitized = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> property in obj.ToList())
            {
                if (IsSensitiveKey(property.Key))
                {
                    sanitized[property.Key] = "<redacted>";
                }
                else
                {
                    sanitized[property.Key] = SanitizeJsonNode(property.Value);
                }
            }
            return sanitized;
        }
        if (node is JsonArray array)
        {
            var sanitized = new JsonArray();
            for (int i = 0; i < array.Count; i++)
            {
                sanitized.Add(SanitizeJsonNode(array[i]));
            }
            return sanitized;
        }
        if (node is JsonValue value && value.TryGetValue<string>(out string? text) && text != null)
        {
            return JsonValue.Create(SanitizeText(text));
        }
        return node.DeepClone();
    }

    private static bool IsSensitiveKey(string key)
    {
        string normalized = new(key.Where(char.IsLetterOrDigit).ToArray());
        return normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("passwd", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("token", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("apikey", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("authorization", StringComparison.OrdinalIgnoreCase);
    }

    public static string HashTitle(string? title)
    {
        string value = title ?? string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
    }

    public static string FormatHwnd(IntPtr hwnd)
        => hwnd == IntPtr.Zero ? "0x0" : $"0x{hwnd.ToInt64():X}";

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

    /// <summary>
    /// Windows 11 starts at build 22000. Some Windows 11 installations keep a
    /// registry ProductName beginning with "Windows 10" (for example Windows 11
    /// Enterprise LTSC), so the raw value is preserved separately in
    /// RawProductName while this value supplies an accurate display label.
    /// A real Windows 10 build is never relabeled because the build number is
    /// the deciding evidence.
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

    /// <summary>
    /// Build-inferred family label. The build number is reliable evidence even
    /// when registry branding is stale; the raw registry value is the fallback
    /// when no build could be parsed.
    /// </summary>
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
        buildNumber = 0;
        if (string.IsNullOrWhiteSpace(build))
            return false;
        // CurrentBuild is a plain decimal like "22631" on supported targets.
        return int.TryParse(build.Trim(), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out buildNumber);
    }

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

    private static string ReplacePathVariants(string value, string path, string replacement)
    {
        string slash = path.Replace('\\', '/');
        string backslash = path.Replace('/', '\\');
        string result = value.Replace(path, replacement, StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(slash, path, StringComparison.Ordinal))
            result = result.Replace(slash, replacement, StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(backslash, path, StringComparison.Ordinal))
            result = result.Replace(backslash, replacement, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static string Classify(Exception ex)
        => ex switch
        {
            UnauthorizedAccessException => "access-denied",
            IOException => "io-error",
            _ => "probe-failed",
        };
}
