using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace TabDock.Services;

/// <summary>
/// Owns the per-Windows-user, cross-session TabDock writer guard.
///
/// The Global namespace is intentional: two sessions for the same user must
/// serialize access to the same AppData state and hidden-window journal. The
/// current user's SID is part of the name, so a different user receives a
/// different mutex and is not blocked. An explicit ACL grants full mutex
/// access only to that SID; relying on the default named-object ACL would make
/// the isolation depend on the creator token's policy.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private Mutex? _mutex;
    private bool _ownsMutex;

    /// <summary>Builds the stable cross-session name for a validated user SID.</summary>
    internal static string BuildMutexName(string userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid))
            throw new ArgumentException("A non-empty Windows user SID is required.", nameof(userSid));
        return $"Global\\TabDock-{userSid}";
    }

    /// <summary>
    /// Attempts to acquire the guard. Failure is fail-closed: the caller must
    /// exit before opening or changing persisted product state.
    /// </summary>
    public bool TryAcquire(out string? failure)
    {
        failure = null;
        try
        {
            string? userSid = WindowsIdentity.GetCurrent().User?.Value;
            if (string.IsNullOrWhiteSpace(userSid))
            {
                failure = "the current Windows user SID could not be resolved";
                return false;
            }

            var security = new MutexSecurity();
            security.AddAccessRule(new MutexAccessRule(
                new SecurityIdentifier(userSid),
                MutexRights.FullControl,
                AccessControlType.Allow));

            _mutex = MutexAcl.Create(
                initiallyOwned: true,
                name: BuildMutexName(userSid),
                createdNew: out bool createdNew,
                mutexSecurity: security);
            if (!createdNew)
            {
                failure = "another TabDock instance for this Windows user already owns the guard";
                _mutex.Dispose();
                _mutex = null;
                return false;
            }

            _ownsMutex = true;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"the per-user instance guard could not be acquired: {ex.GetType().Name}: {ex.Message}";
            Dispose();
            return false;
        }
    }

    public void Dispose()
    {
        Mutex? mutex = _mutex;
        _mutex = null;
        if (mutex == null)
            return;

        if (_ownsMutex)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The OS releases the named mutex when the process exits. Do
                // not allow a teardown race to mask the actual shutdown path.
            }
            finally
            {
                _ownsMutex = false;
            }
        }
        mutex.Dispose();
    }
}
