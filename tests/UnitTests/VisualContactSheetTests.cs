using System;
using System.Collections.Generic;
using System.IO;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class VisualContactSheetTests
{
    [Fact]
    public void EmptySelection_IsRejectedWithoutCreatingDerivedEvidence()
    {
        string root = CreateTempRoot();
        try
        {
            bool built = VisualContactSheetBuilder.TryBuild(
                root,
                Array.Empty<VisualArtifactRecord>(),
                1024,
                1024,
                1_000_000,
                out VisualContactSheetBuildResult? result,
                out string reason);

            Assert.False(built);
            Assert.Null(result);
            Assert.Contains("no raw", reason, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void OneImage_ProducesBoundedPngAndKeepsRawArtifactSeparate()
    {
        string root = CreateTempRoot();
        try
        {
            VisualArtifactRecord raw = WriteRaw(root, "one", 3, 2, 20, "A stable guest remains visible after rename.");
            string rawPath = Path.Combine(root, raw.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            bool built = VisualContactSheetBuilder.TryBuild(
                root,
                new[] { raw },
                1024,
                1024,
                1_000_000,
                out VisualContactSheetBuildResult? result,
                out string reason);

            Assert.True(built, reason);
            Assert.NotNull(result);
            Assert.Single(result!.IncludedArtifacts);
            Assert.Equal(raw.ArtifactId, result.IncludedArtifacts[0].ArtifactId);
            (int width, int height, int[] pixels) = VisualPngEncoder.Decode(result.Png);
            Assert.True(width <= 1024);
            Assert.True(height <= 1024);
            Assert.Equal(width * height, pixels.Length);
            Assert.True(File.Exists(rawPath));
            Assert.NotEqual(raw.Sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(result.Png)).ToLowerInvariant());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ManyMixedDimensions_AreChronologicalAndLabelsAreBounded()
    {
        string root = CreateTempRoot();
        try
        {
            var raw = new List<VisualArtifactRecord>
            {
                WriteRaw(root, "late", 7, 3, 300, new string('x', 500)),
                WriteRaw(root, "early", 2, 8, 100, "Initial guest is visible."),
                WriteRaw(root, "middle", 4, 4, 200, "The container keeps the active guest visible."),
            };

            bool built = VisualContactSheetBuilder.TryBuild(
                root,
                raw,
                900,
                500,
                2_000_000,
                out VisualContactSheetBuildResult? result,
                out string reason);

            Assert.True(built, reason);
            Assert.NotNull(result);
            Assert.Equal(new[] { "early", "middle", "late" },
                result!.IncludedArtifacts.Select(artifact => artifact.ArtifactId));
            (int width, int height, int[] pixels) = VisualPngEncoder.Decode(result.Png);
            Assert.True(width <= 900);
            Assert.True(height <= 500);
            Assert.Equal(width * height, pixels.Length);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void DerivedByteLimitFailure_PreservesRawEvidence()
    {
        string root = CreateTempRoot();
        try
        {
            VisualArtifactRecord raw = WriteRaw(root, "raw", 2, 2, 0, "Raw evidence remains authoritative.");
            string rawPath = Path.Combine(root, raw.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            bool built = VisualContactSheetBuilder.TryBuild(
                root,
                new[] { raw },
                512,
                512,
                1,
                out VisualContactSheetBuildResult? result,
                out string reason);

            Assert.False(built);
            Assert.Null(result);
            Assert.Contains("byte budget", reason, StringComparison.Ordinal);
            Assert.True(File.Exists(rawPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static VisualArtifactRecord WriteRaw(
        string root,
        string id,
        int width,
        int height,
        long relativeMilliseconds,
        string expectation)
    {
        int[] pixels = new int[width * height];
        for (int index = 0; index < pixels.Length; index++)
            pixels[index] = (index & 1) == 0 ? 0x00FF0000 : 0x0000FF00;
        byte[] png = VisualPngEncoder.Encode(width, height, pixels);
        var store = new VisualArtifactStore(root);
        VisualStoredArtifact stored = store.WriteImmutable(id, $"raw/{id}.png", png, 1_000_000);
        var rectangle = new VisualRect(0, 0, width, height);
        var artifact = new VisualArtifactRecord(
            id,
            id,
            VisualCheckpointPhase.AFTER_ACTION_SETTLED,
            stored.RelativePath,
            VisualEvidenceSchema.PngMimeType,
            stored.Sha256,
            stored.SizeBytes,
            width,
            height,
            DateTimeOffset.Parse("2026-09-01T00:00:00+00:00").AddMilliseconds(relativeMilliseconds),
            relativeMilliseconds + 1,
            relativeMilliseconds,
            VisualCaptureMethod.SYNTHETIC,
            VisualCaptureScopeKind.GUEST_WINDOW,
            VisualPrivacyClass.TEST_OWNED,
            rectangle,
            rectangle,
            96,
            "synthetic",
            null,
            expectation,
            false,
            null,
            1);
        artifact.Validate();
        return artifact;
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "tabdock-visual-sheet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
