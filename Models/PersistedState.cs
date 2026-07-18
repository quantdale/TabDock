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
    public int Version { get; set; } = 1;
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
    public string ExePath { get; set; } = string.Empty;
}

/// <summary>Root DTO for %APPDATA%\TabDock\hidden-windows.json.</summary>
public sealed class HiddenWindowJournalFile
{
    public List<HiddenWindowEntry> Entries { get; set; } = new();
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(PersistedState))]
[JsonSerializable(typeof(HiddenWindowJournalFile))]
public partial class TabDockJsonContext : JsonSerializerContext
{
}
