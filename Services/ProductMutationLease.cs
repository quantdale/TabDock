using System;
using System.Threading;

namespace TabDock.Services;

/// <summary>
/// Owns the one cross-process lease for product-mutating TabDock state.
/// Read-only diagnostics intentionally do not acquire it.
/// </summary>
internal sealed class ProductMutationLease : IDisposable
{
    internal const string DefaultName = @"Global\TabDock";

    private readonly Mutex _mutex;
    private bool _ownsMutex;
    private bool _disposed;

    private ProductMutationLease(Mutex mutex)
    {
        _mutex = mutex;
        _ownsMutex = true;
    }

    internal static bool TryAcquire(out ProductMutationLease? lease, string? name = null)
    {
        lease = null;
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name ?? DefaultName);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // Windows transfers ownership to this waiter. The abandoned
                // owner cannot still be mutating product state.
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return false;
            }

            lease = new ProductMutationLease(mutex);
            mutex = null;
            return true;
        }
        catch
        {
            mutex?.Dispose();
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            catch (ObjectDisposedException) { }
            _ownsMutex = false;
        }
        _mutex.Dispose();
    }
}
