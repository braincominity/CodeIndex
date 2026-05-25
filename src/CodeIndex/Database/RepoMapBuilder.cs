using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

/// <summary>
/// Builds repo-level overview (map) from indexed data.
/// Extracted from DbReader to keep each class focused.
/// インデックス済みデータからリポジトリ俯瞰情報（map）を構築する。
/// クラスの責務を明確にするためDbReaderから分離。
/// </summary>
internal sealed class RepoMapBuilder
{
    private readonly SqliteConnection _conn;
    private readonly HashSet<string> _fileColumns;

    private static readonly Dictionary<string, string[]> EntrypointNameHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = ["Main", "Program", "App", "Startup", "CreateHostBuilder"],
        ["python"] = ["main", "app", "cli"],
        ["javascript"] = ["main", "bootstrap", "start", "createApp", "App"],
        ["typescript"] = ["main", "bootstrap", "start", "createApp", "App"],
        ["go"] = ["main"],
        ["rust"] = ["main"],
        ["java"] = ["main", "Application", "App"],
        ["kotlin"] = ["main", "Application", "App"],
        ["ruby"] = ["main", "call", "App"],
        ["php"] = ["main", "handle", "App"],
        ["swift"] = ["main", "App"],
        ["dart"] = ["main", "runApp"],
        ["scala"] = ["main", "App"],
        ["fsharp"] = ["main", "Program", "App"],
        ["vb"] = ["Main", "Program", "App"],
        ["c"] = ["main"],
        ["cpp"] = ["main"],
        ["haskell"] = ["main"],
        ["r"] = ["main"],
        ["lua"] = ["main"],
        ["elixir"] = ["start", "init", "call"],
    };
    private static readonly Dictionary<string, string[]> EntrypointPathHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = ["Program.cs", "Startup.cs", "App.xaml.cs", "MainWindow.xaml.cs", "MainPage.xaml.cs", "AppShell.xaml.cs", "Shell.xaml.cs", "ContentPage.xaml.cs", "ContentView.xaml.cs", "Window.xaml.cs", "UserControl.xaml.cs", "App.cs", "App.razor"],
        ["python"] = ["main.py", "__main__.py", "app.py", "cli.py"],
        ["javascript"] = ["index.js", "main.js", "app.js", "server.js"],
        ["typescript"] = ["index.ts", "main.ts", "app.ts", "server.ts"],
        ["go"] = ["main.go"],
        ["rust"] = ["main.rs", "lib.rs"],
        ["java"] = ["Main.java", "App.java", "Application.java"],
        ["kotlin"] = ["Main.kt", "App.kt", "Application.kt"],
        ["ruby"] = ["app.rb", "main.rb", "cli.rb"],
        ["php"] = ["index.php", "app.php"],
        ["swift"] = ["main.swift", "App.swift"],
        ["dart"] = ["main.dart", "app.dart"],
        ["scala"] = ["Main.scala", "App.scala", "Application.scala"],
        ["fsharp"] = ["Program.fs", "App.fs"],
        ["vb"] = ["Program.vb", "Main.vb", "Module.vb", "Module1.vb", "Form1.vb", "App.xaml.vb", "App.vb"],
        ["c"] = ["main.c"],
        ["cpp"] = ["main.cpp", "main.cc", "main.cxx"],
        ["haskell"] = ["Main.hs", "Main.lhs"],
        ["r"] = ["main.R", "app.R"],
        ["lua"] = ["main.lua", "init.lua"],
        ["elixir"] = ["application.ex", "router.ex", "endpoint.ex"],
    };

    private readonly bool _hasReferencesTable;

    public RepoMapBuilder(SqliteConnection connection, HashSet<string> fileColumns, bool hasReferencesTable)
    {
        _conn = connection;
        _fileColumns = fileColumns;
        _hasReferencesTable = hasReferencesTable;
    }

    /// <summary>
    /// Build a repo-level overview to help AI clients orient before deep queries.
    /// 深掘り前の把握に使うリポジトリ俯瞰情報を構築する。
    /// </summary>
    public RepoMapResult Build(int limit, string? lang, IReadOnlyList<string>? pathPatterns,
        IReadOnlyList<string>? excludePathPatterns, bool excludeTests, double minEntrypointConfidence,
        Func<(DateTime? IndexedAt, DateTime? LatestModified)> getFreshness)
    {
        // Query file stats first, then workspace freshness — preserves original
        // ordering so concurrent indexing cannot make workspace timestamps older
        // than scoped timestamps.
        // ファイル統計を先に取得し、その後にワークスペース鮮度を取得 — 元の順序を
        // 維持し、並行インデックス時にワークスペースのタイムスタンプがスコープ付き
        // タイムスタンプより古くならないようにする。
        //
        // Issue #180: wrap the multi-statement map build in one DEFERRED transaction so
        // the scoped file stats, the workspace freshness, and the entrypoint lookups all
        // come from the same WAL snapshot. Otherwise a concurrent writer committing
        // between statements can make `workspace_latest_modified` older than
        // `latest_modified`, or make entrypoint rows disagree with the file-stats rows
        // they came from.
        // Issue #180: map 内の多段 SELECT を 1 つの DEFERRED transaction で囲み、scoped
        // file stats / workspace freshness / entrypoint 取得が同じ WAL snapshot から返る
        // ようにする。
        using var txn = _conn.BeginTransaction(deferred: true);
        var fileStats = GetFileStats(lang, pathPatterns, excludePathPatterns, excludeTests);
        ApplyJavaModuleGrouping(fileStats, LoadJavaModuleDescriptors());
        var freshness = getFreshness();
        var result = new RepoMapResult
        {
            FileCount = fileStats.Count,
            TotalLines = fileStats.Sum(file => (long)file.Lines),
            TotalSymbols = fileStats.Sum(file => (long)file.SymbolCount),
            TotalReferences = fileStats.Sum(file => (long)file.ReferenceCount),
            IndexedAt = fileStats.Count > 0 ? fileStats.Max(file => file.IndexedAt) : null,
            LatestModified = fileStats.Count > 0 ? fileStats.Max(file => file.Modified) : null,
            WorkspaceIndexedAt = freshness.IndexedAt,
            WorkspaceLatestModified = freshness.LatestModified,
            Languages = fileStats
                .GroupBy(file => file.Lang ?? "unknown")
                .Select(group => new RepoLanguageResult
                {
                    Lang = group.Key,
                    Files = group.Count(),
                    Lines = group.Sum(file => (long)file.Lines),
                    Symbols = group.Sum(file => (long)file.SymbolCount),
                    References = group.Sum(file => (long)file.ReferenceCount),
                })
                .OrderByDescending(group => group.Files)
                .ThenBy(group => group.Lang)
                .Take(limit)
                .ToList(),
            Modules = fileStats
                .GroupBy(GetModuleKey)
                .Select(group => new RepoModuleResult
                {
                    Module = group.Key,
                    Files = group.Count(),
                    Lines = group.Sum(file => (long)file.Lines),
                    Symbols = group.Sum(file => (long)file.SymbolCount),
                    References = group.Sum(file => (long)file.ReferenceCount),
                })
                .OrderByDescending(group => group.References)
                .ThenByDescending(group => group.Symbols)
                .ThenByDescending(group => group.Lines)
                .ThenBy(group => group.Module)
                .Take(limit)
                .ToList(),
            TopFiles = fileStats
                .Select(CreateScoredFileSummary)
                .OrderByDescending(file => file.Score)
                .ThenByDescending(file => file.ReferenceCount)
                .ThenByDescending(file => file.SymbolCount)
                .ThenByDescending(file => file.Lines)
                .ThenBy(file => file.Path)
                .Take(limit)
                .ToList(),
            LargestFiles = fileStats
                .OrderByDescending(file => file.Lines)
                .ThenByDescending(file => file.Size)
                .ThenBy(file => file.Path)
                .Take(limit)
                .Select(CreateUnscoredFileSummary)
                .ToList(),
            SymbolRichFiles = fileStats
                .OrderByDescending(file => file.SymbolCount)
                .ThenByDescending(file => file.ReferenceCount)
                .ThenByDescending(file => file.Lines)
                .ThenBy(file => file.Path)
                .Take(limit)
                .Select(CreateUnscoredFileSummary)
                .ToList(),
            ReferenceRichFiles = fileStats
                .OrderByDescending(file => file.ReferenceCount)
                .ThenByDescending(file => file.SymbolCount)
                .ThenByDescending(file => file.Lines)
                .ThenBy(file => file.Path)
                .Take(limit)
                .Select(CreateUnscoredFileSummary)
                .ToList(),
            Entrypoints = GetEntrypoints(fileStats, limit, lang, pathPatterns, excludePathPatterns, excludeTests, minEntrypointConfidence),
            GraphTableAvailable = _hasReferencesTable,
        };
        txn.Commit();
        return result;
    }

    private List<RepoFileStat> GetFileStats(string? lang, IReadOnlyList<string>? pathPatterns,
        IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        using var cmd = _conn.CreateCommand();
        var refCountExpr = _hasReferencesTable
            ? "(SELECT COUNT(*) FROM symbol_references r WHERE r.file_id = f.id)"
            : "0";
        var sql = $@"
            SELECT f.path, f.lang, f.size, f.lines,
                   (SELECT COUNT(*) FROM symbols s WHERE s.file_id = f.id) AS symbol_count,
                   {refCountExpr} AS reference_count,
                   {GetFileColumnSql("checksum")} AS checksum,
                   {GetFileColumnSql("modified")} AS modified,
                   {GetFileColumnSql("indexed_at")} AS indexed_at
            FROM files f
            WHERE 1=1";

        if (lang != null)
            sql += " AND f.lang = @lang";
        DbReader.AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += " ORDER BY f.path";

        cmd.CommandText = sql;
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        DbReader.AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        var results = new List<RepoFileStat>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new RepoFileStat
            {
                Path = reader.GetString(0),
                Lang = DbReader.GetNullableString(reader, 1),
                Size = reader.GetInt64(2),
                Lines = reader.GetInt32(3),
                SymbolCount = reader.GetInt32(4),
                ReferenceCount = reader.GetInt32(5),
                Checksum = DbReader.GetNullableString(reader, 6),
                Modified = DbReader.GetNullableDateTime(reader, 7),
                IndexedAt = DbReader.GetNullableDateTime(reader, 8),
            });
        }

        return results;
    }

    private Dictionary<string, string> LoadJavaModuleDescriptors()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.path, s.name
            FROM files f
            JOIN symbols s ON s.file_id = f.id
            WHERE f.lang = 'java'
              AND (f.path = 'module-info.java' OR f.path LIKE '%/module-info.java')
              AND s.kind = 'namespace'
            ORDER BY f.path, s.line
            """;

        var moduleByDescriptorPath = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            var descriptorPath = reader.GetString(0);
            if (moduleByDescriptorPath.ContainsKey(descriptorPath))
                continue;

            var moduleName = reader.GetString(1);
            if (!string.IsNullOrWhiteSpace(moduleName))
                moduleByDescriptorPath[descriptorPath] = moduleName;
        }

        return moduleByDescriptorPath;
    }

    private static void ApplyJavaModuleGrouping(List<RepoFileStat> fileStats, IReadOnlyDictionary<string, string> moduleByDescriptorPath)
    {
        if (moduleByDescriptorPath.Count == 0)
            return;

        foreach (var file in fileStats)
        {
            if (!string.Equals(file.Lang, "java", StringComparison.OrdinalIgnoreCase))
                continue;

            var owningModuleName = ResolveOwningJavaModuleName(file.Path, moduleByDescriptorPath);
            if (!string.IsNullOrWhiteSpace(owningModuleName))
                file.ModuleName = owningModuleName;
        }
    }

    private List<RepoEntrypointResult> GetEntrypoints(IReadOnlyList<RepoFileStat> fileStats, int limit,
        string? lang, IReadOnlyList<string>? pathPatterns, IReadOnlyList<string>? excludePathPatterns, bool excludeTests,
        double minConfidence)
    {
        using var cmd = _conn.CreateCommand();
        var sql = @"
            SELECT f.path, f.lang, s.kind, s.name, s.line
            FROM symbols s
            JOIN files f ON s.file_id = f.id
            WHERE s.kind IN ('function', 'class')";

        if (lang != null)
            sql += " AND f.lang = @lang";
        DbReader.AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += " ORDER BY f.path, s.line";

        cmd.CommandText = sql;
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        DbReader.AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        var results = new List<RepoEntrypointResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            var path = reader.GetString(0);
            var candidateLang = DbReader.GetNullableString(reader, 1);
            var kind = reader.GetString(2);
            var name = reader.GetString(3);
            var line = reader.GetInt32(4);
            var match = ScoreEntrypoint(path, candidateLang, kind, name);
            if (match.Score <= 0)
                continue;

            results.Add(new RepoEntrypointResult
            {
                Path = path,
                Lang = candidateLang,
                Kind = kind,
                Name = name,
                Line = line,
                Score = match.Score,
                MatchType = match.MatchType,
                Confidence = match.Confidence,
                HintRank = match.HintRank,
            });
        }

        // Fall back to known entry files when symbol extraction misses entrypoints
        // シンボル抽出がエントリポイントを捉えられない場合、既知のエントリファイルにフォールバック
        var filesWithEntrypoints = results
            .Select(result => result.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in fileStats)
        {
            if (filesWithEntrypoints.Contains(file.Path))
                continue;

            var match = ScoreEntrypointFileFallback(file.Path, file.Lang, file.SymbolCount, file.ReferenceCount);
            if (match.Score <= 0)
                continue;

            results.Add(new RepoEntrypointResult
            {
                Path = file.Path,
                Lang = file.Lang,
                Kind = "file",
                Name = Path.GetFileName(file.Path),
                Line = 1,
                Score = match.Score,
                MatchType = match.MatchType,
                Confidence = match.Confidence,
                HintRank = match.HintRank,
            });
        }

        ApplyEntrypointAmbiguityPenalty(results);
        return results
            .Where(result => result.Confidence >= minConfidence)
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.Confidence)
            .ThenBy(result => result.HintRank)
            .ThenBy(result => result.Path)
            .ThenBy(result => result.Line)
            .Take(limit)
            .ToList();
    }

    private string GetFileColumnSql(string columnName)
    {
        return _fileColumns.Contains(columnName) ? $"f.{columnName}" : "NULL";
    }

    private static RepoFileSummaryResult CreateScoredFileSummary(RepoFileStat file)
    {
        var summary = CreateUnscoredFileSummary(file);
        summary.Score = (file.Lines * 1L) + (file.SymbolCount * 5L) + (file.ReferenceCount * 2L);
        return summary;
    }

    private static RepoFileSummaryResult CreateUnscoredFileSummary(RepoFileStat file)
    {
        return new RepoFileSummaryResult
        {
            Path = file.Path,
            Lang = file.Lang,
            Lines = file.Lines,
            Size = file.Size,
            SymbolCount = file.SymbolCount,
            ReferenceCount = file.ReferenceCount,
        };
    }

    private static string GetModuleKey(RepoFileStat file)
    {
        if (!string.IsNullOrWhiteSpace(file.ModuleName))
        {
            return file.ModuleName;
        }

        var segments = file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return ".";
        if (segments.Length == 1)
            return segments[0];

        return segments[0] switch
        {
            "src" or "app" or "lib" or "tests" or "test" or "docs" or "packages" when segments.Length >= 3 => $"{segments[0]}/{segments[1]}",
            "src" or "app" or "lib" or "tests" or "test" or "docs" or "packages" => segments[0],
            _ => segments[0],
        };
    }

    private static string? ResolveOwningJavaModuleName(string path, IReadOnlyDictionary<string, string> moduleByDescriptorPath)
    {
        var currentDirectory = GetParentDirectoryPath(path) ?? string.Empty;
        while (true)
        {
            var descriptorPath = string.IsNullOrEmpty(currentDirectory)
                ? "module-info.java"
                : $"{currentDirectory}/module-info.java";
            if (moduleByDescriptorPath.TryGetValue(descriptorPath, out var moduleName))
                return moduleName;

            if (string.IsNullOrEmpty(currentDirectory))
                return null;

            currentDirectory = GetParentDirectoryPath(currentDirectory) ?? string.Empty;
        }
    }

    private static string? GetParentDirectoryPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0)
            return string.Empty;

        return path[..lastSlash];
    }

    private static EntrypointScore ScoreEntrypoint(string path, string? lang, string kind, string name)
    {
        if (lang == null)
            return EntrypointScore.None;

        var score = 0;
        var nameRank = GetHintRank(EntrypointNameHints, lang, name);
        if (nameRank > 0)
            score += 4;

        var fileName = Path.GetFileName(path);
        var pathRank = GetHintRank(EntrypointPathHints, lang, fileName);
        if (pathRank > 0)
            score += 3;

        if (score == 0)
            return EntrypointScore.None;

        if (kind == "function")
            score += 1;

        if (kind == "class" && string.Equals(Path.GetFileNameWithoutExtension(fileName), name, StringComparison.OrdinalIgnoreCase))
            score += 1;

        score += GetPathLocationBoost(path);
        var matchType = pathRank > 0 && nameRank > 0
            ? "path+name"
            : pathRank > 0
                ? "path"
                : "name";
        var hintRank = pathRank > 0 && nameRank > 0
            ? Math.Min(pathRank, nameRank)
            : Math.Max(pathRank, nameRank);
        var confidence = pathRank > 0 && nameRank > 0
            ? 0.85
            : pathRank > 0
                ? 0.65
                : 0.5;

        return new EntrypointScore(score, matchType, NormalizeConfidence(confidence + GetPathLocationConfidenceBoost(path)), hintRank);
    }

    private static EntrypointScore ScoreEntrypointFileFallback(string path, string? lang, int symbolCount, int referenceCount)
    {
        if (lang == null)
            return EntrypointScore.None;

        var fileName = Path.GetFileName(path);
        var pathRank = GetHintRank(EntrypointPathHints, lang, fileName);
        if (pathRank <= 0)
        {
            return EntrypointScore.None;
        }

        var score = 2;
        if (symbolCount > 0)
            score += 1;
        if (referenceCount > 0)
            score += 1;

        score += GetPathLocationBoost(path);
        return new EntrypointScore(score, "path", NormalizeConfidence(0.4 + GetPathLocationConfidenceBoost(path)), pathRank);
    }

    private static int GetHintRank(IReadOnlyDictionary<string, string[]> hintsByLang, string lang, string candidate)
    {
        if (!hintsByLang.TryGetValue(lang, out var hints))
            return 0;

        for (var i = 0; i < hints.Length; i++)
        {
            if (string.Equals(hints[i], candidate, StringComparison.OrdinalIgnoreCase))
                return i + 1;
        }

        return 0;
    }

    private static int GetPathLocationBoost(string path)
    {
        var slashCount = path.Count(ch => ch == '/');
        if (slashCount == 0)
            return 2;
        if (path.StartsWith("src/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("app/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("cmd/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
            return 1;

        return 0;
    }

    private static double GetPathLocationConfidenceBoost(string path)
    {
        var slashCount = path.Count(ch => ch == '/');
        if (slashCount == 0)
            return 0.1;
        if (path.StartsWith("src/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("app/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("cmd/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
            return 0.05;

        return 0;
    }

    private static void ApplyEntrypointAmbiguityPenalty(List<RepoEntrypointResult> results)
    {
        foreach (var group in results.GroupBy(result => $"{result.Lang ?? ""}\0{result.MatchType}\0{result.Name}", StringComparer.OrdinalIgnoreCase))
        {
            var count = group.Count();
            if (count <= 1)
                continue;

            var penalty = Math.Min(0.3, 0.1 * (count - 1));
            foreach (var result in group)
                result.Confidence = NormalizeConfidence(Math.Max(0.2, result.Confidence - penalty));
        }
    }

    private static double NormalizeConfidence(double confidence) => Math.Round(Math.Min(confidence, 1.0), 3);

    private readonly record struct EntrypointScore(int Score, string MatchType, double Confidence, int HintRank)
    {
        public static EntrypointScore None { get; } = new(0, "", 0, 0);
    }
}
