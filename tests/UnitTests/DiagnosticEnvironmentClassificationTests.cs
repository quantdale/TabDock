using System;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former DiagnosticSelfTest aggregator (Wave 4): diagnostic
/// title hashing never leaks the raw title, and state-JSON text classification
/// distinguishes valid from corrupt evidence.
/// </summary>
public class DiagnosticEnvironmentClassificationTests
{
    [Fact]
    public void HashTitle_DoesNotReturnTheRawTitle()
    {
        Assert.NotEqual("secret", DiagnosticEnvironmentService.HashTitle("secret"));
    }

    [Fact]
    public void ClassifyJsonText_ValidStateIsClassifiedValid()
    {
        Assert.Equal(
            "valid",
            DiagnosticEnvironmentService.ClassifyJsonText("{\"Version\":1,\"Groups\":[]}", isState: true));
    }

    [Fact]
    public void ClassifyJsonText_MalformedStateIsClassifiedCorrupt()
    {
        string classification = DiagnosticEnvironmentService.ClassifyJsonText("not-json", isState: true);
        Assert.StartsWith("corrupt", classification, StringComparison.Ordinal);
    }
}
