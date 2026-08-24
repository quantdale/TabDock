using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

public sealed class NativeInteractionReplayTests
{
    [Fact]
    public void LifecycleReplay_RejectsStaleIdentityAndReleasesOnDestroy()
    {
        NativeInteractionReplayResult result = NativeInteractionReplay.Run(
            new NativeInteractionReplayCase(
                new[] { "A", "B" },
                "A",
                new[]
                {
                    new NativeInteractionReplayEvent(NativeReplayEventKind.Foreground, "B"),
                    new NativeInteractionReplayEvent(NativeReplayEventKind.Hide, "B"),
                    new NativeInteractionReplayEvent(NativeReplayEventKind.Destroy, "B"),
                    new NativeInteractionReplayEvent(NativeReplayEventKind.Foreground, "B", NativeReplayIdentityResult.Mismatch),
                }));

        Assert.Equal(new[] { "A" }, result.Captured);
        Assert.Null(result.Foreground);
        Assert.Equal(4, result.Intents.Count);
        Assert.Contains(NativeReplayIntent.RefuseIdentity, result.Intents);
        Assert.Single(result.Refusals);
    }

    [Fact]
    public void LifecycleReplay_IgnoresForeignEvents()
    {
        NativeInteractionReplayResult result = NativeInteractionReplay.Run(
            new NativeInteractionReplayCase(
                new[] { "A" },
                "A",
                new[] { new NativeInteractionReplayEvent(NativeReplayEventKind.NameChange, "Foreign") }));

        Assert.Equal(new[] { "A" }, result.Captured);
        Assert.Equal(new[] { NativeReplayIntent.Ignore }, result.Intents);
        Assert.Empty(result.Refusals);
    }

    [Fact]
    public void DragReplay_DistinguishesInputGeometryAndProductCauses()
    {
        var valid = new[]
        {
            new NativeReplayDragPoint("TabDock", 10, 10, 1, true),
            new NativeReplayDragPoint("TabDock", 40, 10, 1, true),
        };
        Assert.Equal(NativeReplayDragDisposition.Valid,
            NativeInteractionReplay.ClassifyDrag(valid, "TabDock", 1, 1, 1).Disposition);
        Assert.Equal(NativeReplayDragDisposition.InputTargetNeverReached,
            NativeInteractionReplay.ClassifyDrag(
                new[] { new NativeReplayDragPoint("Foreign", 10, 10, 1, true) },
                "TabDock", 1, 0, 1).Disposition);
        Assert.Equal(NativeReplayDragDisposition.GeometryStale,
            NativeInteractionReplay.ClassifyDrag(
                new[] { new NativeReplayDragPoint("TabDock", 10, 10, 2, true) },
                "TabDock", 1, 0, 1).Disposition);
        Assert.Equal(NativeReplayDragDisposition.ProductIgnoredValidInput,
            NativeInteractionReplay.ClassifyDrag(valid, "TabDock", 1, 0, 1).Disposition);
        Assert.Equal(NativeReplayDragDisposition.ZeroDeltaPolyline,
            NativeInteractionReplay.ClassifyDrag(
                new[]
                {
                    new NativeReplayDragPoint("TabDock", 10, 10, 1, true),
                    new NativeReplayDragPoint("TabDock", 10, 10, 1, true),
                },
                "TabDock", 1, 0, 1).Disposition);
    }

    [Fact]
    public void SplitReplay_RequiresEveryPhaseToRetainItsRootIdentity()
    {
        var valid = new[]
        {
            new NativeReplaySplitPhase("container", "Container", "Container", NativeReplayIdentityResult.Match),
            new NativeReplaySplitPhase("guest-titlebar", "Guest", "Guest", NativeReplayIdentityResult.Match),
            new NativeReplaySplitPhase("content-host", "Guest", "Guest", NativeReplayIdentityResult.Match),
            new NativeReplaySplitPhase("release", "Container", "Container", NativeReplayIdentityResult.Match),
        };
        Assert.Equal(NativeReplaySplitDragDisposition.Valid,
            NativeInteractionReplay.ClassifySplitDrag(valid).Disposition);
        Assert.Equal(NativeReplaySplitDragDisposition.ForeignOrWrongTarget,
            NativeInteractionReplay.ClassifySplitDrag(
                new[] { valid[0], valid[1] with { ObservedRoot = "Foreign" } }).Disposition);
        Assert.Equal(NativeReplaySplitDragDisposition.IdentityChanged,
            NativeInteractionReplay.ClassifySplitDrag(
                new[] { valid[0] with { IdentityResult = NativeReplayIdentityResult.Unavailable } }).Disposition);
    }

    [Fact]
    public void InlineReplay_UsesFinalAuthoritativeIdentityInsteadOfEnumerationPosition()
    {
        NativeReplayInlineCaptureResult result = NativeInteractionReplay.ResolveInlineCapture(
            new[] { "C", "A", "B" }, "B", NativeReplayIdentityResult.Match);

        Assert.Equal(NativeReplayInlineCaptureDisposition.CapturedByIdentity, result.Disposition);
        Assert.Equal(2, result.AuthoritativeIndex);
        Assert.Equal("B", result.TargetIdentity);
    }

    [Fact]
    public void CheckedInFixtures_AreExecutableReplayContracts()
    {
        using JsonDocument lifecycle = Load("winevent-lifecycle.json");
        JsonElement lifecycleRoot = lifecycle.RootElement;
        NativeInteractionReplayEvent[] lifecycleEvents = lifecycleRoot
            .GetProperty("events")
            .EnumerateArray()
            .Select(element => new NativeInteractionReplayEvent(
                Enum.Parse<NativeReplayEventKind>(element.GetProperty("kind").GetString()!, ignoreCase: true),
                element.GetProperty("identity").GetString()!,
                Enum.Parse<NativeReplayIdentityResult>(element.GetProperty("identityResult").GetString()!, ignoreCase: true)))
            .ToArray();
        NativeInteractionReplayResult lifecycleResult = NativeInteractionReplay.Run(
            new NativeInteractionReplayCase(
                lifecycleRoot.GetProperty("initialCaptured").EnumerateArray().Select(value => value.GetString()!).ToArray(),
                lifecycleRoot.GetProperty("initialForeground").GetString(),
                lifecycleEvents));
        Assert.Equal(
            lifecycleRoot.GetProperty("expected").GetProperty("captured").EnumerateArray()
                .Select(value => value.GetString()!).ToArray(),
            lifecycleResult.Captured);
        Assert.Null(lifecycleResult.Foreground);
        Assert.Contains(NativeReplayIntent.RefuseIdentity, lifecycleResult.Intents);

        using JsonDocument drag = Load("dragreorder-h2.json");
        JsonElement dragRoot = drag.RootElement;
        NativeReplayDragPoint[] points = dragRoot.GetProperty("points")
            .EnumerateArray()
            .Select(element => new NativeReplayDragPoint(
                element.GetProperty("targetIdentity").GetString()!,
                element.GetProperty("x").GetInt32(),
                element.GetProperty("y").GetInt32(),
                element.GetProperty("geometryGeneration").GetInt32(),
                element.GetProperty("inputDelivered").GetBoolean()))
            .ToArray();
        Assert.Equal(NativeReplayDragDisposition.Valid,
            NativeInteractionReplay.ClassifyDrag(
                points,
                dragRoot.GetProperty("expectedTarget").GetString()!,
                dragRoot.GetProperty("initialGeometryGeneration").GetInt32(),
                productReorderCount: 1,
                expectedReorderCount: 1).Disposition);

        using JsonDocument split = Load("split-drag-release.json");
        NativeReplaySplitPhase[] phases = split.RootElement.GetProperty("phases")
            .EnumerateArray()
            .Select(element => new NativeReplaySplitPhase(
                element.GetProperty("name").GetString()!,
                element.GetProperty("expectedRoot").GetString()!,
                element.GetProperty("observedRoot").GetString()!,
                Enum.Parse<NativeReplayIdentityResult>(element.GetProperty("identityResult").GetString()!, ignoreCase: true)))
            .ToArray();
        Assert.Equal(NativeReplaySplitDragDisposition.Valid,
            NativeInteractionReplay.ClassifySplitDrag(phases).Disposition);

        using JsonDocument inline = Load("inline-capture-handoff.json");
        JsonElement inlineRoot = inline.RootElement;
        NativeReplayInlineCaptureResult inlineResult = NativeInteractionReplay.ResolveInlineCapture(
            inlineRoot.GetProperty("authoritativeFinalOrder").EnumerateArray().Select(value => value.GetString()!).ToArray(),
            inlineRoot.GetProperty("targetIdentity").GetString()!,
            Enum.Parse<NativeReplayIdentityResult>(inlineRoot.GetProperty("finalIdentityResult").GetString()!, ignoreCase: true));
        Assert.Equal(NativeReplayInlineCaptureDisposition.CapturedByIdentity, inlineResult.Disposition);
        Assert.Equal(
            inlineRoot.GetProperty("expectedResolution").GetProperty("authoritativeIndex").GetInt32(),
            inlineResult.AuthoritativeIndex);
    }

    private static JsonDocument Load(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "native-replay", fileName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
