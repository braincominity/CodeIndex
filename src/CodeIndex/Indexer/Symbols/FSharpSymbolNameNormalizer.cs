namespace CodeIndex.Indexer;

internal static class FSharpSymbolNameNormalizer
{
    internal static string Normalize(string name)
    {
        if (name.Length >= 4 && name.StartsWith("``", StringComparison.Ordinal) && name.EndsWith("``", StringComparison.Ordinal))
            return name[2..^2];

        if (name.Length >= 2 && name[0] == '(' && name[^1] == ')')
        {
            var operatorName = name[1..^1].Trim();
            if (operatorName.Length > 0)
                return $"operator {operatorName}";
        }

        return name;
    }
}
