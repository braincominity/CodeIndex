namespace CodeIndex.Indexer;

internal static class LanguageMapOverrides
{
    internal const string WorkspaceFileName = ".cdidx-langmap.yaml";

    internal static IReadOnlyDictionary<string, string> LoadEffectiveMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateConfigPaths())
            LoadInto(path, map);
        return map;
    }

    private static IEnumerable<string> EnumerateConfigPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            yield return Path.Combine(home, ".config", "cdidx", "langmap.yaml");

        var directory = Environment.CurrentDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, WorkspaceFileName);
            if (File.Exists(candidate))
            {
                yield return candidate;
                yield break;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }
    }

    private static void LoadInto(string path, Dictionary<string, string> target)
    {
        if (!File.Exists(path))
            return;

        string? pendingExtension = null;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (TryReadScalar(line.TrimStart('-').Trim(), "extension", out var value))
            {
                pendingExtension = NormalizeExtension(value);
                continue;
            }

            if (TryReadScalar(line.TrimStart('-').Trim(), "language", out value) && pendingExtension != null)
            {
                target[pendingExtension] = value.Trim().ToLowerInvariant();
                pendingExtension = null;
            }
        }
    }

    private static bool TryReadScalar(string line, string key, out string value)
    {
        value = string.Empty;
        var prefix = key + ":";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        value = line[prefix.Length..].Trim().Trim('"', '\'');
        return value.Length > 0;
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension.Trim().ToLowerInvariant();
        return extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
    }
}
