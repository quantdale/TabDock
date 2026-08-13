using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TabDock.Models;

/// <summary>
/// Root DTO for serializing/restoring TabDock metadata.
/// Live HWNDs are intentionally not persisted.
/// </summary>
public sealed class PersistedState
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<PersistedGroup> Groups { get; set; } = new();
}

public sealed class PersistedGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AccentColor { get; set; } = string.Empty;
    public int ActiveIndex { get; set; }
    public List<PersistedTab> Tabs { get; set; } = new();
}

public sealed class PersistedTab
{
    public string ExePath { get; set; } = string.Empty;
    public string OriginalTitle { get; set; } = string.Empty;
    public string CustomLabel { get; set; } = string.Empty;
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public bool WasMaximized { get; set; }
}

/// <summary>One hidden shepherded guest tracked for crash recovery (see WindowShepherdService.RescueOrphanedWindows).</summary>
public sealed class HiddenWindowEntry
{
    public long Hwnd { get; set; }
    public uint Pid { get; set; }
    public uint WindowThreadId { get; set; }
    /// <summary>Per-capture HWND property token used to reject same-process HWND recycling during rescue.</summary>
    public long WindowIdentityToken { get; set; }
    public string ExePath { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public long ProcessStartTimeUtcTicks { get; set; }
    public bool OriginallyVisible { get; set; }
    public bool HasOriginalPlacement { get; set; }
    public uint OriginalPlacementFlags { get; set; }
    public int OriginalShowCommand { get; set; }
    public int OriginalMinPositionX { get; set; }
    public int OriginalMinPositionY { get; set; }
    public int OriginalMaxPositionX { get; set; }
    public int OriginalMaxPositionY { get; set; }
    public int OriginalNormalLeft { get; set; }
    public int OriginalNormalTop { get; set; }
    public int OriginalNormalRight { get; set; }
    public int OriginalNormalBottom { get; set; }
    public bool HasOriginalTransitionsState { get; set; }
    public bool OriginalTransitionsDisabled { get; set; }

    /// <summary>
    /// Durable marker used for the tiny self-hide transition window. Rescue
    /// consumes an entry with this marker without showing the guest, even if
    /// the subsequent clear was interrupted by a hard kill.
    /// </summary>
    public bool DoNotRescue { get; set; }
}

/// <summary>Root DTO for %APPDATA%\TabDock\hidden-windows.json.</summary>
public sealed class HiddenWindowJournalFile
{
    /// <summary>
    /// v1: original minimal HWND/PID/executable journal with no explicit
    /// version field. v2: full presentation and process-start journal from
    /// the deep-audit remediation. v3: v2 plus GUI-thread and per-capture
    /// HWND-generation identity fields.
    /// </summary>
    public const int CurrentVersion = 3;

    public const int LegacyMinimalVersion = 1;
    public const int PresentationIdentityVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<HiddenWindowEntry> Entries { get; set; } = new();
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(PersistedState))]
[JsonSerializable(typeof(HiddenWindowJournalFile))]
public partial class TabDockJsonContext : JsonSerializerContext
{
}
