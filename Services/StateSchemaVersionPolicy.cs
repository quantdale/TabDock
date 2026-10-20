using System;
using System.Globalization;
using System.Text.Json;
using TabDock.Models;

namespace TabDock.Services;

internal enum StateSchemaVersionKind
{
    Missing,
    Supported,
    Unsupported,
    Corrupt,
}

/// <summary>
/// Pure, total classification of the persisted-state root Version property.
/// Shared by the mutating restore path and the read-only doctor inspector so
/// both report the same schema semantics without duplicating conversion rules.
/// </summary>
internal static class StateSchemaVersionPolicy
{
    internal static StateSchemaVersionKind Classify(
        JsonElement root,
        out int version,
        out string reason)
    {
        // Absence retains legacy-v1 semantics; the caller decides whether that
        // is a supported legacy file or a distinguished report status.
        version = 1;
        reason = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            reason = "root is not an object";
            return StateSchemaVersionKind.Corrupt;
        }

        if (!TryGetPropertyIgnoreCase(root, "Version", out JsonElement versionElement))
            return StateSchemaVersionKind.Missing;

        // JSON numeric types are the only values TryGetInt32 may inspect; any
        // other kind throws InvalidOperationException if converted blindly.
        if (versionElement.ValueKind != JsonValueKind.Number)
        {
            reason = "Version is not an integer";
            return StateSchemaVersionKind.Corrupt;
        }

        // Fractions and numbers outside the signed 32-bit range are not
        // representable schema versions.
        if (!versionElement.TryGetInt32(out version))
        {
            version = 1;
            reason = "Version is not a representable 32-bit integer";
            return StateSchemaVersionKind.Corrupt;
        }

        if (version < 1)
        {
            reason = version.ToString(CultureInfo.InvariantCulture);
            return StateSchemaVersionKind.Corrupt;
        }

        if (version > PersistedState.CurrentVersion)
        {
            reason = version.ToString(CultureInfo.InvariantCulture);
            return StateSchemaVersionKind.Unsupported;
        }

        return StateSchemaVersionKind.Supported;
    }

    internal static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}
