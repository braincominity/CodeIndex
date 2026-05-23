using Microsoft.Data.Sqlite;

namespace CodeIndex.Tests;

internal static class SqlitePoolCleanup
{
    private static readonly object Gate = new();
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

        lock (Gate)
        {
            if (_activeExclusiveOwners > 0 && !callerOwnsExclusiveAccess)
            {
                _clearPending = true;
                return;
            }

            SqliteConnection.ClearAllPools();
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
                    SqliteConnection.ClearAllPools();
                }
            }
        }
    }
}
