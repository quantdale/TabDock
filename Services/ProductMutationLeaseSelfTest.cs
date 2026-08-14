using System;
using System.Threading;

namespace TabDock.Services;

internal static class ProductMutationLeaseSelfTest
{
    internal static bool UserScopedNameRules()
    {
        const string userA = "S-1-5-21-111111111-222222222-333333333-1001";
        const string userB = "S-1-5-21-111111111-222222222-333333333-1002";
        bool sameUserStable = ProductMutationLease.TryBuildUserScopedName(userA, out string first)
            && ProductMutationLease.TryBuildUserScopedName(userA, out string second)
            && string.Equals(first, second, StringComparison.Ordinal);
        bool differentUsersSeparate = ProductMutationLease.TryBuildUserScopedName(userB, out string other)
            && !string.Equals(first, other, StringComparison.Ordinal);
        bool unsafeRejected = !ProductMutationLease.TryBuildUserScopedName(
                "S-1-5-21-evil\\Global\\TabDock",
                out _)
            && !ProductMutationLease.TryBuildUserScopedName("not-a-sid", out _)
            && !ProductMutationLease.TryBuildUserScopedName("S-1-5-21-\u001B", out _);
        return sameUserStable && differentUsersSeparate && unsafeRejected;
    }

    internal static bool ExclusiveAndReusable()
    {
        string name = @"Local\TabDock-lease-selftest-" + Guid.NewGuid().ToString("N");
        if (!ProductMutationLease.TryAcquire(out ProductMutationLease? first, name))
            return false;
        using (first)
        {
            bool secondAcquired = false;
            Thread secondThread = new(() =>
            {
                if (ProductMutationLease.TryAcquire(out ProductMutationLease? second, name))
                {
                    secondAcquired = true;
                    second?.Dispose();
                }
            });
            secondThread.Start();
            secondThread.Join();
            if (secondAcquired)
                return false;
        }

        if (!ProductMutationLease.TryAcquire(out ProductMutationLease? afterRelease, name))
            return false;
        afterRelease!.Dispose();

        using var ownerReady = new ManualResetEventSlim(false);
        Thread abandonedOwner = new(() =>
        {
            var mutex = new Mutex(initiallyOwned: false, name);
            mutex.WaitOne();
            ownerReady.Set();
            // Deliberately do not release or dispose: Windows abandons the
            // mutex when this owner thread exits, and the next waiter must
            // recover ownership through AbandonedMutexException.
        });
        abandonedOwner.Start();
        if (!ownerReady.Wait(TimeSpan.FromSeconds(2)))
            return false;
        abandonedOwner.Join();

        if (!ProductMutationLease.TryAcquire(out ProductMutationLease? recovered, name))
            return false;
        recovered!.Dispose();
        return true;
    }

    internal static bool DifferentUserScopedLeasesCanCoexist()
    {
        const string userA = "S-1-5-21-111111111-222222222-333333333-2001";
        const string userB = "S-1-5-21-111111111-222222222-333333333-2002";
        if (!ProductMutationLease.TryBuildUserScopedName(userA, out string nameA)
            || !ProductMutationLease.TryBuildUserScopedName(userB, out string nameB))
        {
            return false;
        }

        if (!ProductMutationLease.TryAcquire(out ProductMutationLease? leaseA, nameA))
            return false;
        using (leaseA)
        {
            if (!ProductMutationLease.TryAcquire(out ProductMutationLease? leaseB, nameB))
                return false;
            leaseB!.Dispose();
        }
        return true;
    }
}
