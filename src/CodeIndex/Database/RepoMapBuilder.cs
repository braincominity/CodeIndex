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
        ["csharp"] = ["Program.cs", "Startup.cs", "App.xaml.cs", "App.cs", "App.razor"],
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
        ["vb"] = ["Program.vb", "Module1.vb", "App.vb"],
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
        IReadOnlyList<string>? excludePathPatterns, bool excludeTests,
        Func<(DateTime? IndexedAt, DateTime? LatestModified)> getFreshness)
    {
        // Query file stats first, then workspace freshness — preserves original
        // ordering so concurrent indexing cannot make workspace timestamps older
        // than scoped timestamps.
        // ファイル統計を先に取得し、その後にワークスペース鮮度を取得 — 元の順序を
        // 維持し、並行インデックス時にワークスペースのタイムスタンプがスコープ付き
        // タイムスタンプより古くならないようにする。
        var fileStats = GetFileStats(lang, pathPatterns, excludePathPatterns, excludeTests);
        var freshness = getFreshness();
        return new RepoMapResult
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
                .GroupBy(file => GetModuleKey(file.Path))
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
            Entrypoints = GetEntrypoints(fileStats, limit, lang, pathPatterns, excludePathPatterns, excludeTests),
            GraphTableAvailable = _hasReferencesTable,
        };
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
                Lang = reader.IsDBNull(1) ? null : reader.GetString(1),
                Size = reader.GetInt64(2),
                Lines = reader.GetInt32(3),
                SymbolCount = reader.GetInt32(4),
                ReferenceCount = reader.GetInt32(5),
                Checksum = reader.IsDBNull(6) ? null : reader.GetString(6),
                Modified = DbReader.GetNullableDateTime(reader, 7),
                IndexedAt = DbReader.GetNullableDateTime(reader, 8),
            });
        }

        return results;
    }

    private List<RepoEntrypointResult> GetEntrypoints(IReadOnlyList<RepoFileStat> fileStats, int limit,
        string? lang, IReadOnlyList<string>? pathPatterns, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
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
            var candidateLang = reader.IsDBNull(1) ? null : reader.GetString(1);
            var kind = reader.GetString(2);
            var name = reader.GetString(3);
            var line = reader.GetInt32(4);
            var score = ScoreEntrypoint(path, candidateLang, kind, name);
            if (score <= 0)
                continue;

            results.Add(new RepoEntrypointResult
            {
                Path = path,
                Lang = candidateLang,
                Kind = kind,
                Name = name,
                Line = line,
                Score = score,
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

            var score = ScoreEntrypointFileFallback(file.Path, file.Lang, file.SymbolCount, file.ReferenceCount);
            if (score <= 0)
                continue;

            results.Add(new RepoEntrypointResult
            {
                Path = file.Path,
                Lang = file.Lang,
                Kind = "file",
                Name = Path.GetFileName(file.Path),
                Line = 1,
                Score = score,
            });
        }

        return results
            .OrderByDescending(result => result.Score)
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

    private static string GetModuleKey(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
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

    private static int ScoreEntrypoint(string path, string? lang, string kind, string name)
    {
        if (lang == null)
            return 0;

        var score = 0;
        if (EntrypointNameHints.TryGetValue(lang, out var names) && names.Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
            score += 4;

        var fileName = Path.GetFileName(path);
        if (EntrypointPathHints.TryGetValue(lang, out var fileHints) && fileHints.Any(candidate => string.Equals(candidate, fileName, StringComparison.OrdinalIgnoreCase)))
            score += 3;

        if (score == 0)
            return 0;

        if (kind == "function")
            score += 1;

        if (kind == "class" && string.Equals(Path.GetFileNameWithoutExtension(fileName), name, StringComparison.OrdinalIgnoreCase))
            score += 1;

        return score;
    }

    private static int ScoreEntrypointFileFallback(string path, string? lang, int symbolCount, int referenceCount)
    {
        if (lang == null)
            return 0;

        var fileName = Path.GetFileName(path);
        if (!EntrypointPathHints.TryGetValue(lang, out var fileHints) ||
            !fileHints.Any(candidate => string.Equals(candidate, fileName, StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        var score = 2;
        if (symbolCount > 0)
            score += 1;
        if (referenceCount > 0)
            score += 1;

        return score;
    }
}
