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

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(PersistedState))]
public partial class TabDockJsonContext : JsonSerializerContext
{
}
