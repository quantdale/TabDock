using System;
using System.Threading;

namespace TabDock.Services;

internal static class ProductMutationLeaseSelfTest
{
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
}
