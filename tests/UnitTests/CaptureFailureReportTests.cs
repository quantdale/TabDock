using System;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// The single capture-failure summary shared by the picker path and the inline
/// Add-App panel: one owner-modal per batch, user-facing lines carry title and
/// reason but never a raw HWND (the log line keeps it for diagnosis).
/// </summary>
public class CaptureFailureReportTests
{
    private static CaptureFailureReport.Failure F(string title, long hwnd, string error)
        => new(title, new IntPtr(hwnd), error);

    [Fact]
    public void SingleFailure_UsesSingularCaption_AndHwndFreeUserLine()
    {
        string report = CaptureFailureReport.Build(new[] { F("Notepad", 0x1234, "window is elevated") });

        Assert.StartsWith("Could not capture the selected window.", report);
        Assert.Contains("Notepad: window is elevated", report);
        Assert.DoesNotContain("0x", report);
    }

    [Fact]
    public void MultiFailure_UsesAggregateCaption_AndOneLinePerFailure()
    {
        string report = CaptureFailureReport.Build(new[]
        {
            F("Alpha", 0x1, "gone"),
            F("Beta", 0x2, "elevated"),
            F("Gamma", 0x3, "dpi probe failed"),
        });

        Assert.StartsWith("Could not capture 3 of the selected windows; the others were captured.", report);
        Assert.Contains("Alpha: gone", report);
        Assert.Contains("Beta: elevated", report);
        Assert.Contains("Gamma: dpi probe failed", report);
        Assert.DoesNotContain("0x", report);
        // One body line per failure after the caption.
        Assert.Equal(4, report.Split('\n').Length);
    }

    [Fact]
    public void LogLine_RetainsRawHwndForDiagnosis()
    {
        string line = CaptureFailureReport.LogLine(F("Notepad", 0x1234, "window is elevated"));

        Assert.Equal("0x1234: window is elevated", line);
    }

    [Fact]
    public void EmptyFailureList_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CaptureFailureReport.Build(Array.Empty<CaptureFailureReport.Failure>()));
    }
}
