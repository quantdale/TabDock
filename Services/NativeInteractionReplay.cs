using System;
using System.Collections.Generic;
using System.Linq;

namespace TabDock.Services;

internal enum NativeReplayEventKind
{
    Foreground,
    Reorder,
    Hide,
    Show,
    NameChange,
    Minimize,
    Destroy,
}

internal enum NativeReplayIdentityResult
{
    Match,
    Mismatch,
    Unavailable,
}

internal enum NativeReplayIntent
{
    Ignore,
    DispatchForeground,
    DispatchZOrder,
    ApplyVisibilityRepair,
    RefreshName,
    ApplyMinimizeRepair,
    ReleaseMember,
    RefuseIdentity,
}

internal readonly record struct NativeInteractionReplayEvent(
    NativeReplayEventKind Kind,
    string Identity,
    NativeReplayIdentityResult IdentityResult = NativeReplayIdentityResult.Match,
    string? RelatedIdentity = null);

internal sealed record NativeInteractionReplayCase(
    IReadOnlyList<string> InitialCaptured,
    string? InitialForeground,
    IReadOnlyList<NativeInteractionReplayEvent> Events);

internal sealed record NativeInteractionReplayResult(
    IReadOnlyList<string> Captured,
    string? Foreground,
    IReadOnlyDictionary<string, bool> Visibility,
    IReadOnlyList<NativeReplayIntent> Intents,
    IReadOnlyList<string> Refusals);

/// <summary>
/// Replays only the native-event policy/state transition. It never creates an
/// HWND and never calls USER32, so a recorded physical sequence can become a
/// deterministic regression without pretending to be an operating system.
/// </summary>
internal static class NativeInteractionReplay
{
    public static NativeInteractionReplayResult Run(NativeInteractionReplayCase replay)
    {
        var captured = new HashSet<string>(replay.InitialCaptured, StringComparer.Ordinal);
        var visibility = captured.ToDictionary(identity => identity, _ => true, StringComparer.Ordinal);
        var intents = new List<NativeReplayIntent>();
        var refusals = new List<string>();
        string? foreground = replay.InitialForeground;

        foreach (NativeInteractionReplayEvent current in replay.Events)
        {
            if (current.IdentityResult != NativeReplayIdentityResult.Match)
            {
                intents.Add(NativeReplayIntent.RefuseIdentity);
                refusals.Add($"{current.Kind}:{current.Identity}:{current.IdentityResult}");
                continue;
            }

            string target = current.Kind == NativeReplayEventKind.Reorder
                ? current.RelatedIdentity ?? current.Identity
                : current.Identity;
            if (!captured.Contains(target))
            {
                intents.Add(NativeReplayIntent.Ignore);
                continue;
            }

            switch (current.Kind)
            {
                case NativeReplayEventKind.Foreground:
                    foreground = target;
                    intents.Add(NativeReplayIntent.DispatchForeground);
                    break;
                case NativeReplayEventKind.Reorder:
                    foreground = target;
                    intents.Add(NativeReplayIntent.DispatchZOrder);
                    break;
                case NativeReplayEventKind.Hide:
                    visibility[target] = false;
                    intents.Add(NativeReplayIntent.ApplyVisibilityRepair);
                    break;
                case NativeReplayEventKind.Show:
                    visibility[target] = true;
                    intents.Add(NativeReplayIntent.ApplyVisibilityRepair);
                    break;
                case NativeReplayEventKind.NameChange:
                    intents.Add(NativeReplayIntent.RefreshName);
                    break;
                case NativeReplayEventKind.Minimize:
                    intents.Add(NativeReplayIntent.ApplyMinimizeRepair);
                    break;
                case NativeReplayEventKind.Destroy:
                    captured.Remove(target);
                    visibility.Remove(target);
                    if (string.Equals(foreground, target, StringComparison.Ordinal))
                        foreground = null;
                    intents.Add(NativeReplayIntent.ReleaseMember);
                    break;
            }
        }

        return new NativeInteractionReplayResult(
            captured.OrderBy(identity => identity, StringComparer.Ordinal).ToArray(),
            foreground,
            new Dictionary<string, bool>(visibility, StringComparer.Ordinal),
            intents,
            refusals);
    }

    public static NativeReplayDragResult ClassifyDrag(
        IReadOnlyList<NativeReplayDragPoint> points,
        string expectedTarget,
        int initialGeometryGeneration,
        int productReorderCount,
        int expectedReorderCount)
    {
        if (points.Count == 0
            || points.Any(point => !point.InputDelivered
                || !string.Equals(point.TargetIdentity, expectedTarget, StringComparison.Ordinal)))
        {
            return new NativeReplayDragResult(
                NativeReplayDragDisposition.InputTargetNeverReached,
                "at least one drag sample was not delivered to the expected test-owned root");
        }

        if (points.Any(point => point.GeometryGeneration != initialGeometryGeneration))
        {
            return new NativeReplayDragResult(
                NativeReplayDragDisposition.GeometryStale,
                "drag samples crossed a geometry generation boundary after the snapshot");
        }

        if (points.All(point => point.X == points[0].X && point.Y == points[0].Y))
        {
            return new NativeReplayDragResult(
                NativeReplayDragDisposition.ZeroDeltaPolyline,
                "all delivered drag samples have zero displacement");
        }

        if (productReorderCount == 0 && expectedReorderCount > 0)
        {
            return new NativeReplayDragResult(
                NativeReplayDragDisposition.ProductIgnoredValidInput,
                "valid, identity-current drag samples produced no expected reorder");
        }

        return new NativeReplayDragResult(NativeReplayDragDisposition.Valid, "ok");
    }

    public static NativeReplaySplitDragResult ClassifySplitDrag(
        IReadOnlyList<NativeReplaySplitPhase> phases)
    {
        foreach (NativeReplaySplitPhase phase in phases)
        {
            if (phase.IdentityResult != NativeReplayIdentityResult.Match)
            {
                return new NativeReplaySplitDragResult(
                    NativeReplaySplitDragDisposition.IdentityChanged,
                    $"{phase.Name}: identity {phase.IdentityResult}");
            }
            if (!string.Equals(phase.ExpectedRoot, phase.ObservedRoot, StringComparison.Ordinal))
            {
                return new NativeReplaySplitDragResult(
                    NativeReplaySplitDragDisposition.ForeignOrWrongTarget,
                    $"{phase.Name}: expected {phase.ExpectedRoot}, observed {phase.ObservedRoot}");
            }
        }

        return new NativeReplaySplitDragResult(
            NativeReplaySplitDragDisposition.Valid,
            "container, guest titlebar, content-host, and release roots remained identity-current");
    }

    public static NativeReplayInlineCaptureResult ResolveInlineCapture(
        IReadOnlyList<string> authoritativeOrder,
        string targetIdentity,
        NativeReplayIdentityResult finalIdentity)
    {
        if (finalIdentity != NativeReplayIdentityResult.Match)
        {
            return new NativeReplayInlineCaptureResult(
                NativeReplayInlineCaptureDisposition.IdentityChanged,
                -1,
                null,
                $"target identity is {finalIdentity}");
        }

        int index = -1;
        for (int i = 0; i < authoritativeOrder.Count; i++)
        {
            if (string.Equals(authoritativeOrder[i], targetIdentity, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            return new NativeReplayInlineCaptureResult(
                NativeReplayInlineCaptureDisposition.TargetNotCurrent,
                -1,
                null,
                "target is absent from authoritative final order");
        }

        return new NativeReplayInlineCaptureResult(
            NativeReplayInlineCaptureDisposition.CapturedByIdentity,
            index,
            targetIdentity,
            "final handoff resolved by identity");
    }
}

internal readonly record struct NativeReplayDragPoint(
    string TargetIdentity,
    int X,
    int Y,
    int GeometryGeneration,
    bool InputDelivered);

internal enum NativeReplayDragDisposition
{
    Valid,
    InputTargetNeverReached,
    GeometryStale,
    ProductIgnoredValidInput,
    ZeroDeltaPolyline,
}

internal readonly record struct NativeReplayDragResult(
    NativeReplayDragDisposition Disposition,
    string Reason);

internal readonly record struct NativeReplaySplitPhase(
    string Name,
    string ExpectedRoot,
    string ObservedRoot,
    NativeReplayIdentityResult IdentityResult);

internal enum NativeReplaySplitDragDisposition
{
    Valid,
    IdentityChanged,
    ForeignOrWrongTarget,
}

internal readonly record struct NativeReplaySplitDragResult(
    NativeReplaySplitDragDisposition Disposition,
    string Reason);

internal enum NativeReplayInlineCaptureDisposition
{
    CapturedByIdentity,
    TargetNotCurrent,
    IdentityChanged,
}

internal readonly record struct NativeReplayInlineCaptureResult(
    NativeReplayInlineCaptureDisposition Disposition,
    int AuthoritativeIndex,
    string? TargetIdentity,
    string Reason);
