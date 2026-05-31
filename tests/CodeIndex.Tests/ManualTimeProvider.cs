namespace CodeIndex.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    internal static readonly DateTimeOffset FixtureUtcNow = DateTimeOffset.UnixEpoch.AddDays(20_000);
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow.ToUniversalTime();
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();
}
