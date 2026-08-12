using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// The one application-level source for the identity of the running artifact.
/// It reads generated assembly metadata and never shells out to Git.
/// </summary>
public static class BuildIdentity
{
    private static readonly Assembly s_assembly = typeof(BuildIdentity).Assembly;
    private static readonly BuildIdentityInfo s_current = Create(includeHash: false);

    public static BuildIdentityInfo Current => Clone(s_current);

    public static BuildIdentityInfo Capture(bool includeHash)
        => includeHash ? Create(includeHash: true) : Current;

    public static string ToLogLine(BuildIdentityInfo identity)
        => $"BUILD[identity] version={identity.SemanticVersion} commit={identity.CommitHash} " +
           $"config={identity.BuildConfiguration} runtime={identity.RuntimeIdentifier} " +
           $"informationalVersion={identity.InformationalVersion}";

    internal static string? ParseCommitHash(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return null;
        int plus = informationalVersion.IndexOf('+');
        if (plus < 0 || plus == informationalVersion.Length - 1)
            return null;
        string candidate = informationalVersion[(plus + 1)..];
        return candidate.Length >= 7 && candidate.All(Uri.IsHexDigit) ? candidate : null;
    }

    internal static string ParseSemanticVersion(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            string value = informationalVersion.Split('+', 2)[0];
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return assemblyVersion?.ToString() ?? "unavailable";
    }

    private static BuildIdentityInfo Create(bool includeHash)
    {
        var info = new BuildIdentityInfo
        {
            ProductName = GetAttribute<AssemblyProductAttribute>()?.Product
                ?? s_assembly.GetName().Name
                ?? "TabDock",
            InformationalVersion = GetAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unavailable",
            ExecutablePath = Environment.ProcessPath ?? "unavailable",
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            RuntimeDescription = RuntimeInformation.FrameworkDescription,
        };
        info.SemanticVersion = ParseSemanticVersion(info.InformationalVersion, s_assembly.GetName().Version);
        info.CommitHash = ParseCommitHash(info.InformationalVersion) ?? GetMetadata("SourceRevisionId") ?? "unavailable";
        info.BuildConfiguration = GetMetadata("BuildConfiguration") ?? "unavailable";
        info.RuntimeIdentifier = GetMetadata("RuntimeIdentifier") ?? "unavailable";
        string? selfContained = GetMetadata("SelfContained");
        info.DeploymentModel = string.Equals(selfContained, "true", StringComparison.OrdinalIgnoreCase)
            ? "self-contained"
            : string.Equals(selfContained, "false", StringComparison.OrdinalIgnoreCase)
                ? "framework-dependent"
                : "unavailable";
        info.ExecutableFileVersion = GetFileVersion(info.ExecutablePath);
        if (includeHash)
            info.ExecutableSha256 = ComputeSha256(info.ExecutablePath);
        return info;
    }

    private static T? GetAttribute<T>() where T : Attribute
        => s_assembly.GetCustomAttributes(typeof(T), inherit: false).OfType<T>().FirstOrDefault();

    private static string? GetMetadata(string key)
        => s_assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.Ordinal))?.Value;

    private static string GetFileVersion(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                ? "unavailable"
                : FileVersionInfo.GetVersionInfo(path).FileVersion ?? "unavailable";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "unavailable";
        }
    }

    private static string ComputeSha256(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "unavailable (file-not-found)";
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (UnauthorizedAccessException)
        {
            return "unavailable (access-denied)";
        }
        catch (IOException)
        {
            return "unavailable (io-error)";
        }
        catch (Exception)
        {
            return "unavailable (hash-error)";
        }
    }

    private static BuildIdentityInfo Clone(BuildIdentityInfo source)
        => new()
        {
            ProductName = source.ProductName,
            SemanticVersion = source.SemanticVersion,
            CommitHash = source.CommitHash,
            BuildConfiguration = source.BuildConfiguration,
            RuntimeIdentifier = source.RuntimeIdentifier,
            BuildTimestampUtc = source.BuildTimestampUtc,
            InformationalVersion = source.InformationalVersion,
            ExecutablePath = source.ExecutablePath,
            ExecutableSha256 = source.ExecutableSha256,
            ExecutableFileVersion = source.ExecutableFileVersion,
            ProcessArchitecture = source.ProcessArchitecture,
            OsArchitecture = source.OsArchitecture,
            RuntimeDescription = source.RuntimeDescription,
            DeploymentModel = source.DeploymentModel,
        };
}
