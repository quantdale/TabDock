using System;
using System.Collections.Generic;
using System.Text;

namespace TabDock.Services;

/// <summary>
/// Builds the ONE user-facing capture-failure summary shared by every capture
/// entry point (R21-014 transaction UX): attempt everything requested, keep
/// the successes, then present a single owner-modal instead of disabling the
/// container once per failing target while an admitted guest sits visible
/// above it. User-facing lines carry title and reason but never the raw HWND;
/// <see cref="LogLine"/> keeps it for diagnosis.
/// </summary>
internal static class CaptureFailureReport
{
    public readonly record struct Failure(string Title, IntPtr Hwnd, string Error);

    public static string LogLine(Failure failure)
        => $"0x{failure.Hwnd.ToInt64():X}: {failure.Error}";

    public static string Build(IReadOnlyList<Failure> failures)
    {
        if (failures.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(failures), "a report requires at least one failure");

        var body = new StringBuilder();
        if (failures.Count == 1)
        {
            body.Append("Could not capture the selected window.");
        }
        else
        {
            body.Append($"Could not capture {failures.Count} of the selected windows; the others were captured.");
        }

        foreach (Failure failure in failures)
        {
            body.AppendLine();
            body.Append($"{failure.Title}: {failure.Error}");
        }

        return body.ToString();
    }
}
