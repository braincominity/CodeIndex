using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class KotlinSymbolNameNormalizer
{
    internal static string Normalize(string name, string matchLine)
    {
        var normalizedName = StripBacktickIdentifier(name);
        var trimmedLine = matchLine.TrimStart();
        if (!trimmedLine.StartsWith("companion object", StringComparison.Ordinal))
            return normalizedName;

        var trimmedName = normalizedName.Trim();
        return string.IsNullOrWhiteSpace(trimmedName)
            || string.Equals(trimmedName, "companion object", StringComparison.Ordinal)
            ? "Companion"
            : normalizedName;
    }

    private static string StripBacktickIdentifier(string name)
    {
        if (name.Length >= 2 && name[0] == '`' && name[^1] == '`')
            return name[1..^1];
        return name;
    }

    internal static void NormalizeSecondaryConstructorNames(List<SymbolRecord> symbols)
    {
        foreach (var symbol in symbols)
        {
            if (symbol.Kind != "function"
                || symbol.ContainerKind != "class"
                || string.IsNullOrWhiteSpace(symbol.ContainerName))
            {
                continue;
            }

            var signature = symbol.Signature?.TrimStart();
            if (string.IsNullOrWhiteSpace(signature))
                continue;

            var isSecondaryConstructor = signature.StartsWith("constructor", StringComparison.Ordinal)
                || signature.StartsWith("public constructor", StringComparison.Ordinal)
                || signature.StartsWith("private constructor", StringComparison.Ordinal)
                || signature.StartsWith("protected constructor", StringComparison.Ordinal)
                || signature.StartsWith("internal constructor", StringComparison.Ordinal);
            if (!isSecondaryConstructor)
                continue;

            symbol.Name = symbol.ContainerName;
        }
    }
}
