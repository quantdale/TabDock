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
        void Check(bool condition)
        {
            checks++;
            if (!condition) failures++;
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
        Check(!Contains(sanitized, profile, profileSlash, appData, appDataSlash, localAppData, username));
        Check(!Contains(sanitized, "SECRET-TOKEN", "super-secret", "bearer-secret"));

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
        Check(validJson);
        Check(!Contains(sanitizedJson, profile, profileSlash, appData, appDataSlash, username, "SECRET-TOKEN", "API-KEY-SECRET"));

        string root = Path.Combine(Path.GetTempPath(), "TabDock-privacy-selftest-" + Guid.NewGuid().ToString("N"));
        string bundlePath = Path.Combine(root, "support.zip");
        Directory.CreateDirectory(root);
        try
        {
            string exported = DiagnosticReportService.ExportBundle(bundlePath);
            Check(File.Exists(exported));
            using ZipArchive archive = ZipFile.OpenRead(exported);
            Check(archive.Entries.Count >= 9);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using StreamReader reader = new(entry.Open());
                string content = reader.ReadToEnd();
                Check(!Contains(content, profile, profileSlash, appData, appDataSlash, localAppData, username));
                Check(!Contains(content, "SECRET-TOKEN", "super-secret", "bearer-secret"));
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
