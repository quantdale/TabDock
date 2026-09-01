using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TabDock.ValidationDriver;

internal sealed record VisualContactSheetBuildResult(
    byte[] Png,
    IReadOnlyList<VisualArtifactRecord> IncludedArtifacts);

/// <summary>Builds a bounded derived overview without modifying raw PNG bytes.</summary>
internal static class VisualContactSheetBuilder
{
    private const int MaximumImages = 32;
    private const int MaximumThumbnailDimension = 384;
    private const int LabelHeight = 96;
    private const int CardPadding = 8;
    private const int CardGap = 8;
    private const int FontScale = 2;
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int GlyphAdvance = (GlyphWidth + 1) * FontScale;
    private const int LineHeight = (GlyphHeight + 1) * FontScale;

    public static bool TryBuild(
        string artifactRoot,
        IReadOnlyList<VisualArtifactRecord> artifacts,
        int maximumWidth,
        int maximumHeight,
        long maximumBytes,
        out VisualContactSheetBuildResult? result,
        out string reason)
    {
        result = null;
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(artifactRoot))
        {
            reason = "contact-sheet artifact root is required";
            return false;
        }
        if (artifacts == null || artifacts.Count == 0)
        {
            reason = "no raw visual artifacts were selected";
            return false;
        }
        if (maximumWidth <= 0 || maximumHeight <= 0 || maximumBytes <= 0)
        {
            reason = "contact-sheet bounds are invalid";
            return false;
        }

        VisualPathPolicy paths;
        try
        {
            paths = new VisualPathPolicy(artifactRoot);
        }
        catch (ArgumentException ex)
        {
            reason = ex.Message;
            return false;
        }

        VisualArtifactRecord[] ordered = artifacts
            .Where(artifact => !artifact.Derived)
            .OrderBy(artifact => artifact.RelativeMilliseconds)
            .ThenBy(artifact => artifact.Sequence)
            .ThenBy(artifact => artifact.ArtifactId, StringComparer.Ordinal)
            .Take(MaximumImages)
            .ToArray();
        if (ordered.Length == 0)
        {
            reason = "contact sheets require at least one raw visual artifact";
            return false;
        }

        var images = new List<(VisualArtifactRecord Artifact, int Width, int Height, int[] Pixels)>();
        try
        {
            foreach (VisualArtifactRecord artifact in ordered)
            {
                string normalized = paths.NormalizeRelative(artifact.RelativePath);
                string fullPath = paths.Resolve(normalized);
                if (!File.Exists(fullPath))
                {
                    reason = $"raw visual artifact is missing: {normalized}";
                    return false;
                }
                (int width, int height, int[] pixels) = VisualPngEncoder.Decode(File.ReadAllBytes(fullPath));
                if (width != artifact.Width || height != artifact.Height)
                {
                    reason = $"raw visual artifact dimensions disagree: {artifact.ArtifactId}";
                    return false;
                }
                images.Add((artifact, width, height, pixels));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or OverflowException)
        {
            reason = $"raw visual artifact could not be decoded: {ex.GetType().Name}";
            return false;
        }

        int columns = Math.Min(4, images.Count);
        int cardWidth = maximumWidth / columns;
        if (cardWidth < 64)
        {
            reason = "contact-sheet width budget is too small";
            return false;
        }
        cardWidth = Math.Min(640, cardWidth);
        int thumbnailWidth = Math.Max(1, cardWidth - (CardPadding * 2));
        int thumbnailHeight = Math.Min(MaximumThumbnailDimension, thumbnailWidth);
        int rows = (images.Count + columns - 1) / columns;
        int availableCardHeight = maximumHeight / rows;
        thumbnailHeight = Math.Min(thumbnailHeight, availableCardHeight - LabelHeight - CardGap);
        if (thumbnailHeight < 16)
        {
            reason = "contact-sheet height budget is too small";
            return false;
        }

        int cardHeight = checked(CardPadding + thumbnailHeight + CardGap + LabelHeight);
        int canvasWidth = checked(columns * cardWidth);
        int canvasHeight = checked(rows * cardHeight);
        if (canvasWidth > maximumWidth || canvasHeight > maximumHeight)
        {
            reason = "contact-sheet dimensions exceed the configured bounds";
            return false;
        }

        var canvas = new int[checked(canvasWidth * canvasHeight)];
        Fill(canvas, 0x00F6F8FA);
        for (int index = 0; index < images.Count; index++)
        {
            int column = index % columns;
            int row = index / columns;
            int cardLeft = column * cardWidth;
            int cardTop = row * cardHeight;
            FillRect(canvas, canvasWidth, cardLeft, cardTop, cardWidth, cardHeight, 0x00D7DEE7);
            FillRect(canvas, canvasWidth, cardLeft + 1, cardTop + 1, cardWidth - 2, cardHeight - 2, 0x00FFFFFF);

            (VisualArtifactRecord artifact, int sourceWidth, int sourceHeight, int[] pixels) = images[index];
            int imageWidth = sourceWidth;
            int imageHeight = sourceHeight;
            double scale = Math.Min((double)thumbnailWidth / sourceWidth, (double)thumbnailHeight / sourceHeight);
            imageWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero));
            imageHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero));
            int imageLeft = cardLeft + (cardWidth - imageWidth) / 2;
            int imageTop = cardTop + CardPadding + (thumbnailHeight - imageHeight) / 2;
            FillRect(canvas, canvasWidth, cardLeft + CardPadding, cardTop + CardPadding, thumbnailWidth, thumbnailHeight, 0x001E2935);
            DrawNearest(canvas, canvasWidth, imageLeft, imageTop, imageWidth, imageHeight, pixels, sourceWidth, sourceHeight);

            int labelTop = cardTop + CardPadding + thumbnailHeight + CardGap;
            int charactersPerLine = Math.Max(1, (cardWidth - (CardPadding * 2)) / GlyphAdvance);
            DrawLabel(canvas, canvasWidth, cardLeft + CardPadding, labelTop, charactersPerLine, artifact);
        }

        byte[] png = VisualPngEncoder.Encode(canvasWidth, canvasHeight, canvas);
        if (png.LongLength > maximumBytes)
        {
            reason = "contact-sheet exceeds the configured byte budget";
            return false;
        }
        result = new VisualContactSheetBuildResult(png, images.Select(item => item.Artifact).ToArray());
        return true;
    }

    private static void DrawNearest(
        int[] destination,
        int destinationWidth,
        int left,
        int top,
        int width,
        int height,
        int[] source,
        int sourceWidth,
        int sourceHeight)
    {
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / height);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / width);
                destination[(top + y) * destinationWidth + left + x] = source[sourceY * sourceWidth + sourceX];
            }
        }
    }

    private static void DrawLabel(
        int[] canvas,
        int canvasWidth,
        int left,
        int top,
        int charactersPerLine,
        VisualArtifactRecord artifact)
    {
        string[] labels =
        {
            artifact.CheckpointId,
            $"{artifact.Phase} +{artifact.RelativeMilliseconds}ms",
            artifact.ScopeKind.ToString(),
            TrimLabel(artifact.Expectation, charactersPerLine),
        };
        for (int line = 0; line < labels.Length; line++)
            DrawText(canvas, canvasWidth, left, top + line * LineHeight, labels[line], charactersPerLine);
    }

    private static string TrimLabel(string value, int maximumCharacters)
    {
        string normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= maximumCharacters)
            return normalized;
        if (maximumCharacters <= 3)
            return normalized[..maximumCharacters];
        return normalized[..(maximumCharacters - 3)] + "...";
    }

    private static void DrawText(
        int[] canvas,
        int canvasWidth,
        int left,
        int top,
        string text,
        int maximumCharacters)
    {
        string upper = text.ToUpperInvariant();
        int count = Math.Min(upper.Length, maximumCharacters);
        for (int index = 0; index < count; index++)
        {
            DrawGlyph(canvas, canvasWidth, left + index * GlyphAdvance, top, upper[index]);
        }
    }

    private static void DrawGlyph(int[] canvas, int canvasWidth, int left, int top, char character)
    {
        string[] glyph = Glyphs.TryGetValue(character, out string[]? value)
            ? value
            : Glyphs['?'];
        for (int y = 0; y < glyph.Length; y++)
        {
            for (int x = 0; x < glyph[y].Length; x++)
            {
                if (glyph[y][x] != '#')
                    continue;
                for (int sy = 0; sy < FontScale; sy++)
                {
                    for (int sx = 0; sx < FontScale; sx++)
                    {
                        int targetX = left + x * FontScale + sx;
                        int targetY = top + y * FontScale + sy;
                        if ((uint)targetX < (uint)canvasWidth && (uint)targetY < (uint)(canvas.Length / canvasWidth))
                            canvas[targetY * canvasWidth + targetX] = 0x001C2733;
                    }
                }
            }
        }
    }

    private static void Fill(int[] pixels, int color)
    {
        Array.Fill(pixels, color);
    }

    private static void FillRect(int[] pixels, int width, int left, int top, int rectWidth, int rectHeight, int color)
    {
        for (int y = top; y < top + rectHeight; y++)
            Array.Fill(pixels, color, y * width + left, rectWidth);
    }

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        [' '] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." },
        ['.'] = new[] { ".....", ".....", ".....", ".....", ".....", "..##.", "..##." },
        [','] = new[] { ".....", ".....", ".....", ".....", ".....", "..##.", ".##.." },
        [':'] = new[] { ".....", "..##.", "..##.", ".....", "..##.", "..##.", "....." },
        ['-'] = new[] { ".....", ".....", ".....", ".###.", ".....", ".....", "....." },
        ['_'] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", ".###." },
        ['/'] = new[] { "....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#...." },
        ['+'] = new[] { ".....", "..#..", "..#..", ".###.", "..#..", "..#..", "....." },
        ['?'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." },
        ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
        ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
        ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
        ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
        ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
        ['5'] = new[] { "#####", "#....", "#....", "####.", "....#", "....#", "####." },
        ['6'] = new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." },
        ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
        ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
        ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." },
        ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
        ['C'] = new[] { ".####", "#....", "#....", "#....", "#....", "#....", ".####" },
        ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
        ['G'] = new[] { ".####", "#....", "#....", "#..##", "#...#", "#...#", ".####" },
        ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
        ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
        ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
        ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
        ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
        ['N'] = new[] { "#...#", "##..#", "##..#", "#.#.#", "#..##", "#..##", "#...#" },
        ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
        ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
        ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
        ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
        ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
        ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" },
        ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
        ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
        ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
    };
}
