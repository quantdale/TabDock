using System;
using TabDock.Models;
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

    [Theory]
    [InlineData("\"oops\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("1.5")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    public void ClassifyJsonText_InvalidSchemaVersionIsCorruptWithoutThrowing(string versionToken)
    {
        string json = "{\"Version\":" + versionToken + ",\"Groups\":[]}";

        string classification = DiagnosticEnvironmentService.ClassifyJsonText(json, isState: true);

        Assert.Equal("corrupt (invalid-schema-version)", classification);
    }

    [Fact]
    public void ClassifyJsonText_FutureSchemaVersionIsUnsupported()
    {
        string json = "{\"Version\":" + (PersistedState.CurrentVersion + 1) + ",\"Groups\":[]}";

        string classification = DiagnosticEnvironmentService.ClassifyJsonText(json, isState: true);

        Assert.Equal("unsupported (future-schema-version)", classification);
    }

    /// <summary>
    /// R1 correction: an unsupported future schema must stay authoritative even
    /// when today's Groups property is absent or has a different shape. A future
    /// schema need not retain the current layout, so relabeling such evidence as
    /// corrupt contradicts the restore path and misguides support/recovery.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(",\"Groups\":null")]
    [InlineData(",\"Groups\":{}")]
    [InlineData(",\"Groups\":\"x\"")]
    [InlineData(",\"Groups\":7")]
    [InlineData(",\"Groups\":[{},{}]")]
    public void ClassifyJsonText_FutureSchemaVersionIsUnsupportedRegardlessOfGroupsShape(string groupsFragment)
    {
        string json = "{\"Version\":" + (PersistedState.CurrentVersion + 1) + groupsFragment + "}";

        string classification = DiagnosticEnvironmentService.ClassifyJsonText(json, isState: true);

        Assert.Equal("unsupported (future-schema-version)", classification);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"Groups\":null")]
    [InlineData(",\"Groups\":{}")]
    [InlineData(",\"Groups\":\"x\"")]
    public void ClassifyStateJson_FutureSchemaWithDifferentGroupsShape_ReportsUnsupportedWithNoFabricatedMetadata(string groupsFragment)
    {
        string json = "{\"Version\":" + (PersistedState.CurrentVersion + 1) + groupsFragment + "}";

        string status = DiagnosticEnvironmentService.ClassifyStateJson(
            json, out int? schemaVersion, out int groupCount, out int persistedMemberMetadataCount);

        Assert.Equal("unsupported (future-schema-version)", status);
        Assert.Equal(PersistedState.CurrentVersion + 1, schemaVersion);
        Assert.Equal(0, groupCount);
        Assert.Equal(0, persistedMemberMetadataCount);
    }

    [Fact]
    public void ClassifyStateJson_FutureSchemaWithRecognizableGroupsArray_StillReportsBestEffortMetadata()
    {
        string json = "{\"Version\":" + (PersistedState.CurrentVersion + 1)
            + ",\"Groups\":[{\"Tabs\":[{},{}]}]}";

        string status = DiagnosticEnvironmentService.ClassifyStateJson(
            json, out int? schemaVersion, out int groupCount, out int persistedMemberMetadataCount);

        Assert.Equal("unsupported (future-schema-version)", status);
        Assert.Equal(PersistedState.CurrentVersion + 1, schemaVersion);
        Assert.Equal(1, groupCount);
        Assert.Equal(2, persistedMemberMetadataCount);
    }

    [Fact]
    public void ClassifyJsonText_MissingVersionIsSupportedLegacyAbsence()
    {
        string classification = DiagnosticEnvironmentService.ClassifyJsonText("{\"Groups\":[]}", isState: true);

        Assert.Equal("valid (legacy-version-absent)", classification);
    }

    [Fact]
    public void ClassifyJsonText_MissingGroupsWithValidVersionIsCorrupt()
    {
        string classification = DiagnosticEnvironmentService.ClassifyJsonText("{\"Version\":2}", isState: true);

        Assert.Equal("corrupt (required-array-missing)", classification);
    }

    [Fact]
    public void ClassifyStateJson_InvalidVersionStillReportsAvailableMetadataWithoutThrowing()
    {
        string json = "{\"Version\":\"oops\",\"Groups\":[{\"Tabs\":[{},{}]}]}";

        string status = DiagnosticEnvironmentService.ClassifyStateJson(
            json, out int? schemaVersion, out int groupCount, out int persistedMemberMetadataCount);

        Assert.Equal("corrupt (invalid-schema-version)", status);
        Assert.Null(schemaVersion);
        Assert.Equal(1, groupCount);
        Assert.Equal(2, persistedMemberMetadataCount);
    }

    [Fact]
    public void ClassifyStateJson_FutureVersionReportsUnsupportedSchema()
    {
        string json = "{\"Version\":" + (PersistedState.CurrentVersion + 1) + ",\"Groups\":[]}";

        string status = DiagnosticEnvironmentService.ClassifyStateJson(
            json, out int? schemaVersion, out int groupCount, out _);

        Assert.Equal("unsupported (future-schema-version)", status);
        Assert.Equal(PersistedState.CurrentVersion + 1, schemaVersion);
        Assert.Equal(0, groupCount);
    }
}
