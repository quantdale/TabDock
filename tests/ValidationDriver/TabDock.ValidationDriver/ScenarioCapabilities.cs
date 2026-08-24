using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabDock.Services;

namespace TabDock.ValidationDriver;

internal sealed record ScenarioDescriptor(
    string Name,
    string? RequiredBrowser = null,
    string? RequiredApplication = null,
    bool RequiresInteractiveSession = true,
    bool RequiresMultiMonitor = false,
    bool RequiresMixedDpi = false,
    bool RequiresNonDefaultDpi = false,
    bool RequiresSigning = false,
    bool RequiresStageB = false);

/// <summary>Privacy-safe capability snapshot used by preflight and manifests.</summary>
internal sealed record ScenarioCapabilitySnapshot(
    bool ChromeAvailable,
    bool EdgeAvailable,
    bool BraveAvailable,
    bool FirefoxAvailable,
    bool WindowsTerminalAvailable,
    bool NotepadAvailable,
    bool NotepadBrokerBehaviorDetectable,
    int MonitorCount,
    bool MixedDpiAvailable,
    bool NonDefaultDpiAvailable,
    bool NegativeVirtualCoordinatesAvailable,
    bool InteractiveSessionAvailable,
    bool WorkstationLockedKnown,
    bool WorkstationLocked,
    bool SendInputAvailable,
    bool CandidateSigningConfigured,
    bool StageBAvailable)
{
    public bool MultiMonitorAvailable => MonitorCount > 1;

    public bool BrowserAvailable(string? browser)
        => browser?.ToLowerInvariant() switch
        {
            "chrome-normal" or "chrome-nogpu" or "chrome-gpu" => ChromeAvailable,
            "edge-normal" => EdgeAvailable,
            "brave-normal" => BraveAvailable,
            "firefox-normal" => FirefoxAvailable,
            "chrome-and-edge" => ChromeAvailable && EdgeAvailable,
            _ => true,
        };

    public bool ApplicationAvailable(string? application)
        => application?.ToLowerInvariant() switch
        {
            "notepad" => NotepadAvailable,
            "windows-terminal" => WindowsTerminalAvailable,
            "notepad-broker" => NotepadAvailable && NotepadBrokerBehaviorDetectable,
            _ => true,
        };
}

internal sealed record ScenarioCapabilityResolution(
    bool Runnable,
    ScenarioOutcomeKind? Outcome,
    string? Reason,
    ScenarioCapabilitySnapshot Snapshot)
{
    public static ScenarioCapabilityResolution RunnableResult(ScenarioCapabilitySnapshot snapshot)
        => new(true, null, null, snapshot);

    public static ScenarioCapabilityResolution Blocked(
        ScenarioCapabilitySnapshot snapshot,
        ScenarioOutcomeKind outcome,
        string reason)
        => new(false, outcome, reason, snapshot);
}

/// <summary>
/// Central capability discovery and descriptor resolution. This class contains
/// no process launch and can be tested entirely from synthetic snapshots.
/// </summary>
internal static class ScenarioCapabilities
{
    public static ScenarioDescriptor Describe(string scenario, Options options)
    {
        string? browser = null;
        if (scenario == "browser-multi")
            browser = "chrome-and-edge";
        else if (scenario.StartsWith("browser-", StringComparison.Ordinal))
            browser = options.Guest;
        else if (scenario == "chrometabdrag"
            || scenario == "chromeinput"
            || scenario.StartsWith("keyboardinput-chrome", StringComparison.Ordinal))
            browser = "chrome-normal";
        else if (scenario.StartsWith("keyboardinput-edge", StringComparison.Ordinal))
            browser = "edge-normal";

        string? application = scenario switch
        {
            "keyboardinput-notepad" => "notepad-broker",
            "maximize-repro" when string.Equals(options.Guest, "wt", StringComparison.OrdinalIgnoreCase)
                => "windows-terminal",
            _ => null,
        };

        bool topology = scenario.Contains("multi-monitor", StringComparison.OrdinalIgnoreCase);
        bool dpi = scenario.Contains("dpi", StringComparison.OrdinalIgnoreCase);

        return new ScenarioDescriptor(
            scenario,
            browser,
            application,
            RequiresInteractiveSession: true,
            RequiresMultiMonitor: topology,
            RequiresMixedDpi: scenario.Contains("mixed", StringComparison.OrdinalIgnoreCase),
            RequiresNonDefaultDpi: dpi,
            RequiresSigning: scenario.Contains("signing", StringComparison.OrdinalIgnoreCase),
            RequiresStageB: scenario.Contains("stage-b", StringComparison.OrdinalIgnoreCase));
    }

    public static ScenarioCapabilityResolution Resolve(
        ScenarioDescriptor descriptor,
        ScenarioCapabilitySnapshot snapshot)
    {
        if (descriptor.RequiredBrowser != null && !snapshot.BrowserAvailable(descriptor.RequiredBrowser))
        {
            return ScenarioCapabilityResolution.Blocked(
                snapshot,
                ScenarioOutcomeKind.SkipCapability,
                $"{descriptor.Name}: required browser capability '{descriptor.RequiredBrowser}' is unavailable");
        }

        if (descriptor.RequiredApplication != null
            && !snapshot.ApplicationAvailable(descriptor.RequiredApplication))
        {
            return ScenarioCapabilityResolution.Blocked(
                snapshot,
                ScenarioOutcomeKind.SkipCapability,
                $"{descriptor.Name}: required application capability '{descriptor.RequiredApplication}' is unavailable");
        }

        if (descriptor.RequiresInteractiveSession && !snapshot.InteractiveSessionAvailable)
        {
            return ScenarioCapabilityResolution.Blocked(
                snapshot,
                ScenarioOutcomeKind.BlockedEnvironment,
                $"{descriptor.Name}: interactive session is unavailable");
        }

        if (descriptor.RequiresInteractiveSession
            && snapshot.WorkstationLockedKnown
            && snapshot.WorkstationLocked)
        {
            return ScenarioCapabilityResolution.Blocked(
                snapshot,
                ScenarioOutcomeKind.BlockedEnvironment,
                $"{descriptor.Name}: workstation is locked");
        }

        if (descriptor.RequiresInteractiveSession && !snapshot.SendInputAvailable)
        {
            return ScenarioCapabilityResolution.Blocked(
                snapshot,
                ScenarioOutcomeKind.BlockedEnvironment,
                $"{descriptor.Name}: interactive SendInput capability could not be proven");
        }

        if (descriptor.RequiresMultiMonitor && !snapshot.MultiMonitorAvailable)
        {
            return ScenarioCapabilityResolution.Blocked(
                snapshot,
                ScenarioOutcomeKind.BlockedEnvironment,
                $"{descriptor.Name}: multi-monitor topology is unavailable");
        }

        if (descriptor.RequiresMixedDpi && !snapshot.MixedDpiAvailable)
        {
            return ScenarioCapabilityResolution.Blocked(
                snapshot,
                ScenarioOutcomeKind.BlockedEnvironment,
                $"{descriptor.Name}: mixed-DPI topology is unavailable");
        }

        if (descriptor.RequiresNonDefaultDpi && !snapshot.NonDefaultDpiAvailable)
        {
            return ScenarioCapabilityResolution.Blocked(
                snapshot,
                ScenarioOutcomeKind.SkipCapability,
                $"{descriptor.Name}: no non-default monitor DPI is available");
        }

        if (descriptor.RequiresSigning && !snapshot.CandidateSigningConfigured)
        {
            return ScenarioCapabilityResolution.Blocked(
                snapshot,
                ScenarioOutcomeKind.BlockedCapability,
                $"{descriptor.Name}: production signing capability is not configured");
        }

        if (descriptor.RequiresStageB && !snapshot.StageBAvailable)
        {
            return ScenarioCapabilityResolution.Blocked(
                snapshot,
                ScenarioOutcomeKind.BlockedCapability,
                $"{descriptor.Name}: Stage-B capability is unavailable");
        }

        return ScenarioCapabilityResolution.RunnableResult(snapshot);
    }

    public static ScenarioCapabilitySnapshot CaptureCurrent()
    {
        bool interactive = Environment.UserInteractive;
        IntPtr foreground = IntPtr.Zero;
        try { foreground = NativeMethods.GetForegroundWindow(); } catch { }

        List<NativeMethods.RECT> monitors = new();
        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr _, IntPtr _, ref NativeMethods.RECT rect, IntPtr _) =>
                {
                    monitors.Add(rect);
                    return true;
                }, IntPtr.Zero);
        }
        catch
        {
            monitors.Clear();
        }

        bool negative = monitors.Any(rect => rect.left < 0 || rect.top < 0);
        var dpis = new HashSet<uint>();
        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr monitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
                {
                    uint dpi = MonitorDpiService.GetEffectiveDpi(monitor);
                    if (dpi != 0)
                        dpis.Add(dpi);
                    return true;
                }, IntPtr.Zero);
        }
        catch { dpis.Clear(); }
        bool nonDefaultDpi = dpis.Any(dpi => dpi != NativeMethods.USER_DEFAULT_SCREEN_DPI);

        bool lockedKnown = true;
        bool locked = foreground == IntPtr.Zero;
        bool sendInput = interactive && foreground != IntPtr.Zero;
        return new ScenarioCapabilitySnapshot(
            IsExecutableAvailable("chrome.exe", new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
            }),
            IsExecutableAvailable("msedge.exe", new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            }),
            IsExecutableAvailable("brave.exe", new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            }),
            IsExecutableAvailable("firefox.exe", new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mozilla Firefox", "firefox.exe"),
            }),
            IsExecutableAvailable("wt.exe", Array.Empty<string>()),
            IsExecutableAvailable("notepad.exe", new[] { Path.Combine(Environment.SystemDirectory, "notepad.exe") }),
            IsNotepadBrokerBehaviorDetectable(),
            monitors.Count,
            dpis.Count > 1,
            nonDefaultDpi,
            negative,
            interactive,
            lockedKnown,
            locked,
            sendInput,
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIGNING_PROVIDER")),
            string.Equals(Environment.GetEnvironmentVariable("TABDOCK_STAGE_B_AVAILABLE"), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExecutableAvailable(string executable, IReadOnlyList<string> knownPaths)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return false;
        if (knownPaths.Any(File.Exists))
            return true;
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return false;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, executable)));
    }

    private static bool IsNotepadBrokerBehaviorDetectable()
    {
        // The broker is an OS behavior, not a capability that can be safely
        // proven without launching/adopting a real window. Report it as
        // detectable when the platform supplies Notepad and an interactive
        // desktop; the scenario still pins the returned HWND before input.
        return Environment.UserInteractive
            && File.Exists(Path.Combine(Environment.SystemDirectory, "notepad.exe"));
    }
}
