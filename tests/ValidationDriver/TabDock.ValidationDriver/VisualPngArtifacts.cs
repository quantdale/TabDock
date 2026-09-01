using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TabDock.ValidationDriver;

internal static class VisualPngEncoder
{
    private static readonly byte[] Signature =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    };

    public const int MaximumDimension = 16_384;
    public const long MaximumRawBytes = 128L * 1024 * 1024;

    public static byte[] Encode(int width, int height, ReadOnlySpan<int> pixels)
    {
        ValidateDimensions(width, height, pixels.Length);
        long rowBytes = checked((long)width * 4);
        long rawBytes = checked((rowBytes + 1) * height);
        if (rawBytes > MaximumRawBytes)
            throw new ArgumentOutOfRangeException(nameof(pixels), "PNG input exceeds the bounded raw-image budget.");

        if (rawBytes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(pixels), "PNG input is too large for one bounded buffer.");
        var scanlines = new byte[(int)rawBytes];
        int offset = 0;
        for (int y = 0; y < height; y++)
        {
            scanlines[offset++] = 0;
            ReadOnlySpan<int> row = pixels.Slice(y * width, width);
            foreach (int pixel in row)
            {
                scanlines[offset++] = (byte)((pixel >> 16) & 0xFF);
                scanlines[offset++] = (byte)((pixel >> 8) & 0xFF);
                scanlines[offset++] = (byte)(pixel & 0xFF);
                scanlines[offset++] = 0xFF;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(scanlines, 0, scanlines.Length);
        }

        using var png = new MemoryStream(checked(scanlines.Length + (int)compressed.Length + 128));
        png.Write(Signature, 0, Signature.Length);
        Span<byte> header = stackalloc byte[13];
        WriteUInt32(header, 0, checked((uint)width));
        WriteUInt32(header, 4, checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
        WriteChunk(png, "IEND", ReadOnlySpan<byte>.Empty);
        return png.ToArray();
    }
    public static (int Width, int Height, int[] Pixels) Decode(ReadOnlySpan<byte> png)
    {
        if (png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature))
            throw new ArgumentException("PNG signature is invalid.", nameof(png));

        int offset = Signature.Length;
        int width = 0;
        int height = 0;
        bool headerSeen = false;
        bool endSeen = false;
        using var compressed = new MemoryStream();
        while (offset < png.Length)
        {
            if (png.Length - offset < 12)
                throw new ArgumentException("PNG chunk header is truncated.", nameof(png));
            uint chunkLength = ReadUInt32(png, offset);
            offset += 4;
            if (chunkLength > int.MaxValue || chunkLength > png.Length - offset - 8)
                throw new ArgumentException("PNG chunk exceeds the bounded input.", nameof(png));
            ReadOnlySpan<byte> type = png.Slice(offset, 4);
            offset += 4;
            ReadOnlySpan<byte> data = png.Slice(offset, checked((int)chunkLength));
            offset += checked((int)chunkLength);
            uint expectedCrc = ReadUInt32(png, offset);
            offset += 4;
            if (Crc32(type, data) != expectedCrc)
                throw new ArgumentException("PNG chunk CRC is invalid.", nameof(png));

            if (type.SequenceEqual("IHDR"u8))
            {
                if (headerSeen || data.Length != 13)
                    throw new ArgumentException("PNG header is invalid.", nameof(png));
                width = checked((int)ReadUInt32(data, 0));
                height = checked((int)ReadUInt32(data, 4));
                if (data[8] != 8 || data[9] != 6 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    throw new ArgumentException("PNG must use bounded RGBA8 non-interlaced pixels.", nameof(png));
                ValidateDimensions(width, height, checked(width * height));
                headerSeen = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!headerSeen || endSeen)
                    throw new ArgumentException("PNG image data is out of order.", nameof(png));
                if (compressed.Length + data.Length > MaximumRawBytes)
                    throw new ArgumentException("PNG compressed data exceeds the bounded input.", nameof(png));
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!headerSeen || endSeen || data.Length != 0)
                    throw new ArgumentException("PNG end marker is invalid.", nameof(png));
                endSeen = true;
                break;
            }
        }

        if (!headerSeen || compressed.Length == 0 || !endSeen)
            throw new ArgumentException("PNG is incomplete.", nameof(png));
        long rawLength = checked(((long)width * 4 + 1) * height);
        if (rawLength > MaximumRawBytes || rawLength > int.MaxValue)
            throw new ArgumentException("PNG decoded data exceeds the bounded input.", nameof(png));
        byte[] raw = new byte[(int)rawLength];
        compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            zlib.ReadExactly(raw);
            if (zlib.ReadByte() != -1)
                throw new ArgumentException("PNG contains trailing decoded data.", nameof(png));
        }

        var pixels = new int[checked(width * height)];
        int rawOffset = 0;
        for (int y = 0; y < height; y++)
        {
            if (raw[rawOffset++] != 0)
                throw new ArgumentException("PNG uses an unsupported row filter.", nameof(png));
            for (int x = 0; x < width; x++)
            {
                int red = raw[rawOffset++];
                int green = raw[rawOffset++];
                int blue = raw[rawOffset++];
                rawOffset++;
                pixels[y * width + x] = (red << 16) | (green << 8) | blue;
            }
        }
        return (width, height, pixels);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> source, int offset)
        => ((uint)source[offset] << 24)
            | ((uint)source[offset + 1] << 16)
            | ((uint)source[offset + 2] << 8)
            | source[offset + 3];

    private static void ValidateDimensions(int width, int height, int pixelCount)
    {
        if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(width), "PNG dimensions are outside the bounded range.");
        if (checked((long)width * height) != pixelCount)
            throw new ArgumentException("pixel count does not match PNG dimensions.", nameof(pixelCount));
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        if (type.Length != 4)
            throw new ArgumentException("PNG chunk type must contain four ASCII characters.", nameof(type));
        Span<byte> length = stackalloc byte[4];
        WriteUInt32(length, 0, checked((uint)data.Length));
        output.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        typeBytes[0] = (byte)type[0];
        typeBytes[1] = (byte)type[1];
        typeBytes[2] = (byte)type[2];
        typeBytes[3] = (byte)type[3];
        output.Write(typeBytes);
        output.Write(data);

        uint crc = Crc32(typeBytes, data);
        WriteUInt32(length, 0, crc);
        output.Write(length);
    }

    private static void WriteUInt32(Span<byte> destination, int offset, uint value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFF_FFFF;
        foreach (byte value in type)
            crc = UpdateCrc(crc, value);
        foreach (byte value in data)
            crc = UpdateCrc(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB8_8320;
        return crc;
    }
}

internal sealed record VisualStoredArtifact(string RelativePath, string Sha256, long SizeBytes);

internal sealed class VisualArtifactStore
{
    private readonly VisualPathPolicy _paths;
    private readonly HashSet<string> _artifactIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _relativePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public VisualArtifactStore(string root)
    {
        _paths = new VisualPathPolicy(root);
    }

    public string Root => _paths.Root;

    public VisualStoredArtifact WriteRaw(
        string artifactId,
        string relativePath,
        VisualFrame frame,
        long maxBytes)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));
        byte[] png = VisualPngEncoder.Encode(frame.Width, frame.Height, frame.Pixels.Span);
        return WriteImmutable(artifactId, relativePath, png, maxBytes);
    }

    public VisualStoredArtifact WriteImmutable(
        string artifactId,
        string relativePath,
        ReadOnlySpan<byte> bytes,
        long maxBytes)
    {
        if (string.IsNullOrWhiteSpace(artifactId))
            throw new ArgumentException("artifact ID is required.", nameof(artifactId));
        if (bytes.Length == 0)
            throw new ArgumentException("artifact bytes cannot be empty.", nameof(bytes));
        if (maxBytes <= 0 || bytes.Length > maxBytes)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "artifact exceeds the configured byte budget.");

        string normalized = _paths.NormalizeRelative(relativePath);
        string fullPath = _paths.Resolve(normalized);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        lock (_sync)
        {
            if (!_artifactIds.Add(artifactId))
                throw new InvalidOperationException($"duplicate visual artifact ID '{artifactId}'.");
            if (!_relativePaths.Add(normalized))
            {
                _artifactIds.Remove(artifactId);
                throw new InvalidOperationException($"duplicate visual artifact path '{normalized}'.");
            }
            if (File.Exists(fullPath))
            {
                _artifactIds.Remove(artifactId);
                _relativePaths.Remove(normalized);
                throw new IOException($"visual artifact path already exists: '{normalized}'.");
            }

            string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    options: FileOptions.SequentialScan))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, fullPath);
                string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                return new VisualStoredArtifact(normalized, hash, bytes.Length);
            }
            catch
            {
                _artifactIds.Remove(artifactId);
                _relativePaths.Remove(normalized);
                TryDelete(temporaryPath);
                TryDelete(fullPath);
                throw;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The original exception is the useful failure; a best-effort temp
            // cleanup must never hide it.
        }
    }
}
