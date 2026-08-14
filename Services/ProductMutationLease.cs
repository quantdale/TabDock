using System;
using System.Linq;
using System.Security.AccessControl;
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
    internal const MutexRights RequiredMutexRights =
        MutexRights.Synchronize | MutexRights.Modify | MutexRights.ReadPermissions;

    private const int MaximumNameLength = 240;
    private static readonly IProductMutationLeasePlatform DefaultPlatform =
        new WindowsProductMutationLeasePlatform();

    private readonly Mutex _mutex;
    private bool _ownsMutex;
    private bool _disposed;

    private ProductMutationLease(Mutex mutex)
    {
        _mutex = mutex;
        _ownsMutex = true;
    }

    internal static bool TryAcquire(
        out ProductMutationLease? lease,
        string? name = null,
        IProductMutationLeasePlatform? platform = null,
        Func<SecurityIdentifier?>? currentUserProvider = null,
        Func<SecurityIdentifier, MutexSecurity?>? securityFactory = null)
    {
        lease = null;
        Mutex? mutex = null;
        try
        {
            SecurityIdentifier? currentUserSid = currentUserProvider != null
                ? currentUserProvider()
                : GetCurrentUserSid();
            if (currentUserSid == null)
                return false;

            string mutexName;
            if (string.IsNullOrWhiteSpace(name))
            {
                if (!TryBuildUserScopedName(currentUserSid.Value, out mutexName))
                    return false;
            }
            else
            {
                mutexName = name;
            }

            MutexSecurity? security = securityFactory != null
                ? securityFactory(currentUserSid)
                : BuildMutexSecurity(currentUserSid);
            if (security == null)
                return false;

            IProductMutationLeasePlatform aclPlatform = platform ?? DefaultPlatform;
            if (!aclPlatform.TryOpenExisting(mutexName, RequiredMutexRights, out mutex)
                || mutex == null)
            {
                try
                {
                    mutex = aclPlatform.Create(
                        initiallyOwned: false,
                        mutexName,
                        out _,
                        security);
                }
                catch
                {
                    // A legitimate same-user creator may win a create race;
                    // retry only through the secured OpenExisting path. Never
                    // fall back to the default broad-DACL Mutex constructor.
                    mutex?.Dispose();
                    mutex = null;
                    if (!aclPlatform.TryOpenExisting(mutexName, RequiredMutexRights, out mutex)
                        || mutex == null)
                    {
                        return false;
                    }
                }
            }

            if (!aclPlatform.HasExpectedSecurity(mutex, currentUserSid))
            {
                mutex.Dispose();
                mutex = null;
                return false;
            }

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
            SecurityIdentifier? sid = GetCurrentUserSid();
            return sid != null && TryBuildUserScopedName(sid.Value, out name);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryBuildMutexSecurity(
        SecurityIdentifier? currentUserSid,
        out MutexSecurity? security)
    {
        security = BuildMutexSecurity(currentUserSid);
        return security != null;
    }

    private static SecurityIdentifier? GetCurrentUserSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User;
    }

    private static MutexSecurity? BuildMutexSecurity(SecurityIdentifier? currentUserSid)
    {
        if (currentUserSid == null)
            return null;

        try
        {
            var security = new MutexSecurity();
            security.SetOwner(currentUserSid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetAccessRule(new MutexAccessRule(
                currentUserSid,
                RequiredMutexRights,
                AccessControlType.Allow));
            return security;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasExpectedSecurity(
        MutexSecurity security,
        SecurityIdentifier currentUserSid)
    {
        try
        {
            IdentityReference? owner = security.GetOwner(typeof(SecurityIdentifier));
            if (owner is not SecurityIdentifier ownerSid
                || !ownerSid.Equals(currentUserSid))
            {
                return false;
            }

            if (!security.AreAccessRulesProtected)
                return false;

            int allowRuleCount = 0;
            foreach (MutexAccessRule rule in security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier)))
            {
                if (rule.IsInherited
                    || rule.AccessControlType != AccessControlType.Allow
                    || rule.IdentityReference is not SecurityIdentifier ruleSid
                    || !ruleSid.Equals(currentUserSid)
                    || rule.MutexRights != RequiredMutexRights)
                {
                    return false;
                }

                allowRuleCount++;
            }

            return allowRuleCount == 1;
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

    internal interface IProductMutationLeasePlatform
    {
        bool TryOpenExisting(string name, MutexRights rights, out Mutex? mutex);

        Mutex Create(
            bool initiallyOwned,
            string name,
            out bool createdNew,
            MutexSecurity security);

        bool HasExpectedSecurity(Mutex mutex, SecurityIdentifier currentUserSid);
    }

    private sealed class WindowsProductMutationLeasePlatform : IProductMutationLeasePlatform
    {
        public bool TryOpenExisting(string name, MutexRights rights, out Mutex? mutex) =>
            MutexAcl.TryOpenExisting(name, rights, out mutex);

        public Mutex Create(
            bool initiallyOwned,
            string name,
            out bool createdNew,
            MutexSecurity security) =>
            MutexAcl.Create(initiallyOwned, name, out createdNew, security);

        public bool HasExpectedSecurity(Mutex mutex, SecurityIdentifier currentUserSid)
        {
            MutexSecurity security = mutex.GetAccessControl();
            return ProductMutationLease.HasExpectedSecurity(security, currentUserSid);
        }
    }
}
