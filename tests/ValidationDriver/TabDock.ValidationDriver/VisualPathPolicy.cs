using System;
using System.IO;
using System.Linq;

namespace TabDock.ValidationDriver;

/// <summary>
/// Resolves visual artifact paths relative to one run-owned directory. The
/// policy is deliberately stricter than Path.GetFullPath so portable records
/// cannot contain empty, dot, traversal, drive, or UNC segments.
/// </summary>
internal sealed class VisualPathPolicy
{
    private readonly string _root;
    private readonly string _rootWithSeparator;

    public VisualPathPolicy(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("artifact root is required.", nameof(root));
        _root = Path.GetFullPath(root);
        _rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public string NormalizeRelative(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("visual artifact path is empty.", nameof(relativePath));
        string candidate = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(candidate) || candidate.StartsWith("/", StringComparison.Ordinal)
            || candidate.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException($"visual artifact path is absolute: '{relativePath}'.", nameof(relativePath));
        }

        string[] segments = candidate.Split('/', StringSplitOptions.None);
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new ArgumentException($"visual artifact path contains an empty or traversal segment: '{relativePath}'.", nameof(relativePath));
        return string.Join('/', segments);
    }

    public string Resolve(string relativePath)
    {
        string normalized = NormalizeRelative(relativePath);
        string full = Path.GetFullPath(Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"visual artifact path escapes the artifact root: '{relativePath}'.", nameof(relativePath));
        return full;
    }

    public string RelativeFromFullPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            throw new ArgumentException("full artifact path is empty.", nameof(fullPath));
        string full = Path.GetFullPath(fullPath);
        if (!full.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"path is outside the artifact root: '{fullPath}'.", nameof(fullPath));
        return NormalizeRelative(Path.GetRelativePath(_root, full));
    }
}
