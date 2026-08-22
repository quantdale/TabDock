using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former DiagnosticSelfTest aggregator (Wave 4): a Windows
/// 11 build must never be mislabeled solely because the registry ProductName
/// says Windows 10, and a real Windows 10 build must remain identifiable as
/// Windows 10. The raw registry evidence stays available for forensics.
/// </summary>
public class DiagnosticEnvironmentProductNamingTests
{
    [Theory]
    [InlineData("Windows 10 Pro", "19045", "Windows 10 Pro")]
    [InlineData("Windows 10 Pro", "22631", "Windows 11 Pro")]
    [InlineData("Windows 11 Pro", "22631", "Windows 11 Pro")]
    [InlineData("Windows 10 Enterprise LTSC", "26100", "Windows 11 Enterprise LTSC")]
    // Builds below the Windows 11 boundary are never relabeled upward.
    [InlineData("Windows 10 Pro", "21996", "Windows 10 Pro")]
    public void NormalizeWindowsProductName_ReconcilesRegistryEvidenceWithBuild(string? productName, string build, string expected)
    {
        Assert.Equal(expected, DiagnosticEnvironmentService.NormalizeWindowsProductName(productName, build));
    }

    [Fact]
    public void NormalizeWindowsProductName_MissingProductNameIsDisclosed()
    {
        Assert.Equal(
            "Windows 11 (build 22631; raw ProductName: unavailable)",
            DiagnosticEnvironmentService.NormalizeWindowsProductName(null, "22631"));
    }

    [Theory]
    [InlineData("22631", "Windows 10 Pro", "Windows 11")]
    [InlineData("19045", "Windows 10 Pro", "Windows 10 or earlier")]
    public void GetWindowsProductFamily_PrefersBuildEvidence(string? build, string productName, string expected)
    {
        Assert.Equal(expected, DiagnosticEnvironmentService.GetWindowsProductFamily(build, productName));
    }

    [Fact]
    public void GetWindowsProductFamily_MissingBuildFallsBackToRawProductName()
    {
        Assert.Equal("Windows 10 (raw registry evidence)", DiagnosticEnvironmentService.GetWindowsProductFamily(null, "Windows 10 Pro"));
    }

    [Fact]
    public void GetWindowsProductFamily_NoEvidenceAtAllIsUnavailable()
    {
        Assert.Equal("unavailable", DiagnosticEnvironmentService.GetWindowsProductFamily(null, null));
    }
}
