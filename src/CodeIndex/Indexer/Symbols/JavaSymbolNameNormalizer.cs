using CodeIndex.Database;

namespace CodeIndex.Indexer;

internal static class JavaSymbolNameNormalizer
{
    internal static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOf('\\', StringComparison.Ordinal) < 0)
            return name;

        return ExactSourceSearchNormalizer.Normalize(name, "java");
    }
}
