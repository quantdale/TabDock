using System;
using System.Diagnostics;
using System.Threading;

namespace TabDock.ValidationDriver;

/// <summary>Bounded wait result with the last observed state for evidence.</summary>
internal readonly record struct ScenarioWaitResult(
    bool Succeeded,
    long ElapsedMilliseconds,
    int Iterations,
    string LastObserved,
    string? LastError)
{
    public bool TimedOut => !Succeeded && LastError == null;
}

/// <summary>
/// Shared condition wait. It uses a monotonic clock, explicit deadlines, and
/// an optional state description so timeout evidence says what was observed.
/// The clock/delay seams make the wait contract deterministic without sleeps.
/// </summary>
internal static class ScenarioWait
{
    public static ScenarioWaitResult Until(
        Func<bool> condition,
        int timeoutMilliseconds,
        int pollMilliseconds = 50,
        Func<string>? describe = null,
        Action<ScenarioWaitResult>? onTimeout = null)
        => Until(
            condition,
            timeoutMilliseconds,
            pollMilliseconds,
            describe,
            onTimeout,
            Stopwatch.GetTimestamp,
            Thread.Sleep,
            honorCancellation: true);

    internal static ScenarioWaitResult Until(
        Func<bool> condition,
        int timeoutMilliseconds,
        int pollMilliseconds,
        Func<string>? describe,
        Action<ScenarioWaitResult>? onTimeout,
        Func<long> timestamp,
        Action<int> delay,
        bool honorCancellation)
    {
        if (condition == null)
            throw new ArgumentNullException(nameof(condition));
        if (timeoutMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        if (pollMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(pollMilliseconds));

        long start = timestamp();
        long deadline = start + ToTimestampUnits(timeoutMilliseconds);
        int iterations = 0;
        string lastObserved = "unobserved";
        string? lastError = null;

        while (true)
        {
            if (honorCancellation)
                Util.ThrowIfCancelled();

            iterations++;
            try
            {
                if (condition())
                {
                    ScenarioWaitResult success = new(
                        true,
                        ElapsedMilliseconds(start, timestamp()),
                        iterations,
                        Describe(describe, ref lastObserved),
                        null);
                    return success;
                }
                lastError = null;
            }
            catch (Exception ex)
            {
                lastError = ex.GetType().Name;
            }

            lastObserved = Describe(describe, ref lastObserved);
            long now = timestamp();
            if (now >= deadline)
            {
                ScenarioWaitResult timedOut = new(
                    false,
                    ElapsedMilliseconds(start, now),
                    iterations,
                    lastObserved,
                    lastError);
                onTimeout?.Invoke(timedOut);
                return timedOut;
            }

            long remainingMs = Math.Max(1, ElapsedMilliseconds(now, deadline));
            delay((int)Math.Min(pollMilliseconds, remainingMs));
        }
    }

    private static string Describe(Func<string>? describe, ref string previous)
    {
        if (describe == null)
            return previous;
        try
        {
            previous = describe() ?? "<null>";
        }
        catch (Exception ex)
        {
            previous = $"describe-error:{ex.GetType().Name}";
        }
        return previous;
    }

    private static long ToTimestampUnits(long milliseconds)
        => (long)Math.Ceiling(milliseconds * (double)Stopwatch.Frequency / 1000d);

    private static long ElapsedMilliseconds(long start, long end)
        => Math.Max(0, (long)Math.Floor((end - start) * 1000d / Stopwatch.Frequency));
}
