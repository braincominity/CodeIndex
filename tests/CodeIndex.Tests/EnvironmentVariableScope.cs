namespace CodeIndex.Tests;

internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly Dictionary<string, string?> _originalValues;
    private bool _disposed;

    private EnvironmentVariableScope(IEnumerable<string> names)
    {
        _originalValues = names
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
    }

    internal static EnvironmentVariableScope Capture(params string[] names) => new(names);

    internal void Set(string name, string? value)
    {
        ThrowIfDisposed();
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var (name, value) in _originalValues)
            Environment.SetEnvironmentVariable(name, value);

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EnvironmentVariableScope));
    }
}
