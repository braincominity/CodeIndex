using Microsoft.Data.Sqlite;

namespace CodeIndex.Tests;

internal static class SqlitePoolCleanup
{
    private static readonly object Gate = new();
    private static Action _clearAllPools = SqliteConnection.ClearAllPools;
    private static int _activeExclusiveOwners;
    private static bool _clearPending;

    internal static IDisposable EnterExclusiveOwner()
    {
        lock (Gate)
        {
            _activeExclusiveOwners++;
        }

        return new Lease();
    }

    internal static void ClearPoolsForWindowsFileRelease(bool callerOwnsExclusiveAccess = false)
    {
        if (!OperatingSystem.IsWindows())
            return;

        ClearPools(callerOwnsExclusiveAccess, deferForActiveOwners: true);
    }

    internal static void ClearPoolsAtCollectionBoundary()
    {
        ClearPools(callerOwnsExclusiveAccess: true, deferForActiveOwners: false);
    }

    internal static IDisposable ReplaceClearAllPoolsForTesting(Action clearAllPools)
    {
        ArgumentNullException.ThrowIfNull(clearAllPools);

        lock (Gate)
        {
            var prior = _clearAllPools;
            _clearAllPools = clearAllPools;
            return new RestoreClearAllPools(prior);
        }
    }

    private static void ClearPools(bool callerOwnsExclusiveAccess, bool deferForActiveOwners)
    {
        lock (Gate)
        {
            if (deferForActiveOwners && _activeExclusiveOwners > 0 && !callerOwnsExclusiveAccess)
            {
                _clearPending = true;
                return;
            }

            _clearPending = false;
            _clearAllPools();
        }
    }

    private sealed class Lease : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            lock (Gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _activeExclusiveOwners--;
                if (_activeExclusiveOwners == 0 && _clearPending)
                {
                    _clearPending = false;
                    _clearAllPools();
                }
            }
        }
    }

    private sealed class RestoreClearAllPools(Action prior) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            lock (Gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _clearAllPools = prior;
                _clearPending = false;
            }
        }
    }
}
