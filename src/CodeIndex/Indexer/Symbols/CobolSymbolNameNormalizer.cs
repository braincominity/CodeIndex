namespace CodeIndex.Indexer;

internal static class CobolSymbolNameNormalizer
{
    internal static string Normalize(string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? name
            : name.Trim().ToUpperInvariant();
    }
}
