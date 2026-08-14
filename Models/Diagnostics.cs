using System;
using System.Collections.Generic;

namespace TabDock.Models;

/// <summary>Build and artifact identity carried by a TabDock executable.</summary>
public sealed class BuildIdentityInfo
{
    public string ProductName { get; set; } = "TabDock";
    public string SemanticVersion { get; set; } = "unavailable";
    public string CommitHash { get; set; } = "unavailable";
    public string BuildConfiguration { get; set; } = "unavailable";
    public string RuntimeIdentifier { get; set; } = "unavailable";
    public string BuildTimestampUtc { get; set; } = "not-included-for-reproducibility";
    public string InformationalVersion { get; set; } = "unavailable";
    public string ExecutablePath { get; set; } = "unavailable";
    public string ExecutableSha256 { get; set; } = "unavailable";
    public string ExecutableFileVersion { get; set; } = "unavailable";
    public string ProcessArchitecture { get; set; } = "unavailable";
    public string OsArchitecture { get; set; } = "unavailable";
    public string RuntimeDescription { get; set; } = "unavailable";
    public string DeploymentModel { get; set; } = "unavailable";
}

/// <summary>Read-only Windows/runtime context for a support report.</summary>
public sealed class WindowsEnvironmentSnapshot
{
    public string ProductName { get; set; } = "unavailable";
    public string DisplayVersion { get; set; } = "unavailable";
    public string Build { get; set; } = "unavailable";
    public string Revision { get; set; } = "unavailable";
    public string OsVersion { get; set; } = "unavailable";
    public string Runtime { get; set; } = "unavailable";
    public string ProcessArchitecture { get; set; } = "unavailable";
    public string OsArchitecture { get; set; } = "unavailable";
    public bool IsElevated { get; set; }
    public string ElevationStatus { get; set; } = "unavailable";
    public int SessionId { get; set; } = -1;
}

/// <summary>One physical monitor as observed by the current process.</summary>
public sealed class MonitorSnapshot
{
    public int Index { get; set; }
    public string MonitorHandle { get; set; } = "unavailable";
    public bool Primary { get; set; }
    public DiagnosticRect Bounds { get; set; } = new();
    public DiagnosticRect WorkArea { get; set; } = new();
    public uint EffectiveDpiX { get; set; }
    public uint EffectiveDpiY { get; set; }
    public string ScalePercent { get; set; } = "unavailable";
    public string Orientation { get; set; } = "unavailable";
    public string Status { get; set; } = "ok";
}

/// <summary>Display adapter information available from built-in User32 APIs.</summary>
public sealed class DisplayAdapterSnapshot
{
    public int Index { get; set; }
    public string Name { get; set; } = "unavailable";
    public string Description { get; set; } = "unavailable";
    public string DeviceId { get; set; } = "unavailable";
    public string Status { get; set; } = "ok";
    public string DriverVersion { get; set; } = "unavailable (not exposed by User32 enumeration)";
}

/// <summary>Sanitized persistence and journal health summary.</summary>
public sealed class PersistenceSnapshot
{
    public string StatePath { get; set; } = "%APPDATA%\\TabDock\\state.json";
    public string StateStatus { get; set; } = "absent";
    public int? SchemaVersion { get; set; }
    public int GroupCount { get; set; }
    public int PersistedMemberMetadataCount { get; set; }
    public string JournalPath { get; set; } = "%APPDATA%\\TabDock\\hidden-windows.json";
    public string JournalStatus { get; set; } = "absent";
    public int? JournalEntryCount { get; set; }
    public string PendingJournalStatus { get; set; } = "absent";
    public int PendingJournalFileCount { get; set; }
    public bool LogExists { get; set; }
    public string? ErrorCategory { get; set; }
}

/// <summary>A process identity row without command-line or user data.</summary>
public sealed class TabDockProcessSnapshot
{
    public uint ProcessId { get; set; }
    public string ExecutableName { get; set; } = "unavailable";
    public string ExecutablePath { get; set; } = "unavailable";
    public string StartTimeUtc { get; set; } = "unavailable";
    public string Architecture { get; set; } = "unavailable";
    public string Elevation { get; set; } = "unavailable";
    public int SessionId { get; set; } = -1;
    public long MainHwnd { get; set; }
    public bool MainHwndVisible { get; set; }
    public bool MainHwndIconic { get; set; }
    public string Status { get; set; } = "ok";
}

/// <summary>Simple screen-coordinate rectangle used by diagnostic JSON.</summary>
public sealed class DiagnosticRect
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public static DiagnosticRect From(NativeMethods.RECT rect) => new()
    {
        Left = rect.left,
        Top = rect.top,
        Width = rect.Width,
        Height = rect.Height,
    };
}

/// <summary>One safe WindowFromPoint result for a diagnostic sample point.</summary>
public sealed class WindowPointProbe
{
    public string Name { get; set; } = "unknown";
    public int X { get; set; }
    public int Y { get; set; }
    public long ReturnedHwnd { get; set; }
    public uint ReturnedPid { get; set; }
    public string ReturnedClass { get; set; } = "unavailable";
    public string ReturnedProcess { get; set; } = "unavailable";
    public string Status { get; set; } = "ok";
}

/// <summary>Observed native state for one top-level HWND.</summary>
public sealed class NativeWindowSnapshot
{
    public string Role { get; set; } = "unknown";
    public string Status { get; set; } = "ok";
    public long Hwnd { get; set; }
    public uint ProcessId { get; set; }
    public string ProcessName { get; set; } = "unavailable";
    public string ProcessPath { get; set; } = "unavailable";
    public string ProcessStartTimeUtc { get; set; } = "unavailable";
    public string WindowClass { get; set; } = "unavailable";
    public int TitleLength { get; set; }
    public string TitleSha256 { get; set; } = "unavailable";
    public bool IsWindow { get; set; }
    public bool Visible { get; set; }
    public bool Iconic { get; set; }
    public bool Zoomed { get; set; }
    public bool Foreground { get; set; }
    public bool Topmost { get; set; }
    public string Cloaked { get; set; } = "unavailable";
    public DiagnosticRect? Rect { get; set; }
    public DiagnosticRect? ClientRectScreen { get; set; }
    public string Monitor { get; set; } = "unavailable";
    public uint EffectiveDpi { get; set; }
    public string DpiAwarenessContext { get; set; } = "unavailable";
    public long OwnerHwnd { get; set; }
    public long PreviousZOrderHwnd { get; set; }
    public long NextZOrderHwnd { get; set; }
    public string Elevation { get; set; } = "unavailable";
    public List<WindowPointProbe> PointProbes { get; set; } = new();
}

/// <summary>Identity and observed state for one live captured guest.</summary>
public sealed class DiagnosticMemberSnapshot
{
    public string MemberKey { get; set; } = "unavailable";
    public long Hwnd { get; set; }
    public uint ProcessId { get; set; }
    public string ExecutableName { get; set; } = "unavailable";
    public string WindowClass { get; set; } = "unavailable";
    public bool Visible { get; set; }
    public bool Iconic { get; set; }
    public bool Zoomed { get; set; }
    public DiagnosticRect? ExpectedPaneRect { get; set; }
}

/// <summary>Current desired/logical presentation for one TabDock group.</summary>
public sealed class LogicalPresentationSnapshot
{
    public Guid GroupId { get; set; }
    public long ContainerHwnd { get; set; }
    public bool ContainerVisible { get; set; }
    public string WindowState { get; set; } = "unknown";
    public bool Minimized { get; set; }
    public bool Maximized { get; set; }
    public string? ActiveMemberKey { get; set; }
    public long ActiveGuestHwnd { get; set; }
    public bool SplitActive { get; set; }
    public string? SplitLeftMemberKey { get; set; }
    public long SplitLeftHwnd { get; set; }
    public string? SplitRightMemberKey { get; set; }
    public long SplitRightHwnd { get; set; }
    public string? SplitForegroundMemberKey { get; set; }
    public long SplitForegroundHwnd { get; set; }
    public bool ChromeInteractionActive { get; set; }
    public string Monitor { get; set; } = "unavailable";
    public List<DiagnosticMemberSnapshot> Members { get; set; } = new();
    public List<DiagnosticRect> ExpectedPaneRects { get; set; } = new();
}

/// <summary>One bounded, ordered diagnostic event.</summary>
public sealed class DiagnosticEventRecord
{
    public DiagnosticEventRecord()
        : this(null)
    {
    }

    internal DiagnosticEventRecord(IReadOnlyDictionary<string, string>? data)
    {
        Data = data == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(data, StringComparer.Ordinal);
    }

    public long Sequence { get; set; }
    public string TimestampUtc { get; set; } = "unavailable";
    public string Kind { get; set; } = "unknown";
    public string? GroupId { get; set; }
    public long ContainerHwnd { get; set; }
    public long GuestHwnd { get; set; }
    public long ForegroundHwnd { get; set; }
    public string? Action { get; set; }
    public string? Result { get; set; }
    public Dictionary<string, string> Data { get; set; }
}

/// <summary>Complete local report assembled from independent read-only sections.</summary>
public sealed class DiagnosticReport
{
    public string ReportVersion { get; set; } = "1";
    public string GeneratedUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public BuildIdentityInfo Build { get; set; } = new();
    public WindowsEnvironmentSnapshot Windows { get; set; } = new();
    public List<MonitorSnapshot> Monitors { get; set; } = new();
    public List<DisplayAdapterSnapshot> DisplayAdapters { get; set; } = new();
    public PersistenceSnapshot Persistence { get; set; } = new();
    public List<TabDockProcessSnapshot> TabDockProcesses { get; set; } = new();
    public List<NativeWindowSnapshot> NativeWindows { get; set; } = new();
    public List<LogicalPresentationSnapshot> LogicalPresentations { get; set; } = new();
    public List<DiagnosticEventRecord> Trace { get; set; } = new();
    public string RecentLog { get; set; } = "unavailable";
    public List<string> Issues { get; set; } = new();
}
