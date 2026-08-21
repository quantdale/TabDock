using System;
using System.Collections.Generic;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Single authority for "this hide was issued by TabDock" provenance.
///
/// Every intentional guest hide (tab switch, split suspension, split
/// replacement, container minimize) funnels through
/// <see cref="WindowShepherdService.Hide"/>, which registers an expected-hide
/// operation here immediately after the native SW_HIDE is issued. When the
/// matching EVENT_OBJECT_HIDE is later dispatched to the UI thread,
/// <see cref="TryConsumeExpectedHide"/> proves the event belongs to that
/// TabDock operation and consumes it one-shot. Only a hide with no matching
/// expectation may be classified as guest-initiated.
///
/// Expectations bind to the capture generation (the per-capture identity
/// token), never to the raw HWND alone: a recycled HWND resolves to a new
/// captured object with a new token, so neither the old window's stale
/// expectation nor the new window's genuine self-hide can be confused.
/// Timestamps correlate delivery but never establish identity by themselves;
/// an unreadable timestamp fails closed (the expectation is not consumed).
///
/// All methods must be called on the UI thread, like every other consumer of
/// WinEvent-derived state in this application.
/// </summary>
public sealed class GuestHideProvenance
{
    /// <summary>
    /// Delivery tolerance for a posted hide event relative to its
    /// registration. Generous by design — a coalesced dispatcher under load
    /// can delay delivery — but bounded so an expectation whose event was
    /// lost (for example SW_HIDE on an already-hidden window produces no
    /// EVENT_OBJECT_HIDE) cannot suppress a much later genuine self-hide.
    /// </summary>
    public const int DefaultToleranceMilliseconds = 15_000;

    private readonly Dictionary<IntPtr, ExpectedHide> _expectedHides = new();
    private readonly int _toleranceMilliseconds;

    public GuestHideProvenance(int toleranceMilliseconds = DefaultToleranceMilliseconds)
    {
        _toleranceMilliseconds = toleranceMilliseconds > 0
            ? toleranceMilliseconds
            : DefaultToleranceMilliseconds;
    }

    private readonly record struct ExpectedHide(long CaptureToken, uint RegisteredAtEventTime, string Operation);

    /// <summary>
    /// Records that TabDock is about to hide (or has just natively hidden)
    /// this exact captured instance. A newer registration for the same HWND
    /// supersedes an older one.
    /// </summary>
    public void RegisterExpectedHide(CapturedWindow window, string operation, uint eventTime)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.WindowIdentityToken == 0)
            return; // Nothing to bind the expectation to; fail closed at consumption.
        if (string.IsNullOrEmpty(operation))
            operation = "presentation-operation";
        _expectedHides[window.Hwnd] = new ExpectedHide(window.WindowIdentityToken, eventTime, operation);
    }

    /// <summary>
    /// Consumes the expected hide for <paramref name="hwnd"/> when it provably
    /// belongs to <paramref name="member"/>'s capture generation and arrived
    /// within the delivery tolerance. One-shot: a consumed expectation can
    /// never authorize a second hide classification.
    /// </summary>
    public bool TryConsumeExpectedHide(IntPtr hwnd, CapturedWindow member, uint eventTime, out string operation)
    {
        ArgumentNullException.ThrowIfNull(member);
        operation = string.Empty;
        if (!_expectedHides.TryGetValue(hwnd, out ExpectedHide expected))
            return false;

        if (member.WindowIdentityToken == 0 || expected.CaptureToken != member.WindowIdentityToken)
        {
            // The HWND now belongs to a different capture generation. The
            // stale expectation is evidence about a dead instance only.
            _expectedHides.Remove(hwnd);
            return false;
        }

        if (!IsWithinTolerance(expected.RegisteredAtEventTime, eventTime))
        {
            _expectedHides.Remove(hwnd);
            return false;
        }

        _expectedHides.Remove(hwnd);
        operation = expected.Operation;
        return true;
    }

    /// <summary>True while an unconsumed expectation exists for this HWND.</summary>
    public bool HasExpectedHide(IntPtr hwnd) => _expectedHides.ContainsKey(hwnd);

    /// <summary>Drops any expectation for this HWND (release/destroy hygiene).</summary>
    public void ForgetWindow(IntPtr hwnd) => _expectedHides.Remove(hwnd);

    /// <summary>Drops all expectations (container teardown).</summary>
    public void Clear() => _expectedHides.Clear();

    private bool IsWithinTolerance(uint registeredAtEventTime, uint eventTime)
    {
        // USER32 supplies dwmsEventTime for real callbacks and both clocks are
        // system-uptime milliseconds, but a missing timestamp cannot prove
        // provenance: fail closed and let normal classification decide.
        if (eventTime == 0 || registeredAtEventTime == 0)
            return false;
        int delta = unchecked((int)(eventTime - registeredAtEventTime));
        return delta >= -_toleranceMilliseconds && delta <= _toleranceMilliseconds;
    }
}
