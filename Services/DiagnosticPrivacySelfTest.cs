using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace TabDock.Services;

/// <summary>Adversarial privacy fixtures, including a real exported support ZIP.</summary>
internal static class DiagnosticPrivacySelfTest
{
    public static (int Checks, int Failures) Run()
    {
        int checks = 0;
        int failures = 0;
        void Check(bool condition, string label)
        {
            checks++;
            if (!condition)
            {
                failures++;
                try { Console.Error.WriteLine($"PRIVACY-FAIL: {label}"); } catch { }
            }
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string username = Environment.UserName;
        string profileSlash = profile.Replace('\\', '/');
        string appDataSlash = appData.Replace('\\', '/');
        string adversarial = $"2026-08-13T00:00:00Z [SHEPHERD] path={profile}\\AppData\\Roaming\\TabDock\\bin\\guest.exe "
            + $"quoted=\"{profileSlash}/AppData/Roaming/TabDock/state.json\" "
            + $"json={{\"path\":\"{appDataSlash}/TabDock/logs/TabDock.log\",\"token\":\"SECRET-TOKEN\"}} "
            + "password=super-secret Bearer bearer-secret";
        string sanitized = DiagnosticEnvironmentService.SanitizeText(adversarial);
        Check(!Contains(sanitized, profile, profileSlash, appData, appDataSlash, localAppData, username), "sanitized profile/username");
        Check(!Contains(sanitized, "SECRET-TOKEN", "super-secret", "bearer-secret"), "sanitized secrets");

        // Structural title redaction: an arbitrary planted window title carried
        // by the documented title<'...'> marker must be redacted wherever it
        // appears, independent of any allow/deny list.
        string fakeTitle = "Q4 Budget - salary spreadsheet FINAL.xlsx";
        string markedLine = "[STARTUP] container ready title<'" + fakeTitle + "'> hwnd=0x1";
        string sanitizedMarked = DiagnosticEnvironmentService.SanitizeText(markedLine);
        Check(!sanitizedMarked.Contains(fakeTitle, StringComparison.OrdinalIgnoreCase), "title redacted");
        Check(sanitizedMarked.Contains("<redacted-title>", StringComparison.Ordinal), "title marker present");

        // Expanded credential coverage: underscore/hyphen keyword forms and
        // credential-bearing URLs must be redacted too.
        string secretFixture = "client_secret=abc123def my_api_key=xyz789 "
            + "client-secret=zzz pwd=hunter2 Authorization: Bearer sk-123 "
            + "https://alice:s3cret@example.com/path";
        string sanitizedSecrets = DiagnosticEnvironmentService.SanitizeText(secretFixture);
        Check(!Contains(sanitizedSecrets, "abc123def", "xyz789", "zzz", "hunter2", "sk-123", "s3cret"), "expanded secrets");
        Check(sanitizedSecrets.Contains("example.com", StringComparison.Ordinal), "url host survives");

        // Ordinary words that merely contain a username substring survive;
        // whole-token occurrences (path segments) are still replaced.
        if (!string.IsNullOrWhiteSpace(username))
        {
            string wordFixture = $"the {username}core build passed; profile={username}\\docs";
            string sanitizedWords = DiagnosticEnvironmentService.SanitizeText(wordFixture);
            Check(sanitizedWords.Contains(username + "core", StringComparison.Ordinal), "username word survives");
            Check(!Contains(sanitizedWords, "=" + username + "\\"), "username token");
        }

        // Exception evidence survives the log-tail filter even without any
        // retained tag on the line.
        string rawTail = "2026-08-13T00:00:00Z [NOISE] untagged chatter\r\n"
            + "2026-08-13T00:00:01Z EXCEPTION in ContainerWindow: System.InvalidOperationException: boom\r\n"
            + "   at TabDock.ContainerWindow.OnSourceInitialized(Object sender, EventArgs e)\r\n"
            + "2026-08-13T00:00:02Z [BUILD[1]] retained-tag-line\r\n";
        string sanitizedTail = DiagnosticEnvironmentService.SanitizeLogTail(rawTail);
        Check(sanitizedTail.Contains("EXCEPTION", StringComparison.OrdinalIgnoreCase), "exception retained");
        Check(sanitizedTail.Contains("at TabDock.ContainerWindow", StringComparison.Ordinal), "stack retained");
        Check(sanitizedTail.Contains("retained-tag-line", StringComparison.Ordinal), "tag retained");
        Check(!sanitizedTail.Contains("untagged chatter", StringComparison.Ordinal), "noise dropped");

        string json = JsonSerializer.Serialize(new
        {
            path = profile,
            executable = Path.Combine(profile, "AppData", "Local", "TabDock.exe"),
            token = "SECRET-TOKEN",
            api_key = "API-KEY-SECRET",
        });
        string sanitizedJson = DiagnosticEnvironmentService.SanitizeJsonText(json);
        bool validJson = true;
        try { using JsonDocument _ = JsonDocument.Parse(sanitizedJson); }
        catch (JsonException) { validJson = false; }
        Check(validJson, "json valid");
        Check(!Contains(sanitizedJson, profile, profileSlash, appData, appDataSlash, username, "SECRET-TOKEN", "API-KEY-SECRET"), "json sanitized");

        string pendingRoot = Path.Combine(Path.GetTempPath(), "TabDock-pending-privacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pendingRoot);
        try
        {
            File.WriteAllText(
                Path.Combine(pendingRoot, "hidden-windows.json.pending"),
                "{\"Entries\":[{\"Hwnd\":1,\"Pid\":2,\"ExePath\":\"C:\\\\Users\\\\private\\\\guest.exe\"}]}" );
            string pendingReport = PendingRecoveryService.FormatDiscovery(pendingRoot);
            Check(!Contains(pendingReport, pendingRoot, "private", "guest.exe", "window title"), "pending report");
        }
        finally
        {
            try
            {
                if (Directory.Exists(pendingRoot))
                    Directory.Delete(pendingRoot, recursive: true);
            }
            catch { }
        }

        string root = Path.Combine(Path.GetTempPath(), "TabDock-privacy-selftest-" + Guid.NewGuid().ToString("N"));
        string bundlePath = Path.Combine(root, "support.zip");
        Directory.CreateDirectory(root);
        try
        {
            string exported = DiagnosticReportService.ExportBundle(bundlePath);
            Check(File.Exists(exported), "bundle exists");
            using ZipArchive archive = ZipFile.OpenRead(exported);
            Check(archive.Entries.Count >= 9, "entry count");
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using StreamReader reader = new(entry.Open());
                string content = reader.ReadToEnd();
                Check(!Contains(content, profile, profileSlash, appData, appDataSlash, localAppData, username), "entry profile");
                Check(!Contains(content, "SECRET-TOKEN", "super-secret", "bearer-secret"), "entry secrets");
                Check(!Contains(content, "C:\\Users\\private\\guest.exe", "hidden-windows.json.pending"), "entry pending paths");
            }
        }
        catch
        {
            checks++;
            failures++;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { }
        }

        return (checks, failures);
    }

    private static bool Contains(string value, params string[] needles)
        => needles.Where(needle => !string.IsNullOrWhiteSpace(needle))
            .Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
