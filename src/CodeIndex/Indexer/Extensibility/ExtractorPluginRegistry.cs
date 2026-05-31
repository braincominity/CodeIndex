using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace CodeIndex.Indexer.Extensibility;

public static class ExtractorPluginRegistry
{
    public const int CurrentApiVersion = 1;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, ISymbolExtractor> SymbolExtractors = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IReferenceExtractor> ReferenceExtractors = new(StringComparer.Ordinal);
    private static bool pluginsLoaded;

    public static IReadOnlyCollection<string> SymbolLanguages
    {
        get
        {
            EnsurePluginsLoaded();
            lock (Gate)
                return SymbolExtractors.Keys.Order(StringComparer.Ordinal).ToArray();
        }
    }

    public static IReadOnlyCollection<string> ReferenceLanguages
    {
        get
        {
            EnsurePluginsLoaded();
            lock (Gate)
                return ReferenceExtractors.Keys.Order(StringComparer.Ordinal).ToArray();
        }
    }

    public static IReadOnlyDictionary<string, string> LanguageExtensions
    {
        get
        {
            EnsurePluginsLoaded();
            lock (Gate)
            {
                var extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                AddLanguageExtensions(extensions, SymbolExtractors.Values.Select(extractor => (extractor.Language, extractor.FileExtensions)));
                AddLanguageExtensions(extensions, ReferenceExtractors.Values.Select(extractor => (extractor.Language, extractor.FileExtensions)));
                return extensions;
            }
        }
    }

    public static bool TryGetSymbolExtractor(string language, out ISymbolExtractor extractor)
    {
        EnsurePluginsLoaded();
        lock (Gate)
            return SymbolExtractors.TryGetValue(language, out extractor!);
    }

    public static bool TryGetReferenceExtractor(string language, out IReferenceExtractor extractor)
    {
        EnsurePluginsLoaded();
        lock (Gate)
            return ReferenceExtractors.TryGetValue(language, out extractor!);
    }

    public static void Register(ISymbolExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        var language = NormalizePluginLanguage(extractor.Language);
        lock (Gate)
            SymbolExtractors[language] = extractor;
    }

    public static void Register(IReferenceExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        var language = NormalizePluginLanguage(extractor.Language);
        lock (Gate)
            ReferenceExtractors[language] = extractor;
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            SymbolExtractors.Clear();
            ReferenceExtractors.Clear();
            pluginsLoaded = true;
        }
    }

    internal static void ReloadForTests()
    {
        lock (Gate)
        {
            SymbolExtractors.Clear();
            ReferenceExtractors.Clear();
            pluginsLoaded = false;
        }
    }

    private static void EnsurePluginsLoaded()
    {
        if (Volatile.Read(ref pluginsLoaded))
            return;

        lock (Gate)
        {
            if (pluginsLoaded)
                return;

            foreach (var pluginPath in EnumeratePluginAssemblyPaths())
                TryLoadPlugin(pluginPath);
            foreach (var patternPath in EnumeratePatternConfigPaths())
                TryLoadPatternConfig(patternPath);

            pluginsLoaded = true;
        }
    }

    private static IEnumerable<string> EnumeratePluginAssemblyPaths()
    {
        foreach (var directory in EnumeratePluginDirectories())
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var pluginPath in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
                yield return pluginPath;
        }
    }

    private static IEnumerable<string> EnumeratePluginDirectories()
    {
        yield return Path.Combine(Environment.CurrentDirectory, ".cdidx", "plugins");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            yield return Path.Combine(home, ".cdidx", "plugins");
    }

    private static IEnumerable<string> EnumeratePatternConfigPaths()
    {
        foreach (var directory in EnumeratePatternDirectories())
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var path in Directory.EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly))
                yield return path;
            foreach (var path in Directory.EnumerateFiles(directory, "*.yml", SearchOption.TopDirectoryOnly))
                yield return path;
        }
    }

    private static IEnumerable<string> EnumeratePatternDirectories()
    {
        yield return Path.Combine(Environment.CurrentDirectory, ".cdidx", "patterns");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            yield return Path.Combine(home, ".config", "cdidx", "patterns");
    }

    private static void TryLoadPatternConfig(string path)
    {
        try
        {
            var language = string.Empty;
            var extensions = new List<string>();
            var patterns = new List<ConfiguredSymbolExtractor.PatternRule>();
            string? pendingKind = null;
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                if (TryReadScalar(line, "language", out var value))
                {
                    language = NormalizePluginLanguage(value);
                }
                else if (TryReadScalar(line.TrimStart('-').Trim(), "extension", out value))
                {
                    extensions.Add(NormalizePluginExtension(value) ?? value);
                }
                else if (TryReadScalar(line.TrimStart('-').Trim(), "kind", out value))
                {
                    pendingKind = value.Trim();
                }
                else if (TryReadScalar(line.TrimStart('-').Trim(), "regex", out value) && pendingKind != null)
                {
                    patterns.Add(new ConfiguredSymbolExtractor.PatternRule(
                        pendingKind,
                        new Regex(value, RegexOptions.Compiled | RegexOptions.CultureInvariant)));
                    pendingKind = null;
                }
            }

            if (language.Length > 0 && patterns.Count > 0)
                Register(new ConfiguredSymbolExtractor(language, extensions, patterns));
        }
        catch
        {
            // Pattern sidecars are best-effort like binary plugins.
        }
    }

    private static bool TryReadScalar(string line, string key, out string value)
    {
        value = string.Empty;
        var prefix = key + ":";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        value = line[prefix.Length..].Trim().Trim('"', '\'').Replace("\\\\", "\\", StringComparison.Ordinal);
        return value.Length > 0;
    }

    private static void TryLoadPlugin(string pluginPath)
    {
        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(pluginPath));
            var attribute = assembly.GetCustomAttribute<CdidxPluginAttribute>();
            if (attribute == null
                || attribute.MinApiVersion > CurrentApiVersion
                || attribute.MaxApiVersion < CurrentApiVersion)
            {
                return;
            }

            foreach (var type in assembly.GetTypes())
            {
                if (type is { IsAbstract: false, IsInterface: false } && type.GetConstructor(Type.EmptyTypes) != null)
                    TryRegisterPluginType(type);
            }
        }
        catch
        {
            // Plugin loading is best-effort so an incompatible DLL cannot prevent indexing.
        }
    }

    private static void TryRegisterPluginType(Type type)
    {
        try
        {
            if (typeof(ISymbolExtractor).IsAssignableFrom(type)
                && Activator.CreateInstance(type) is ISymbolExtractor symbolExtractor)
            {
                Register(symbolExtractor);
            }

            if (typeof(IReferenceExtractor).IsAssignableFrom(type)
                && Activator.CreateInstance(type) is IReferenceExtractor referenceExtractor)
            {
                Register(referenceExtractor);
            }
        }
        catch
        {
            // Ignore broken plugin types and continue loading the rest of the assembly.
        }
    }

    private static void AddLanguageExtensions(
        Dictionary<string, string> target,
        IEnumerable<(string Language, IReadOnlyCollection<string> FileExtensions)> plugins)
    {
        foreach (var (language, fileExtensions) in plugins)
        {
            var normalizedLanguage = NormalizePluginLanguage(language);
            foreach (var extension in fileExtensions)
            {
                var normalizedExtension = NormalizePluginExtension(extension);
                if (normalizedExtension != null)
                    target.TryAdd(normalizedExtension, normalizedLanguage);
            }
        }
    }

    private static string NormalizePluginLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Plugin language must be non-empty.", nameof(language));

        return language.Trim().ToLowerInvariant();
    }

    private static string? NormalizePluginExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        extension = extension.Trim().ToLowerInvariant();
        return extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
    }
}
