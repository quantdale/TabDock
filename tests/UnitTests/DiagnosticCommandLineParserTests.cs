using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former DiagnosticSelfTest aggregator (Wave 4): the
/// dependency-free diagnostic command parser contract.
/// </summary>
public class DiagnosticCommandLineParserTests
{
    [Fact]
    public void TryParse_VersionCommand_IsRecognized()
    {
        Assert.True(DiagnosticCommandLine.TryParse(new[] { "--version" }, out DiagnosticCommandRequest request, out _));
        Assert.Equal(DiagnosticCommandKind.Version, request.Kind);
    }

    [Fact]
    public void TryParse_DoctorWithOutput_KeepsRequestedPath()
    {
        Assert.True(
            DiagnosticCommandLine.TryParse(new[] { "--doctor", "--output", "report.txt" }, out DiagnosticCommandRequest request, out _),
            "--doctor --output <path> must parse");
        Assert.Equal(DiagnosticCommandKind.Doctor, request.Kind);
        Assert.Equal("report.txt", request.OutputPath);
    }

    [Fact]
    public void TryParse_UnknownOption_ProducesParserError()
    {
        Assert.True(DiagnosticCommandLine.TryParse(new[] { "--doctor", "--bad" }, out _, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_RecoveryCommands_AreRecognized()
    {
        Assert.True(DiagnosticCommandLine.TryParse(new[] { "--pending-recovery" }, out DiagnosticCommandRequest pending, out _));
        Assert.Equal(DiagnosticCommandKind.PendingRecovery, pending.Kind);
        Assert.True(DiagnosticCommandLine.TryParse(new[] { "--recover-pending" }, out DiagnosticCommandRequest recover, out _));
        Assert.Equal(DiagnosticCommandKind.RecoverPending, recover.Kind);
    }

    [Fact]
    public void TryParse_EmptyArguments_IsNotADiagnosticCommand()
    {
        Assert.False(DiagnosticCommandLine.TryParse(Array.Empty<string>(), out _, out _));
    }
}
