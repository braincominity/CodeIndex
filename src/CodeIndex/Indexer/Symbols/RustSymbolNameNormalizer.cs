namespace CodeIndex.Indexer;

internal static class RustSymbolNameNormalizer
{
    internal static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return trimmed;

        if (!trimmed.Contains("::", StringComparison.Ordinal))
            return trimmed.StartsWith("r#", StringComparison.Ordinal)
                ? trimmed[2..]
                : trimmed;

        var segments = trimmed.Split("::");
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i].Trim();
            if (segment.StartsWith("r#", StringComparison.Ordinal))
                segment = segment[2..];
            segments[i] = segment;
        }

        return string.Join("::", segments);
    }
}
