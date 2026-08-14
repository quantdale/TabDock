using System;
using System.Linq;
using System.Security.Principal;
using System.Threading;

namespace TabDock.Services;

/// <summary>
/// Owns the one cross-process lease for product-mutating TabDock state.
/// Read-only diagnostics intentionally do not acquire it.
/// </summary>
internal sealed class ProductMutationLease : IDisposable
{
    internal const string NamePrefix = @"Global\TabDock-";
    private const int MaximumNameLength = 240;

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
            string mutexName;
            if (string.IsNullOrWhiteSpace(name))
            {
                if (!TryGetCurrentUserName(out mutexName))
                    return false;
            }
            else
            {
                mutexName = name;
            }

            mutex = new Mutex(initiallyOwned: false, mutexName);
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

    internal static bool TryGetCurrentUserName(out string name)
    {
        name = string.Empty;
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier? sid = identity.User;
            return sid != null && TryBuildUserScopedName(sid.Value, out name);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryBuildUserScopedName(string? sidText, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(sidText))
            return false;

        string trimmed = sidText.Trim();
        if (trimmed.Length > MaximumNameLength - NamePrefix.Length
            || trimmed.Any(char.IsControl)
            || trimmed.Contains('\\')
            || trimmed.Contains('/'))
        {
            return false;
        }

        try
        {
            string canonicalSid = new SecurityIdentifier(trimmed).Value;
            name = NamePrefix + canonicalSid;
            return name.Length <= MaximumNameLength;
        }
        catch
        {
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
