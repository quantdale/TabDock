using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former DiagnosticPrivacySelfTest (Wave 4): the
/// support-bundle privacy boundary — adversarial path/secret redaction, the
/// log-tail retention filter, JSON sanitization that stays parseable,
/// pending-recovery report redaction, and a real exported support ZIP.
/// Credential-keyword forms, bearer tokens, URL credentials, the title marker,
/// and username word/token handling are already covered by
/// DiagnosticsSanitizationTests and are not duplicated here.
/// </summary>
public class SupportBundlePrivacyTests
{
    [Fact]
    public void SanitizeText_AdversarialPathsAndSecrets_AreRedacted()
    {
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

        Assert.DoesNotContain(profile, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(profileSlash, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(appData, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(appDataSlash, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(localAppData, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(username, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET-TOKEN", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer-secret", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeLogTail_RetainsExceptionEvidenceAndTags_DropsUntaggedNoise()
    {
        const string rawTail = "2026-08-13T00:00:00Z [NOISE] untagged chatter\r\n"
            + "2026-08-13T00:00:01Z EXCEPTION in ContainerWindow: System.InvalidOperationException: boom\r\n"
            + "   at TabDock.ContainerWindow.OnSourceInitialized(Object sender, EventArgs e)\r\n"
            + "2026-08-13T00:00:02Z [BUILD[1]] retained-tag-line\r\n";

        string sanitizedTail = DiagnosticEnvironmentService.SanitizeLogTail(rawTail);

        Assert.Contains("EXCEPTION", sanitizedTail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at TabDock.ContainerWindow", sanitizedTail, StringComparison.Ordinal);
        Assert.Contains("retained-tag-line", sanitizedTail, StringComparison.Ordinal);
        Assert.DoesNotContain("untagged chatter", sanitizedTail, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeJsonText_OutputStaysParseableAndIsSanitized()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string username = Environment.UserName;
        string json = JsonSerializer.Serialize(new
        {
            path = profile,
            executable = Path.Combine(profile, "AppData", "Local", "TabDock.exe"),
            token = "SECRET-TOKEN",
            api_key = "API-KEY-SECRET",
        });

        string sanitizedJson = DiagnosticEnvironmentService.SanitizeJsonText(json);

        // The sanitizer must not break JSON structure (e.g. by breaking quotes).
        using var _ = JsonDocument.Parse(sanitizedJson);
        Assert.DoesNotContain(profile, sanitizedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(profile.Replace('\\', '/'), sanitizedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(appData, sanitizedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(username, sanitizedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET-TOKEN", sanitizedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("API-KEY-SECRET", sanitizedJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PendingRecoveryDiscoveryReport_RedactsPersonalPathsAndTitles()
    {
        string pendingRoot = Path.Combine(Path.GetTempPath(), "TabDock-pending-privacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pendingRoot);
        try
        {
            File.WriteAllText(
                Path.Combine(pendingRoot, "hidden-windows.json.pending"),
                "{\"Entries\":[{\"Hwnd\":1,\"Pid\":2,\"ExePath\":\"C:\\\\Users\\\\private\\\\guest.exe\"}]}");

            string pendingReport = PendingRecoveryService.FormatDiscovery(pendingRoot);

            Assert.DoesNotContain(pendingRoot, pendingReport, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private", pendingReport, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("guest.exe", pendingReport, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("window title", pendingReport, StringComparison.OrdinalIgnoreCase);
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
    }

    [Fact]
    public void ExportedSupportBundle_ContainsNoPersonalPathsOrPlantedSecrets()
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-privacy-test-" + Guid.NewGuid().ToString("N"));
        string bundlePath = Path.Combine(root, "support.zip");
        Directory.CreateDirectory(root);
        try
        {
            string exported = DiagnosticReportService.ExportBundle(bundlePath);
            Assert.True(File.Exists(exported));

            using ZipArchive archive = ZipFile.OpenRead(exported);
            Assert.True(archive.Entries.Count >= 9, "the bundle must contain its full documented entry set");

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string username = Environment.UserName;
            string profileSlash = profile.Replace('\\', '/');
            string appDataSlash = appData.Replace('\\', '/');

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using StreamReader reader = new(entry.Open());
                string content = reader.ReadToEnd();
                Assert.DoesNotContain(profile, content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(profileSlash, content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(appData, content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(appDataSlash, content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(localAppData, content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(username, content, StringComparison.OrdinalIgnoreCase);
                // Planted values that only this test could have introduced: any
                // appearance means foreign process state leaked into the bundle.
                Assert.DoesNotContain("SECRET-TOKEN", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("super-secret", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("bearer-secret", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("C:\\Users\\private\\guest.exe", content, StringComparison.OrdinalIgnoreCase);
            }
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
    }
}
