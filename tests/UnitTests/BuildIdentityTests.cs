using System;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former DiagnosticSelfTest aggregator (Wave 4): pure
/// BuildIdentity parsing contracts used by --version and doctor reporting.
/// </summary>
public class BuildIdentityTests
{
    [Theory]
    [InlineData("1.2.3+abcdef0123456789", "abcdef0123456789")]
    [InlineData("1.2.3", null)]
    public void ParseCommitHash_ExtractsTrailingCommitOrFails(string informational, string? expected)
    {
        Assert.Equal(expected, BuildIdentity.ParseCommitHash(informational));
    }

    [Fact]
    public void ParseSemanticVersion_PrefersInformationalVersion()
    {
        Assert.Equal("1.2.3", BuildIdentity.ParseSemanticVersion("1.2.3+abcdef", new Version(9, 9)));
    }

    [Fact]
    public void ParseSemanticVersion_FallsBackToAssemblyVersion()
    {
        Assert.Equal("9.9", BuildIdentity.ParseSemanticVersion(null, new Version(9, 9)));
    }
}
