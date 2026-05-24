namespace CodeIndex.Tests;

[CollectionDefinition("SQLite pool sensitive", DisableParallelization = true)]
public sealed class SqlitePoolSensitiveCollection : ICollectionFixture<SqlitePoolSensitiveFixture>
{
}

public sealed class SqlitePoolSensitiveFixture : IAsyncLifetime
{
    public Task InitializeAsync()
    {
        SqlitePoolCleanup.ClearPoolsAtCollectionBoundary();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqlitePoolCleanup.ClearPoolsAtCollectionBoundary();
        return Task.CompletedTask;
    }
}
