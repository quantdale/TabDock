using System;
using Xunit;
using TabDock.Services;

namespace TabDock.UnitTests;

/// <summary>
/// Support-bundle privacy boundary guards (R21-019): the export sanitizer must
/// redact credential values in underscore/hyphen keyword forms, credential
/// bearing URLs, and any window title carried by the documented
/// title&lt;'...'&gt; marker — while leaving ordinary words that merely contain
/// the username untouched. All checks run against the public SanitizeText
/// surface and are fully hermetic (no file system access).
/// </summary>
public class DiagnosticsSanitizationTests
{
    [Theory]
    [InlineData("client_secret=abc123def")]
    [InlineData("my_api_key=xyz789")]
    [InlineData("client-secret=zzz")]
    [InlineData("api-key=kkk")]
    [InlineData("pwd=hunter2")]
    [InlineData("passwd=p@ss")]
    [InlineData("credential=topsecret")]
    public void SanitizeText_CredentialKeywordForms_AreRedacted(string line)
    {
        string sanitized = DiagnosticEnvironmentService.SanitizeText(line);
        Assert.DoesNotContain(line[(line.IndexOf('=') + 1)..], sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted>", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeText_BearerTokenAfterAuthorization_IsRedacted()
    {
        string sanitized = DiagnosticEnvironmentService.SanitizeText("Authorization: Bearer sk-live-1234567890");
        Assert.DoesNotContain("sk-live-1234567890", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeText_UrlCredentials_AreRedactedButHostSurvives()
    {
        string sanitized = DiagnosticEnvironmentService.SanitizeText("feed=https://alice:s3cret-pw@example.com/updates");
        Assert.DoesNotContain("alice", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-pw", sanitized, StringComparison.Ordinal);
        Assert.Contains("example.com", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeText_TitleMarkerContent_IsRedactedStructurally()
    {
        string title = "Q4 Budget - salary spreadsheet FINAL.xlsx";
        string marked = DiagnosticEnvironmentService.FormatTitleMarker(title);
        string sanitized = DiagnosticEnvironmentService.SanitizeText("[STARTUP] container ready " + marked + " hwnd=0x1");
        Assert.DoesNotContain(title, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted-title>", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeText_UsernameSubstringWords_SurviveWholeTokenReplacement()
    {
        string username = Environment.UserName;
        if (string.IsNullOrWhiteSpace(username))
            return; // environment without a usable username; nothing to assert

        string text = $"the {username}core build passed";
        string sanitized = DiagnosticEnvironmentService.SanitizeText(text);
        Assert.Contains(username + "core", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeText_PathLikeUsernameToken_IsStillReplaced()
    {
        string username = Environment.UserName;
        if (string.IsNullOrWhiteSpace(username))
            return;

        string sanitized = DiagnosticEnvironmentService.SanitizeText($"profile={username}\\documents");
        Assert.DoesNotContain("=" + username + "\\", sanitized, StringComparison.Ordinal);
    }
}
