using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class VisualPngArtifactTests
{
    [Fact]
    public void Encoder_PreservesDimensionsAndBgraChannelMeaning()
    {
        int[] pixels =
        {
            unchecked((int)0x00FF0000), 0x0000FF00,
            0x000000FF, 0x00000000,
        };

        byte[] png = VisualPngEncoder.Encode(2, 2, pixels);
        (int width, int height, byte[] scanlines) = DecodePng(png);

        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal(new byte[]
        {
            0, 255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 0, 255, 255, 0, 0, 0, 255,
        }, scanlines);
    }

    [Fact]
    public void Encoder_RejectsMismatchedAndUnboundedInput()
    {
        Assert.Throws<ArgumentException>(() => VisualPngEncoder.Encode(2, 2, new[] { 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualPngEncoder.Encode(
            VisualPngEncoder.MaximumDimension + 1, 1, new int[VisualPngEncoder.MaximumDimension + 1]));
    }

    [Fact]
    public void Store_WritesHashBoundAtomicRawArtifactAndRejectsDuplicates()
    {
        string root = Path.Combine(Path.GetTempPath(), "tabdock-visual-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new VisualArtifactStore(root);
            byte[] first = { 1, 2, 3, 4 };
            VisualStoredArtifact saved = store.WriteImmutable("frame-1", "visual/frame.bin", first, 1024);
            string fullPath = Path.Combine(root, "visual", "frame.bin");

            Assert.True(File.Exists(fullPath));
            Assert.Equal(first.Length, saved.SizeBytes);
            Assert.Throws<InvalidOperationException>(() => store.WriteImmutable("frame-2", "visual/frame.bin", new byte[] { 8 }, 1024));
            Assert.Equal(first, File.ReadAllBytes(fullPath));
            Assert.Throws<InvalidOperationException>(() => store.WriteImmutable("frame-1", "visual/other.bin", first, 1024));
            Assert.Equal(first, File.ReadAllBytes(fullPath));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Store_RejectsUnsafePathBeforeCreatingPartialArtifacts()
    {
        string root = Path.Combine(Path.GetTempPath(), "tabdock-visual-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new VisualArtifactStore(root);
            Assert.Throws<ArgumentException>(() => store.WriteImmutable("frame-1", "../outside.bin", new byte[] { 1 }, 1024));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Store_CleansTemporaryFileWhenAtomicRenameFails()
    {
        string root = Path.Combine(Path.GetTempPath(), "tabdock-visual-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            string blockedPath = Path.Combine(root, "visual", "blocked.bin");
            Directory.CreateDirectory(blockedPath);
            var store = new VisualArtifactStore(root);

            Assert.ThrowsAny<IOException>(() => store.WriteImmutable(
                "frame-1", "visual/blocked.bin", new byte[] { 1, 2, 3 }, 1024));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            Assert.True(Directory.Exists(blockedPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static (int Width, int Height, byte[] Scanlines) DecodePng(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());
        int offset = 8;
        int width = 0;
        int height = 0;
        using var compressed = new MemoryStream();
        while (offset < png.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(offset + 8, length);
            if (type == "IHDR")
            {
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[..4]));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4)));
                Assert.Equal(8, data[8]);
                Assert.Equal(6, data[9]);
            }
            else if (type == "IDAT")
            {
                compressed.Write(data);
            }
            offset += 12 + length;
            if (type == "IEND")
                break;
        }

        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return (width, height, output.ToArray());
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
