using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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

    public VisualPathPolicy(string root, bool createRoot = true)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("artifact root is required.", nameof(root));
        _root = Path.GetFullPath(root);
        _rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (createRoot)
            Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public string NormalizeRelative(string relativePath) => NormalizePortableRelative(relativePath);

    internal static string NormalizePortableRelative(string relativePath)
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
        if (segments.Any(segment => segment.EndsWith('.') || segment.EndsWith(' ')
            || segment.Any(character => character < ' ' || "<>:\"|?*".Contains(character))
            || Regex.IsMatch(segment, @"^(?:CON|PRN|AUX|NUL|COM[1-9\u00B9\u00B2\u00B3]|LPT[1-9\u00B9\u00B2\u00B3])(?:\.|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
        {
            throw new ArgumentException($"visual artifact path contains an unsafe Win32 segment: '{relativePath}'.", nameof(relativePath));
        }
        return string.Join('/', segments);
    }

    public string Resolve(string relativePath)
    {
        string normalized = NormalizeRelative(relativePath);
        string full = Path.GetFullPath(Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"visual artifact path escapes the artifact root: '{relativePath}'.", nameof(relativePath));
        EnsureNoReparseComponents(normalized);
        return full;
    }

    public string RelativeFromFullPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            throw new ArgumentException("full artifact path is empty.", nameof(fullPath));
        string full = Path.GetFullPath(fullPath);
        if (!full.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"path is outside the artifact root: '{fullPath}'.", nameof(fullPath));
        string normalized = NormalizeRelative(Path.GetRelativePath(_root, full));
        EnsureNoReparseComponents(normalized);
        return normalized;
    }

    private void EnsureNoReparseComponents(string normalized)
    {
        string component = _root;
        foreach (string segment in new[] { string.Empty }.Concat(normalized.Split('/')))
        {
            if (segment.Length > 0)
                component = Path.Combine(component, segment);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(component); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("visual artifact path contains a reparse-point component.", nameof(normalized));
        }
    }
}
