using CodeIndex.Database;
using CodeIndex.Indexer;
using CodeIndex.Models;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for DbReader query operations.
/// DbReaderクエリ操作のテスト。
/// </summary>
[Collection("SQLite pool sensitive")]
public class DbReaderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContext _db;
    private readonly DbWriter _writer;
    private readonly DbReader _reader;

    public DbReaderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"codeindex_reader_test_{Guid.NewGuid():N}.db");
        _db = new DbContext(_dbPath);
        _db.InitializeSchema();
        _writer = new DbWriter(_db.Connection);

        // Seed test data first, then stamp the index-complete marker so DbReader sees the
        // same state a production post-indexing open would: populated tables + user_version.
        // DbReader を構築する前に seed と MarkIndexComplete を済ませ、本番の index 完了時と同じ状態にする。
        SeedData();
        _writer.MarkGraphReady();
        _writer.MarkIssuesReady();
        // #86: post-indexing production DBs also stamp FoldReady after a full scan, so the
        // reader exercises the Unicode fold path. Legacy fallback is covered by a separate
        // test that opens a DB without this flag.
        // #86: full scan 後の本番 DB は fold ready も立つため、reader は fold 経路を通す。
        _writer.MarkFoldReady();
        foreach (var lang in FileIndexer.GetHotspotFamilyMarkerLanguages())
            _writer.MarkHotspotFamilyReady(lang, $"{lang}-fixture-fingerprint");
        _reader = new DbReader(_db.Connection);
    }

    private void SeedData()
    {
        const string authContent = "def authenticate(user, password):\n    if user == 'admin':\n        return True\n    return False";
        var pyId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/auth.py", Lang = "python", Size = 500, Lines = 30,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = pyId, ChunkIndex = 0, StartLine = 1, EndLine = 30,
            Content = authContent,
        }]);
        var authSymbols = new List<SymbolRecord>
        {
            new SymbolRecord
            {
                FileId = pyId, Kind = "function", Name = "authenticate", Line = 1,
                StartLine = 1, EndLine = 4, BodyStartLine = 2, BodyEndLine = 4,
                Signature = "def authenticate(user, password):"
            },
        };
        _writer.InsertSymbols(authSymbols);
        _writer.InsertReferences(ReferenceExtractor.Extract(pyId, "python", authContent, authSymbols));

        const string apiContent = "export class ApiClient {\n  async fetchData(url) {\n    return fetch(url)\n  }\n}";
        var jsId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/api.js", Lang = "javascript", Size = 800, Lines = 50,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = jsId, ChunkIndex = 0, StartLine = 1, EndLine = 50,
            Content = apiContent,
        }]);
        var apiSymbols = new List<SymbolRecord>
        {
            new SymbolRecord
            {
                FileId = jsId, Kind = "class", Name = "ApiClient", Line = 1,
                StartLine = 1, EndLine = 4, BodyStartLine = 1, BodyEndLine = 4,
                Signature = "export class ApiClient {", Visibility = "export"
            },
            new SymbolRecord
            {
                FileId = jsId, Kind = "function", Name = "fetchData", Line = 2,
                StartLine = 2, EndLine = 3, BodyStartLine = 2, BodyEndLine = 3,
                Signature = "async fetchData(url) {", ContainerKind = "class", ContainerName = "ApiClient"
            },
        };
        _writer.InsertSymbols(apiSymbols);
        _writer.InsertReferences(ReferenceExtractor.Extract(jsId, "javascript", apiContent, apiSymbols));

        // Plain text file with no symbols for outline edge case
        // アウトラインのエッジケース用のシンボルなしプレーンテキストファイル
        InsertIndexedFile("docs/notes.md", "markdown", "# Notes\n\nSome documentation text.");
    }

    private void InsertIndexedFile(string path, string lang, string content, DateTime? modified = null, string? familyScopeKey = null)
    {
        var normalized = content.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = path,
            Lang = lang,
            Size = normalized.Length,
            Lines = lines.Length,
            Modified = modified ?? new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        _writer.InsertChunks([new ChunkRecord
        {
            FileId = fileId,
            ChunkIndex = 0,
            StartLine = 1,
            EndLine = lines.Length,
            Content = normalized,
        }]);

        var symbols = SymbolExtractor.Extract(fileId, lang, normalized);
        SymbolExtractor.ApplyFamilyScope(symbols, familyScopeKey ?? FileIndexer.DeriveFallbackFamilyScopeKey(path));
        _writer.InsertSymbols(symbols);
        _writer.InsertReferences(ReferenceExtractor.Extract(fileId, lang, normalized, symbols));
    }

    [Fact]
    public void Search_FindsMatchingChunks()
    {
        var results = _reader.Search("authenticate");
        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
        Assert.Equal(1, results[0].StartLine);
    }

    [Fact]
    public void Search_PrefersSourceFilesOverTests()
    {
        var testFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "tests/auth_test.py", Lang = "python", Size = 300, Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = testFileId, ChunkIndex = 0, StartLine = 1, EndLine = 3,
            Content = "def authenticate_test_case():\n    authenticate('a', 'b')\n    return True",
        }]);

        var results = _reader.Search("authenticate", limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("src/auth.py", results[0].Path);
        Assert.Equal("tests/auth_test.py", results[1].Path);
    }

    [Fact]
    public void Search_DeduplicatesOverlappingChunks()
    {
        // Create two overlapping chunks in the same file that both match
        // 同じファイル内でオーバーラップし、両方マッチする2チャンクを作成
        var overlapFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/overlap.py", Lang = "python", Size = 2000, Lines = 100,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([
            new ChunkRecord { FileId = overlapFileId, ChunkIndex = 0, StartLine = 1, EndLine = 80, Content = "# overlap_marker\ndef func_a():\n    pass\n" + string.Concat(Enumerable.Repeat("# filler\n", 76)) },
            new ChunkRecord { FileId = overlapFileId, ChunkIndex = 1, StartLine = 71, EndLine = 150, Content = "# overlap_marker\ndef func_b():\n    pass\n" + string.Concat(Enumerable.Repeat("# filler\n", 76)) },
        ]);

        var results = _reader.Search("overlap_marker", limit: 10);

        // Should deduplicate: only 1 result from overlap.py (higher ranked chunk kept)
        // 重複排除: overlap.py からは1件のみ（上位ランクのチャンクを保持）
        var overlapResults = results.Where(r => r.Path == "src/overlap.py").ToList();
        Assert.Single(overlapResults);
    }

    [Fact]
    public void Search_PrefersDefinitionFileOverReferenceOnlySourceFile()
    {
        var refFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/session.py", Lang = "python", Size = 300, Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = refFileId, ChunkIndex = 0, StartLine = 1, EndLine = 3,
            Content = "def login(user, password):\n    return authenticate(user, password)\n",
        }]);

        var results = _reader.Search("authenticate", limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("src/auth.py", results[0].Path);
        Assert.Equal("src/session.py", results[1].Path);
    }

    [Fact]
    public void Search_ReturnsEmptyForNoMatch()
    {
        var results = _reader.Search("nonexistent_term_xyz");
        Assert.Empty(results);
    }

    [Fact]
    public void Search_FiltersByLanguage()
    {
        // "fetch" appears in JS only / "fetch"はJSのみに存在
        var jsResults = _reader.Search("fetch", lang: "javascript");
        Assert.NotEmpty(jsResults);

        var pyResults = _reader.Search("fetch", lang: "python");
        Assert.Empty(pyResults);
    }

    [Fact]
    public void Search_RespectsLimit()
    {
        var results = _reader.Search("return", limit: 1);
        Assert.Single(results);
    }

    [Fact]
    public void Search_RawQuerySupportsFtsPrefixSyntax()
    {
        var results = _reader.Search("auth*", rawQuery: true);

        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
    }

    [Fact]
    public void SearchSymbols_FindsByName()
    {
        var results = _reader.SearchSymbols("authenticate");
        Assert.Single(results);
        Assert.Equal("function", results[0].Kind);
        Assert.Equal("src/auth.py", results[0].Path);
    }

    [Fact]
    public void SearchSymbols_CSharpOperatorsConversionsAndIndexersUseNavigableNames()
    {
        InsertIndexedFile("src/csharp_special_names.cs", "csharp",
            """
            using System.Collections.Generic;

            public struct Money
            {
                public static (int whole, int cents) operator +(Money a, Money b) => (0, 0);
                public static Dictionary<string, int> operator -(Money a, Money b) => new();
                public static checked Money operator checked +(Money a, Money b) => new();
                public static implicit operator decimal(Money m) => 0m;
                public static explicit operator Money(decimal d) => new();
                public Money(decimal amount) { }
                public static explicit operator checked byte(Money m) => 0;
                public static explicit operator Dictionary<string,int>(Money m) => new();
                public static explicit operator (int whole,int cents)(Money m) => (0, 0);
                public static explicit operator (Dictionary<string, int> map, int count)?(Money m) => null;
                public static explicit operator (int[] items, int count)(Money m) => ([], 0);
                public static explicit operator ((int a, int b) pair, int count)(Money m) => ((0, 0), 0);
            }

            public class Bag
            {
                private string[] _items = new string[10];
                public string this[int i] { get => _items[i]; set => _items[i] = value; }
            }
            """);

        Assert.Single(_reader.SearchSymbols("operator +", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("operator -", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("operator checked +", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("implicit operator decimal", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("explicit operator Money", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("explicit operator checked byte", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("explicit operator Dictionary<string, int>", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("explicit operator (int whole, int cents)", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("explicit operator (Dictionary<string, int> map, int count)?", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("explicit operator (int[] items, int count)", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("explicit operator ((int a, int b) pair, int count)", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("Money", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
        Assert.Single(_reader.SearchSymbols("Item", kind: "function", lang: "csharp", exact: true, pathPatterns: ["csharp_special_names"]));
    }

    [Fact]
    public void SearchSymbols_AndDeps_DoNotTreatNamedArgumentLabelsAsLocalFunctions()
    {
        InsertIndexedFile("src/platform.cs", "csharp",
            """
            public class PlatformState
            {
                public static bool Detect() =>
                    new Options(
                        isWindows: OperatingSystem.IsWindows(),
                        isMacCatalyst: OperatingSystem.IsMacCatalyst()).Ready;
            }
            """);
        InsertIndexedFile("src/app.cs", "csharp",
            """
            public class App
            {
                public bool Read() => OperatingSystem.IsWindows() || OperatingSystem.IsMacCatalyst();
            }
            """);

        Assert.Empty(_reader.SearchSymbols("IsWindows", lang: "csharp"));
        Assert.Empty(_reader.SearchSymbols("IsMacCatalyst", lang: "csharp"));
        Assert.Empty(_reader.GetFileDependencies(lang: "csharp"));
    }

    [Fact]
    public void SearchSymbols_FindsAliasQualifiedExplicitInterfaceImplementations()
    {
        InsertIndexedFile("src/impl.cs", "csharp",
            """
            public interface IFoo
            {
                string Name();
                object Create();
            }

            public class Impl : IFoo
            {
                global::System.String IFoo.Name() => "x";
                Alias::Type IFoo.Create() => default;
            }
            """);

        var nameResults = _reader.SearchSymbols("Name", lang: "csharp");
        var createResults = _reader.SearchSymbols("Create", lang: "csharp");

        Assert.Contains(nameResults, s => s.Kind == "function" && s.Name == "Name" && s.ReturnType == "global::System.String");
        Assert.Contains(createResults, s => s.Kind == "function" && s.Name == "Create" && s.ReturnType == "Alias::Type");
    }

    [Fact]
    public void SearchSymbols_ReturnsRichMetadataWhenAvailable()
    {
        var results = _reader.SearchSymbols("fetchData");

        var symbol = Assert.Single(results);
        Assert.Equal(2, symbol.StartLine);
        Assert.Equal(3, symbol.EndLine);
        Assert.Equal(2, symbol.BodyStartLine);
        Assert.Equal(3, symbol.BodyEndLine);
        Assert.Equal("ApiClient", symbol.ContainerName);
        Assert.Equal("class", symbol.ContainerKind);
        Assert.Equal("async fetchData(url) {", symbol.Signature);
    }

    [Fact]
    public void GetExcerpt_ReconstructsRequestedLineRange()
    {
        var excerpt = _reader.GetExcerpt("src/auth.py", 1, 2);

        Assert.NotNull(excerpt);
        Assert.Equal(1, excerpt!.StartLine);
        Assert.Equal(2, excerpt.EndLine);
        Assert.Contains("def authenticate(user, password):", excerpt.Content);
        Assert.Contains("if user == 'admin':", excerpt.Content);
    }

    [Fact]
    public void FindInFiles_ReturnsPathScopedLiteralMatchesWithContext()
    {
        InsertIndexedFile("src/Auth.cs", "csharp",
            """
            class Auth
            {
                void Guard() {}
                void Next() {}
            }
            """);

        var results = _reader.FindInFiles("guard", limit: 10, pathPatterns: ["src/Auth.cs"], before: 1, after: 1);

        var match = Assert.Single(results);
        Assert.Equal("src/Auth.cs", match.Path);
        Assert.Equal(3, match.Line);
        Assert.Equal(10, match.Column);
        Assert.Equal(2, match.StartLine);
        Assert.Equal(4, match.EndLine);
        Assert.Contains("void Guard()", match.Snippet);
        Assert.Contains("void Next()", match.Snippet);
    }

    [Fact]
    public void FindInFiles_ExactModeIsCaseSensitive()
    {
        InsertIndexedFile("src/Auth.cs", "csharp",
            """
            class Auth
            {
                void Guard() {}
            }
            """);

        var insensitive = _reader.FindInFiles("guard", limit: 10, pathPatterns: ["src/Auth.cs"]);
        var exact = _reader.FindInFiles("guard", limit: 10, pathPatterns: ["src/Auth.cs"], exact: true);

        Assert.Single(insensitive);
        Assert.Empty(exact);
    }

    [Fact]
    public void FindInFiles_ReturnsEverySameLineOccurrence()
    {
        InsertIndexedFile("src/Sample.cs", "csharp", "alpha alpha alpha\n");

        var results = _reader.FindInFiles("alpha", limit: 10, pathPatterns: ["src/Sample.cs"]);

        Assert.Equal(3, results.Count);
        Assert.Equal([1, 7, 13], results.Select(r => r.Column).ToArray());
        Assert.All(results, result => Assert.Equal(1, result.Line));
    }

    [Fact]
    public void FindInFiles_CountsOverlappingOccurrences()
    {
        InsertIndexedFile("src/Sample.cs", "csharp", "// banana\n");

        var results = _reader.FindInFiles("ana", limit: 10, pathPatterns: ["src/Sample.cs"]);

        Assert.Equal(2, results.Count);
        Assert.Equal([5, 7], results.Select(r => r.Column).ToArray());
    }

    [Fact]
    public void GetDefinitions_ReturnsDefinitionContentAndOptionalBody()
    {
        var results = _reader.GetDefinitions("authenticate", includeBody: true);

        var definition = Assert.Single(results);
        Assert.Contains("def authenticate(user, password):", definition.Content);
        Assert.NotNull(definition.BodyContent);
        Assert.Contains("return True", definition.BodyContent);
    }

    [Fact]
    public void SearchSymbols_MultipleNamesAreOrJoined()
    {
        var results = _reader.SearchSymbols(new[] { "authenticate", "fetchData" });
        var names = results.Select(r => r.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "authenticate", "fetchData" }, names);
    }

    [Fact]
    public void SearchSymbols_MultiNameLimitStaysGlobalCap()
    {
        // `limit` must remain the total-result cap, not a per-name cap, so MCP payload / CLI output
        // size stays bounded. limit=1 with two requested names must return at most one row.
        // `limit` は合計の上限を維持すること。limit=1 で2名要求した場合も 1 行以下に収める。
        var capped = _reader.SearchSymbols(new[] { "authenticate", "fetchData" }, limit: 1);
        Assert.True(capped.Count <= 1, $"limit=1 must return <=1 row, got {capped.Count}");

        // Under a generous cap, round-robin merge must include every requested name at least once.
        // 十分な上限の下では、round-robin マージですべての要求名が少なくとも 1 行含まれること。
        var fair = _reader.SearchSymbols(new[] { "authenticate", "fetchData" }, limit: 10);
        var names = fair.Select(r => r.Name).Distinct().OrderBy(n => n).ToList();
        Assert.Equal(new[] { "authenticate", "fetchData" }, names);
    }

    [Fact]
    public void SearchSymbols_ExactMatchesNameEqualityAcrossMultipleNames()
    {
        // Seed a sibling symbol whose name contains `authenticate` as a substring so substring
        // mode returns both but exact mode returns only the exact-name rows per OR name.
        // exact=false は substring なので `authenticate_v2` も引き当てるが、exact=true は名前一致のみ。
        var extraFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/auth_v2.py", Lang = "python", Size = 80, Lines = 4,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertSymbols([
            new SymbolRecord { FileId = extraFileId, Kind = "function", Name = "authenticate_v2", Line = 1, StartLine = 1, EndLine = 1 },
        ]);

        var substring = _reader.SearchSymbols(new[] { "authenticate", "fetchData" }, limit: 10, exact: false)
            .Select(r => r.Name).Distinct().OrderBy(n => n).ToList();
        Assert.Contains("authenticate", substring);
        Assert.Contains("authenticate_v2", substring);
        Assert.Contains("fetchData", substring);

        var exact = _reader.SearchSymbols(new[] { "authenticate", "fetchData" }, limit: 10, exact: true)
            .Select(r => r.Name).Distinct().OrderBy(n => n).ToList();
        Assert.Equal(new[] { "authenticate", "fetchData" }, exact);

        // Case-insensitive equality: the request's casing should not matter.
        // 大文字小文字を無視した完全一致であることを確認。
        var exactMixedCase = _reader.SearchSymbols(new[] { "AUTHENTICATE" }, limit: 10, exact: true)
            .Select(r => r.Name).Distinct().ToList();
        Assert.Equal(new[] { "authenticate" }, exactMixedCase);
    }

    [Fact]
    public void SearchSymbols_ExactFoldsNonAsciiCasing()
    {
        // #96: true Unicode CaseFold must catch accent/case pairs, sharp-S, Greek final sigma,
        // and width variants through `--exact`.
        // #96: Unicode CaseFold により accent/case、sharp-S、Greek final sigma、全角/半角を
        // `--exact` で同一視できることを確認する。
        var extraFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/intl.py", Lang = "python", Size = 120, Lines = 6,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertSymbols([
            new SymbolRecord { FileId = extraFileId, Kind = "function", Name = "café_init", Line = 1, StartLine = 1, EndLine = 1 },
            new SymbolRecord { FileId = extraFileId, Kind = "function", Name = "Ｒｕｎ", Line = 2, StartLine = 2, EndLine = 2 }, // fullwidth
            new SymbolRecord { FileId = extraFileId, Kind = "function", Name = "Straße", Line = 3, StartLine = 3, EndLine = 3 },
            new SymbolRecord { FileId = extraFileId, Kind = "function", Name = "Σ", Line = 4, StartLine = 4, EndLine = 4 },
        ]);

        // Lowercase / uppercase Unicode should both land on the same folded row.
        // 大文字小文字違いでも folded 一致する。
        Assert.Single(_reader.SearchSymbols(new[] { "CAFÉ_INIT" }, limit: 10, exact: true));
        Assert.Single(_reader.SearchSymbols(new[] { "café_init" }, limit: 10, exact: true));

        // Sharp-S and final sigma are the classic Unicode CaseFold deltas over invariant lower.
        // sharp-S と final sigma は invariant-lower との差分が出る代表例。
        var sharpS = _reader.SearchSymbols(new[] { "STRASSE" }, limit: 10, exact: true)
            .Select(r => r.Name).ToList();
        Assert.Equal(new[] { "Straße" }, sharpS);

        var sigma = _reader.SearchSymbols(new[] { "ς" }, limit: 10, exact: true)
            .Select(r => r.Name).ToList();
        Assert.Equal(new[] { "Σ" }, sigma);

        // Fullwidth vs halfwidth: FormKC collapses them.
        // 全角/半角も FormKC 合成で同じになる。
        var halfwidth = _reader.SearchSymbols(new[] { "Run" }, limit: 10, exact: true)
            .Select(r => r.Name).OrderBy(n => n).ToList();
        Assert.Contains("Ｒｕｎ", halfwidth);
    }

    [Fact]
    public void SearchSymbols_ExactPrefersExactCaseOverFoldSibling()
    {
        InsertIndexedFile("src/a_case.py", "python",
            "def apiTwin():\n    return authenticate('a', 'b')\n");
        InsertIndexedFile("tests/z_case.py", "python",
            "def ApiTwin():\n    return authenticate('a', 'b')\n");

        var symbols = _reader.SearchSymbols(new[] { "ApiTwin" }, limit: 10, exact: true)
            .Where(r => r.Name is "ApiTwin" or "apiTwin")
            .Select(r => r.Name)
            .Distinct()
            .Take(2)
            .ToList();
        Assert.Equal(new[] { "ApiTwin", "apiTwin" }, symbols);

        var definitions = _reader.GetDefinitions("ApiTwin", limit: 10, exact: true)
            .Where(r => r.Name is "ApiTwin" or "apiTwin")
            .Select(r => r.Name)
            .Distinct()
            .Take(2)
            .ToList();
        Assert.Equal(new[] { "ApiTwin", "apiTwin" }, definitions);

        var topSymbol = Assert.Single(_reader.SearchSymbols(new[] { "ApiTwin" }, limit: 1, exact: true));
        Assert.Equal("ApiTwin", topSymbol.Name);
        Assert.Equal("tests/z_case.py", topSymbol.Path);

        var topDefinition = Assert.Single(_reader.GetDefinitions("ApiTwin", limit: 1, exact: true));
        Assert.Equal("ApiTwin", topDefinition.Name);
        Assert.Equal("tests/z_case.py", topDefinition.Path);
    }

    [Fact]
    public void AllFoldedColumnsBackfilled_DetectsLegacyRowsWithNullFoldedValues()
    {
        // Regression for codex #86 review: the upgrade path must not stamp FoldReady when
        // legacy rows (pre-#86) still have NULL folded columns. Simulate by inserting a row
        // via raw SQL that bypasses the writer's folded-column population, then confirm the
        // backfill check reports missing data.
        // Codex 指摘の回帰: legacy 行が残っていれば AllFoldedColumnsBackfilled() は false を返す。
        var legacyPath = Path.Combine(Path.GetTempPath(), $"codeindex_fold_verify_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DbContext(legacyPath);
            db.InitializeSchema();
            var writer = new DbWriter(db.Connection);

            // Happy path: fresh DB with writer-inserted rows — all folded columns populated.
            // writer 経由で入れた行は folded 付き。
            var fileId = writer.UpsertFile(new FileRecord
            {
                Path = "src/a.py", Lang = "python", Size = 1, Lines = 1,
                Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            writer.InsertSymbols([
                new SymbolRecord { FileId = fileId, Kind = "function", Name = "authenticate", Line = 1, StartLine = 1, EndLine = 1 },
            ]);
            Assert.True(writer.AllFoldedColumnsBackfilled());

            // Simulate a legacy row by manually nulling name_folded (as a pre-#86 row would be).
            // pre-#86 の legacy 行を模擬して name_folded を NULL に戻す。
            using (var cmd = db.Connection.CreateCommand())
            {
                cmd.CommandText = "UPDATE symbols SET name_folded = NULL WHERE name = 'authenticate'";
                cmd.ExecuteNonQuery();
            }
            Assert.False(writer.AllFoldedColumnsBackfilled());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
        }
    }

    [Fact]
    public void SearchSymbols_ExactFallsBackToNocaseWhenFoldKeyVersionMismatches()
    {
        // #86 codex third-pass review: when NameFold.Fold changes and bumps NameFold.Version,
        // previously stamped DBs must NOT be read through the folded equality path — their
        // stored keys were generated by the old fold function and comparing them against
        // queries folded with the new function silently misses.
        // Simulate by writing a mismatched `fold_key_version` into codeindex_meta and
        // confirming the reader falls back to NOCASE. Rebuild would restamp to current.
        // #86 3rd pass: fold_key_version 不一致時は NOCASE fallback に降格することを固定する。
        var mismatchPath = Path.Combine(Path.GetTempPath(), $"codeindex_fold_version_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DbContext(mismatchPath);
            db.InitializeSchema();
            var writer = new DbWriter(db.Connection);
            var fileId = writer.UpsertFile(new FileRecord
            {
                Path = "src/a.py", Lang = "python", Size = 1, Lines = 1,
                Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            writer.InsertSymbols([
                new SymbolRecord { FileId = fileId, Kind = "function", Name = "authenticate", Line = 1, StartLine = 1, EndLine = 1 },
            ]);
            writer.MarkGraphReady();
            writer.MarkIssuesReady();
            writer.MarkFoldReady();

            // Now simulate a future version bump: overwrite the stored fold_key_version so the
            // reader sees a different stamped version than the current binary.
            // 未来の version bump を模擬: 記録された fold_key_version を書き換え、reader と
            // NameFold.Version を食い違わせる。
            writer.SetMeta("fold_key_version", "99");

            var reader = new DbReader(db.Connection);
            Assert.False(reader._foldReady);
            // ASCII equality still works via the NOCASE fallback path.
            Assert.Single(reader.SearchSymbols(new[] { "AUTHENTICATE" }, limit: 10, exact: true));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(mismatchPath)) File.Delete(mismatchPath);
        }
    }

    [Fact]
    public void SearchSymbols_ExactFallsBackToNocaseWhenFoldFingerprintMismatches()
    {
        // #97: runtime casing tables can drift across .NET upgrades even when
        // NameFold.Version stays constant. The persisted canary fingerprint must still
        // match the current runtime's observable fold output before folded keys are trusted.
        // #97: version が同じでも runtime drift はあり得るため、fingerprint 不一致時は
        // fold trusted を外して NOCASE fallback に降格する。
        var mismatchPath = Path.Combine(Path.GetTempPath(), $"codeindex_fold_fingerprint_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DbContext(mismatchPath);
            db.InitializeSchema();
            var writer = new DbWriter(db.Connection);
            var fileId = writer.UpsertFile(new FileRecord
            {
                Path = "src/a.py", Lang = "python", Size = 1, Lines = 1,
                Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            writer.InsertSymbols([
                new SymbolRecord { FileId = fileId, Kind = "function", Name = "authenticate", Line = 1, StartLine = 1, EndLine = 1 },
            ]);
            writer.MarkGraphReady();
            writer.MarkIssuesReady();
            writer.MarkFoldReady();

            writer.SetMeta("fold_key_fingerprint", "DEADBEEFDEADBEEF");

            var reader = new DbReader(db.Connection);
            Assert.False(reader._foldReady);
            Assert.Single(reader.SearchSymbols(new[] { "AUTHENTICATE" }, limit: 10, exact: true));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(mismatchPath)) File.Delete(mismatchPath);
        }
    }

    [Fact]
    public void SearchSymbols_ExactFallsBackToNocaseWhenFoldNotReady()
    {
        // Legacy / partial-backfill DBs do not set FoldReadyFlag; the reader must silently
        // fall back to the ASCII `COLLATE NOCASE` path and still return correct ASCII results.
        // Non-ASCII casing is expected to miss (documented limitation until reindex).
        // Legacy DB は fold フラグ未設定なら NOCASE fallback。ASCII は動き続ける。
        var legacyPath = Path.Combine(Path.GetTempPath(), $"codeindex_fold_legacy_{Guid.NewGuid():N}.db");
        try
        {
            using var legacyDb = new DbContext(legacyPath);
            legacyDb.InitializeSchema();
            var writer = new DbWriter(legacyDb.Connection);
            var fileId = writer.UpsertFile(new FileRecord
            {
                Path = "src/a.py", Lang = "python", Size = 1, Lines = 1,
                Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            writer.InsertSymbols([
                new SymbolRecord { FileId = fileId, Kind = "function", Name = "authenticate", Line = 1, StartLine = 1, EndLine = 1 },
            ]);
            writer.MarkGraphReady();
            writer.MarkIssuesReady();
            // NOTE: intentionally do NOT stamp FoldReady.

            var legacyReader = new DbReader(legacyDb.Connection);
            Assert.False(legacyReader._foldReady);
            // ASCII case-insensitive equality still works via COLLATE NOCASE fallback.
            Assert.Single(legacyReader.SearchSymbols(new[] { "AUTHENTICATE" }, limit: 10, exact: true));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
        }
    }

    [Fact]
    public void SearchSymbols_ExactPredicateIsIndexable()
    {
        // Guard: the exact-match predicate must stay SARGable so SQLite can pick
        // idx_symbols_name_nocase instead of falling back to a full scan per query name.
        // Regression for the codex review of #81. `lower(col) = lower(@q)` is NOT SARGable;
        // `s.name = @q COLLATE NOCASE` is, given the COLLATE NOCASE index on symbols(name).
        // exact パスがインデックス（idx_symbols_name_nocase）を使える形に保つための回帰テスト。
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "EXPLAIN QUERY PLAN SELECT s.name FROM symbols s WHERE s.name = @q COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@q", "authenticate");
        using var reader = cmd.ExecuteReader();
        var plan = new System.Text.StringBuilder();
        while (reader.Read())
            plan.AppendLine(reader.GetString(3));
        var planText = plan.ToString();
        Assert.Contains("idx_symbols_name_nocase", planText);
        Assert.DoesNotContain("SCAN symbols", planText);
    }

    [Fact]
    public void SearchSymbols_EmptyNameListBehavesLikeNoFilter()
    {
        var all = _reader.SearchSymbols((IReadOnlyList<string>?)null);
        var empty = _reader.SearchSymbols(new string[0]);
        Assert.Equal(all.Count, empty.Count);
    }

    [Fact]
    public void SearchSymbols_FiltersByKind()
    {
        var classes = _reader.SearchSymbols(kind: "class");
        Assert.Single(classes);
        Assert.Equal("ApiClient", classes[0].Name);

        var functions = _reader.SearchSymbols(kind: "function");
        Assert.Equal(2, functions.Count);
    }

    [Fact]
    public void SearchSymbols_FiltersByLanguage()
    {
        var pySymbols = _reader.SearchSymbols(lang: "python");
        Assert.Single(pySymbols);

        var jsSymbols = _reader.SearchSymbols(lang: "javascript");
        Assert.Equal(2, jsSymbols.Count);
    }

    [Fact]
    public void SearchSymbols_AllFilters()
    {
        // Combine kind + lang filter / 種別+言語フィルタの組み合わせ
        var results = _reader.SearchSymbols(query: "fetch", kind: "function", lang: "javascript");
        Assert.Single(results);
        Assert.Equal("fetchData", results[0].Name);
    }

    [Fact]
    public void SearchSymbols_ExcludeTests_RemovesLikelyTestPaths()
    {
        var testFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "tests/auth_test.py", Lang = "python", Size = 300, Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertSymbols([
            new SymbolRecord { FileId = testFileId, Kind = "function", Name = "authenticate", Line = 1, StartLine = 1, EndLine = 1 },
        ]);

        var results = _reader.SearchSymbols(query: "authenticate", excludeTests: true);

        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
    }

    [Fact]
    public void Search_ExcludeTests_RemovesLikelyTestPaths()
    {
        var testFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "tests/auth_test.py", Lang = "python", Size = 300, Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = testFileId, ChunkIndex = 0, StartLine = 1, EndLine = 3,
            Content = "def authenticate_test_case():\n    authenticate('a', 'b')\n    return True",
        }]);

        var results = _reader.Search("authenticate", limit: 5, excludeTests: true);

        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
    }

    [Fact]
    public void SearchReferences_FindsIndexedCallSites()
    {
        InsertIndexedFile("src/session.py", "python", "def login(user, password):\n    return authenticate(user, password)\n");

        var results = _reader.SearchReferences("authenticate");

        var reference = Assert.Single(results);
        Assert.Equal("src/session.py", reference.Path);
        Assert.Equal("call", reference.ReferenceKind);
        Assert.Equal("login", reference.ContainerName);
    }

    [Fact]
    public void GetCallers_ReturnsCallingFunctions()
    {
        InsertIndexedFile("src/session.py", "python", "def login(user, password):\n    return authenticate(user, password)\n");

        var results = _reader.GetCallers("authenticate");

        var caller = Assert.Single(results);
        Assert.Equal("src/session.py", caller.Path);
        Assert.Equal("login", caller.CallerName);
        Assert.Equal("authenticate", caller.CalleeName);
        Assert.Equal(1, caller.ReferenceCount);
    }

    [Fact]
    public void GetCallees_ReturnsReferencedSymbolsForCaller()
    {
        InsertIndexedFile("src/session.py", "python", "def login(user, password):\n    return authenticate(user, password)\n");

        var results = _reader.GetCallees("login");

        var callee = Assert.Single(results);
        Assert.Equal("src/session.py", callee.Path);
        Assert.Equal("login", callee.CallerName);
        Assert.Equal("authenticate", callee.CalleeName);
        Assert.Equal("call", callee.ReferenceKind);
    }

    [Fact]
    public void GetSymbolHotspots_CountsSameNameReferencesPerSymbolFile()
    {
        InsertIndexedFile("src/hotspots_alpha.py", "python",
            "def Shared():\n    return True\n\n" +
            "def alpha_use():\n    Shared()\n    Shared()\n");
        InsertIndexedFile("src/hotspots_beta.py", "python",
            "def Shared():\n    return True\n\n" +
            "def beta_use():\n    Shared()\n");
        InsertIndexedFile("src/hotspots_gamma.py", "python",
            "def gamma_use():\n    Shared()\n");

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "python",
            pathPatterns: ["src/hotspots_alpha.py", "src/hotspots_beta.py"],
            excludePathPatterns: null,
            excludeTests: false);

        var shared = results
            .Where(result => result.Symbol.Name == "Shared")
            .OrderBy(result => result.Symbol.Path, StringComparer.Ordinal)
            .ToList();

        Assert.Collection(shared,
            alpha =>
            {
                Assert.Equal("src/hotspots_alpha.py", alpha.Symbol.Path);
                Assert.Equal(2, alpha.ReferenceCount);
            },
            beta =>
            {
                Assert.Equal("src/hotspots_beta.py", beta.Symbol.Path);
                Assert.Equal(1, beta.ReferenceCount);
            });
    }

    [Fact]
    public void GetSymbolHotspots_PathFilterStillTreatsOutOfScopeDuplicateAsAmbiguous()
    {
        InsertIndexedFile("src/hotspots_alpha.py", "python",
            "def Shared():\n    return True\n\n" +
            "def alpha_use():\n    Shared()\n");
        InsertIndexedFile("src/hotspots_beta.py", "python",
            "def Shared():\n    return True\n\n" +
            "def beta_use():\n    Shared()\n    Shared()\n");
        InsertIndexedFile("src/hotspots_gamma.py", "python",
            "def gamma_use():\n    Shared()\n");

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "python",
            pathPatterns: ["src/hotspots_alpha.py"],
            excludePathPatterns: null,
            excludeTests: false);

        var shared = Assert.Single(results.Where(result => result.Symbol.Name == "Shared"));
        Assert.Equal("src/hotspots_alpha.py", shared.Symbol.Path);
        Assert.Equal(1, shared.ReferenceCount);
    }

    [Fact]
    public void GetSymbolHotspots_CountsCrossFileReferencesForUniqueName()
    {
        InsertIndexedFile("src/api.py", "python", "def SharedApi():\n    return True\n");
        InsertIndexedFile("src/use1.py", "python",
            "def use_one():\n    SharedApi()\n    SharedApi()\n");
        InsertIndexedFile("src/use2.py", "python",
            "def use_two():\n    SharedApi()\n");

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "python",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        var sharedApi = Assert.Single(results.Where(result => result.Symbol.Name == "SharedApi"));
        Assert.Equal("src/api.py", sharedApi.Symbol.Path);
        Assert.Equal(3, sharedApi.ReferenceCount);
    }

    [Fact]
    public void GetSymbolHotspots_KeepsCrossFileCountsForSameContainerOverloadFamily()
    {
        InsertIndexedFile("src/api.cs", "csharp",
            """
            public class Api
            {
                public void Run() { }
                public void Run(int value) { }
            }
            """);
        InsertIndexedFile("src/caller.cs", "csharp",
            """
            public class Caller
            {
                public void Call(Api api)
                {
                    api.Run();
                    api.Run(1);
                }
            }
            """);

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        var run = Assert.Single(results.Where(result => result.Symbol.Name == "Run"));
        Assert.Equal("src/api.cs", run.Symbol.Path);
        Assert.Equal("Api", run.Symbol.ContainerName);
        Assert.Equal(2, run.ReferenceCount);
    }

    [Fact]
    public void GetSymbolHotspots_KeepsCrossFileCountsForPartialClassOverloadFamily()
    {
        InsertIndexedFile("src/Api.Part1.cs", "csharp",
            """
            public partial class Api
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/Api.Part2.cs", "csharp",
            """
            public partial class Api
            {
                public void Run(int value) { }
            }
            """);
        InsertIndexedFile("src/Caller.cs", "csharp",
            """
            public class Caller
            {
                public void Call(Api api)
                {
                    api.Run();
                    api.Run(1);
                }
            }
            """);

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        var run = Assert.Single(results.Where(result => result.Symbol.Name == "Run"));
        Assert.Equal("Api", run.Symbol.ContainerName);
        Assert.Equal(2, run.ReferenceCount);
    }

    [Fact]
    public void GetSymbolHotspots_SeparatesSameSimpleContainerNamesAcrossNamespaces()
    {
        InsertIndexedFile("src/One.Api.cs", "csharp",
            """
            namespace One;

            public class Api
            {
                public void Run() { }

                public void LocalOne()
                {
                    Run();
                }
            }
            """);
        InsertIndexedFile("src/Two.Api.cs", "csharp",
            """
            namespace Two;

            public class Api
            {
                public void Run() { }

                public void LocalTwo()
                {
                    Run();
                }
            }
            """);

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        var runs = results
            .Where(result => result.Symbol.Name == "Run")
            .OrderBy(result => result.Symbol.Path, StringComparer.Ordinal)
            .ToList();

        Assert.Collection(runs,
            first =>
            {
                Assert.Equal("src/One.Api.cs", first.Symbol.Path);
                Assert.Equal("Api", first.Symbol.ContainerName);
                Assert.Equal(1, first.ReferenceCount);
            },
            second =>
            {
                Assert.Equal("src/Two.Api.cs", second.Symbol.Path);
                Assert.Equal("Api", second.Symbol.ContainerName);
                Assert.Equal(1, second.ReferenceCount);
            });
    }

    [Fact]
    public void GetSymbolHotspots_DoesNotMergeSameContainerNameAcrossPythonModules()
    {
        InsertIndexedFile("src/alpha.py", "python",
            """
            class Api:
                def Run(self):
                    return True

                def Use(self):
                    self.Run()
                    self.Run()
            """);
        InsertIndexedFile("src/beta.py", "python",
            """
            class Api:
                def Run(self):
                    return True

                def Use(self):
                    self.Run()
                    self.Run()
            """);

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "python",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        var runs = results
            .Where(result => result.Symbol.Name == "Run")
            .OrderBy(result => result.Symbol.Path, StringComparer.Ordinal)
            .ToList();

        Assert.Collection(runs,
            first =>
            {
                Assert.Equal("src/alpha.py", first.Symbol.Path);
                Assert.Equal("Api", first.Symbol.ContainerName);
                Assert.Equal(2, first.ReferenceCount);
            },
            second =>
            {
                Assert.Equal("src/beta.py", second.Symbol.Path);
                Assert.Equal("Api", second.Symbol.ContainerName);
                Assert.Equal(2, second.ReferenceCount);
            });
    }

    [Fact]
    public void GetSymbolHotspots_DoesNotMergeSameQualifiedTypeAcrossProjectRoots()
    {
        InsertIndexedFile("projA/src/Api.cs", "csharp",
            """
            namespace Shared;

            public class Api
            {
                public void Run() { }

                public void LocalA()
                {
                    Run();
                }
            }
            """);
        InsertIndexedFile("projB/src/Api.cs", "csharp",
            """
            namespace Shared;

            public class Api
            {
                public void Run() { }

                public void LocalB()
                {
                    Run();
                }
            }
            """);

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["projA/", "projB/"],
            excludePathPatterns: null,
            excludeTests: false);

        var runs = results
            .Where(result => result.Symbol.Name == "Run")
            .OrderBy(result => result.Symbol.Path, StringComparer.Ordinal)
            .ToList();

        Assert.Collection(runs,
            first =>
            {
                Assert.Equal("projA/src/Api.cs", first.Symbol.Path);
                Assert.Equal("Api", first.Symbol.ContainerName);
                Assert.Equal(1, first.ReferenceCount);
            },
            second =>
            {
                Assert.Equal("projB/src/Api.cs", second.Symbol.Path);
                Assert.Equal("Api", second.Symbol.ContainerName);
                Assert.Equal(1, second.ReferenceCount);
            });
    }

    [Fact]
    public void GetSymbolHotspots_DoesNotMergePartialFamiliesAcrossProjectRoots()
    {
        InsertIndexedFile("projA/src/Api.Part1.cs", "csharp",
            """
            namespace Shared;

            public partial class Api
            {
                public void Run()
                {
                    Run(1);
                }
            }
            """,
            familyScopeKey: "projA");
        InsertIndexedFile("projA/src/Api.Part2.cs", "csharp",
            """
            namespace Shared;

            public partial class Api
            {
                public void Run(int value) { }
            }
            """,
            familyScopeKey: "projA");
        InsertIndexedFile("projB/src/Api.Part1.cs", "csharp",
            """
            namespace Shared;

            public partial class Api
            {
                public void Run()
                {
                    Run(1);
                }
            }
            """,
            familyScopeKey: "projB");
        InsertIndexedFile("projB/src/Api.Part2.cs", "csharp",
            """
            namespace Shared;

            public partial class Api
            {
                public void Run(int value) { }
            }
            """,
            familyScopeKey: "projB");

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["projA/", "projB/"],
            excludePathPatterns: null,
            excludeTests: false);

        var runs = results
            .Where(result => result.Symbol.Name == "Run")
            .OrderBy(result => result.Symbol.Path, StringComparer.Ordinal)
            .ToList();

        Assert.Collection(runs,
            first =>
            {
                Assert.Equal("projA/src/Api.Part1.cs", first.Symbol.Path);
                Assert.Equal("Api", first.Symbol.ContainerName);
                Assert.Equal(1, first.ReferenceCount);
            },
            second =>
            {
                Assert.Equal("projB/src/Api.Part1.cs", second.Symbol.Path);
                Assert.Equal("Api", second.Symbol.ContainerName);
                Assert.Equal(1, second.ReferenceCount);
            });
    }

    [Fact]
    public void GetSymbolHotspots_KeepsCrossFileCountsForVbPartialClassFamily()
    {
        InsertIndexedFile("src/Api.Part1.vb", "vb",
            """
            Public Partial Class Api
                Public Sub Run()
                End Sub
            End Class
            """);
        InsertIndexedFile("src/Api.Part2.vb", "vb",
            """
            Public Partial Class Api
                Public Sub Run(value As Integer)
                End Sub
            End Class
            """);
        InsertIndexedFile("src/Caller.vb", "vb",
            """
            Public Class Caller
                Public Sub Call(api As Api)
                    api.Run()
                    api.Run(1)
                End Sub
            End Class
            """);

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "vb",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        var run = Assert.Single(results.Where(result => result.Symbol.Name == "Run"));
        Assert.Equal("Api", run.Symbol.ContainerName);
        Assert.Equal(2, run.ReferenceCount);
    }

    [Fact]
    public void GetSymbolHotspots_GroupedFamilyReturnsRealDefinitionLocation()
    {
        InsertIndexedFile("src/APart.cs", "csharp",
            """
            public partial class Api
            {
                public void Helper()
                {
                }

                public void Run()
                {
                }
            }
            """);
        InsertIndexedFile("src/BPart.cs", "csharp",
            """
            public partial class Api
            {
                public void Run(int value)
                {
                }
            }
            """);
        InsertIndexedFile("src/Caller.cs", "csharp",
            """
            public class Caller
            {
                public void Call(Api api)
                {
                    api.Run();
                    api.Run(1);
                }
            }
            """);

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        var run = Assert.Single(results.Where(result => result.Symbol.Name == "Run"));
        Assert.Equal(2, run.ReferenceCount);

        var definitions = _reader.SearchSymbols(
            "Run",
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false,
            exact: true);

        Assert.Contains(definitions, definition =>
            definition.Path == run.Symbol.Path &&
            definition.Line == run.Symbol.Line &&
            definition.Name == run.Symbol.Name);
    }

    [Fact]
    public void GetSymbolHotspots_CollapsesSameFileDuplicateNames()
    {
        InsertIndexedFile("src/duplicate_names.py", "python",
            "def Run():\n    return True\n\n" +
            "def Run(value=None):\n    return value\n\n" +
            "def caller():\n    Run()\n");

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "python",
            pathPatterns: ["src/duplicate_names.py"],
            excludePathPatterns: null,
            excludeTests: false);

        var runResults = results
            .Where(result => result.Symbol.Name == "Run")
            .ToList();

        var run = Assert.Single(runResults);
        Assert.Equal("src/duplicate_names.py", run.Symbol.Path);
        Assert.Equal(1, run.ReferenceCount);
        Assert.Equal(1, run.Symbol.Line);
    }

    [Fact]
    public void GetSymbolHotspots_WithoutHotspotFamilyReadyFallsBackForMixedPartialFamilies()
    {
        InsertIndexedFile("src/Api.Part1.cs", "csharp",
            """
            public partial class Api
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/Api.Part2.cs", "csharp",
            """
            public partial class Api
            {
                public void Run(int value) { }
            }
            """);
        InsertIndexedFile("src/Caller.cs", "csharp",
            """
            public class Caller
            {
                public void Call(Api api)
                {
                    api.Run();
                    api.Run(1);
                }
            }
            """);

        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE symbols
                SET family_key = NULL
                WHERE file_id IN (
                    SELECT id FROM files WHERE path = 'src/Api.Part2.cs'
                )
                """;
            cmd.ExecuteNonQuery();
        }
        _writer.SetMeta(DbContext.GetHotspotFamilyVersionMetaKey("csharp"), null);

        var reader = new DbReader(_db.Connection);
        var results = reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        Assert.DoesNotContain(results, result => result.Symbol.Name == "Run");
    }

    [Fact]
    public void GetHotspotFamilySignal_LegacyPartialFamiliesWithoutPersistedKeysAreStillDegraded()
    {
        InsertIndexedFile("src/Api.Part1.cs", "csharp",
            """
            public partial class Api
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/Api.Part2.cs", "csharp",
            """
            public partial class Api
            {
                public void Run(int value) { }
            }
            """);
        InsertIndexedFile("src/Caller.cs", "csharp",
            """
            public class Caller
            {
                public void Call(Api api)
                {
                    api.Run();
                    api.Run(1);
                }
            }
            """);

        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE symbols
                SET family_key = NULL,
                    container_qualified_name = NULL
                WHERE file_id IN (
                    SELECT id FROM files WHERE lang = 'csharp'
                )
                """;
            cmd.ExecuteNonQuery();
        }
        _writer.SetMeta(DbContext.GetHotspotFamilyVersionMetaKey("csharp"), null);
        _writer.SetMeta(DbContext.GetHotspotFamilyMarkerFingerprintMetaKey("csharp"), null);

        var reader = new DbReader(_db.Connection);
        var signal = reader.GetHotspotFamilySignal("csharp");

        Assert.True(signal.Relevant);
        Assert.False(signal.Ready);
        Assert.Contains("csharp", signal.DegradedReason);
    }

    [Fact]
    public void GetHotspotFamilySignal_MissingMarkerFingerprintIsStillDegraded()
    {
        InsertIndexedFile("src/Api.Part1.cs", "csharp",
            """
            public partial class Api
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/Api.Part2.cs", "csharp",
            """
            public partial class Api
            {
                public void Run(int value) { }
            }
            """);
        InsertIndexedFile("src/Caller.cs", "csharp",
            """
            public class Caller
            {
                public void Call(Api api)
                {
                    api.Run();
                    api.Run(1);
                }
            }
            """);

        _writer.SetMeta(DbContext.GetHotspotFamilyMarkerFingerprintMetaKey("csharp"), null);

        var reader = new DbReader(_db.Connection);
        var signal = reader.GetHotspotFamilySignal("csharp");

        Assert.True(signal.Relevant);
        Assert.False(signal.Ready);
        Assert.Contains("csharp", signal.DegradedReason);

        var results = reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        Assert.DoesNotContain(results, result => result.Symbol.Name == "Run");
    }

    [Fact]
    public void GetSymbolHotspots_StaleVersionOneMarkerlessFamiliesDegradeAndDoNotMerge()
    {
        InsertIndexedFile("projA/src/Api.Part1.cs", "csharp",
            """
            namespace Shared;

            public partial class Api
            {
                public void Run()
                {
                    Run(1);
                }
            }
            """,
            familyScopeKey: ".");
        InsertIndexedFile("projA/src/Api.Part2.cs", "csharp",
            """
            namespace Shared;

            public partial class Api
            {
                public void Run(int value) { }
            }
            """,
            familyScopeKey: ".");
        InsertIndexedFile("projB/src/Api.Part1.cs", "csharp",
            """
            namespace Shared;

            public partial class Api
            {
                public void Run()
                {
                    Run(1);
                }
            }
            """,
            familyScopeKey: ".");
        InsertIndexedFile("projB/src/Api.Part2.cs", "csharp",
            """
            namespace Shared;

            public partial class Api
            {
                public void Run(int value) { }
            }
            """,
            familyScopeKey: ".");

        _writer.SetMeta(DbContext.GetHotspotFamilyVersionMetaKey("csharp"), "1");
        _writer.SetMeta(DbContext.GetHotspotFamilyMarkerFingerprintMetaKey("csharp"), "stale-v1");

        var reader = new DbReader(_db.Connection);
        var signal = reader.GetHotspotFamilySignal("csharp");
        Assert.True(signal.Relevant);
        Assert.False(signal.Ready);
        Assert.Contains("csharp", signal.DegradedReason);

        var runs = reader.GetSymbolHotspots(
                limit: 10,
                kind: "function",
                lang: "csharp",
                pathPatterns: ["projA/", "projB/"],
                excludePathPatterns: null,
                excludeTests: false)
            .Where(result => result.Symbol.Name == "Run")
            .OrderBy(result => result.Symbol.Path, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, runs.Count);
        Assert.StartsWith("projA/src/Api.Part", runs[0].Symbol.Path, StringComparison.Ordinal);
        Assert.Equal(1, runs[0].ReferenceCount);
        Assert.StartsWith("projB/src/Api.Part", runs[1].Symbol.Path, StringComparison.Ordinal);
        Assert.Equal(1, runs[1].ReferenceCount);
    }

    [Fact]
    public void GetSymbolHotspots_DoesNotPromoteSameFileDifferentContainersToGlobalCounts()
    {
        InsertIndexedFile("src/Duplicate.cs", "csharp",
            """
            public class A
            {
                public void Run() { }
            }

            public class B
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/Caller.cs", "csharp",
            """
            public class Caller
            {
                public void Call(A api)
                {
                    api.Run();
                    api.Run();
                }
            }
            """);

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        Assert.DoesNotContain(results, result => result.Symbol.Name == "Run");
    }

    [Fact]
    public void GetSymbolHotspots_DoesNotCountAmbiguousSameFileSiblingContainerReferences()
    {
        InsertIndexedFile("src/Duplicate.cs", "csharp",
            """
            public class A
            {
                public void Run() { }

                public void CallA()
                {
                    Run();
                }
            }

            public class B
            {
                public void Run() { }
            }
            """);

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        Assert.DoesNotContain(results, result => result.Symbol.Name == "Run");
    }

    [Fact]
    public void GetSymbolHotspots_LangFilterIgnoresCrossLanguageReferences()
    {
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/Caller.cs", "csharp",
            """
            public class Caller
            {
                public void Call(App app)
                {
                    app.Run();
                    app.Run();
                }
            }
            """);
        InsertIndexedFile("src/tool.py", "python",
            """
            def helper():
                Run()
            """);

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        var run = Assert.Single(results.Where(result => result.Symbol.Name == "Run"));
        Assert.Equal("src/App.cs", run.Symbol.Path);
        Assert.Equal(2, run.ReferenceCount);
    }

    [Fact]
    public void GetSymbolHotspots_CrossLanguageDefinitionsDoNotSuppressSameLanguageHotspots()
    {
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/Caller.cs", "csharp",
            """
            public class Caller
            {
                public void Call(App app)
                {
                    app.Run();
                    app.Run();
                }
            }
            """);
        InsertIndexedFile("src/tool.py", "python",
            """
            def Run():
                return True
            """);

        var results = _reader.GetSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: null,
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        var run = Assert.Single(results.Where(result => result.Symbol.Name == "Run" && result.Symbol.Lang == "csharp"));
        Assert.Equal("src/App.cs", run.Symbol.Path);
        Assert.Equal(2, run.ReferenceCount);
    }

    [Fact]
    public void GraphReaders_ExactMatchesNameEquality()
    {
        // Seed content where `authenticate_v2` is both CALLED (so it appears as a reference
        // `symbol_name`) and calls `authenticate` (so it appears as a `container_name`). Substring
        // mode for `authenticate` matches both rows; exact mode returns only `authenticate`.
        // Mirrors the semantics codex nailed in #81 — case-insensitive equality, no substring expansion.
        // authenticate_v2 を呼び出しもし、中から authenticate も呼び出す内容を仕込む。
        InsertIndexedFile("src/auth_v2.py", "python",
            "def authenticate_v2(user, password):\n    authenticate(user, password)\n    return True\n\n" +
            "def wrapper(u, p):\n    return authenticate_v2(u, p)\n");

        // references
        var refsSub = _reader.SearchReferences("authenticate", exact: false)
            .Select(r => r.SymbolName).Distinct().OrderBy(n => n).ToList();
        Assert.Contains("authenticate", refsSub);
        Assert.Contains("authenticate_v2", refsSub);

        var refsExact = _reader.SearchReferences("authenticate", exact: true)
            .Select(r => r.SymbolName).Distinct().ToList();
        Assert.Equal(new[] { "authenticate" }, refsExact);

        // callers (filter on callee / symbol_name)
        var callersSub = _reader.GetCallers("authenticate", exact: false)
            .Select(r => r.CalleeName).Distinct().OrderBy(n => n).ToList();
        Assert.Contains("authenticate", callersSub);
        Assert.Contains("authenticate_v2", callersSub);

        var callersExact = _reader.GetCallers("authenticate", exact: true)
            .Select(r => r.CalleeName).Distinct().ToList();
        Assert.Equal(new[] { "authenticate" }, callersExact);

        // callees (filter on container_name)
        var calleesSub = _reader.GetCallees("authenticate", exact: false)
            .Select(r => r.CallerName).Distinct().OrderBy(n => n ?? "").ToList();
        Assert.Contains("authenticate_v2", calleesSub);

        var calleesExact = _reader.GetCallees("authenticate", exact: true)
            .Select(r => r.CallerName).Distinct().ToList();
        Assert.DoesNotContain("authenticate_v2", calleesExact);

        // Case-insensitive equality across all three.
        Assert.Single(_reader.SearchReferences("AUTHENTICATE", exact: true));
        Assert.Single(_reader.GetCallers("AUTHENTICATE", exact: true));
    }

    [Fact]
    public void GraphReaders_ExactPrefersExactCaseOverFoldSibling()
    {
        InsertIndexedFile("src/a_case.py", "python",
            "def apiTwin():\n    authenticate('a', 'b')\n    return True\n\n" +
            "def lower_wrapper():\n    return apiTwin()\n");
        InsertIndexedFile("tests/z_case.py", "python",
            "def ApiTwin():\n    authenticate('a', 'b')\n    return True\n\n" +
            "def upper_wrapper():\n    return ApiTwin()\n");

        var references = _reader.SearchReferences("ApiTwin", exact: true)
            .Where(r => r.SymbolName is "ApiTwin" or "apiTwin")
            .Select(r => r.SymbolName)
            .Distinct()
            .Take(2)
            .ToList();
        Assert.Equal(new[] { "ApiTwin", "apiTwin" }, references);

        var callers = _reader.GetCallers("ApiTwin", exact: true)
            .Where(r => r.CalleeName is "ApiTwin" or "apiTwin")
            .Select(r => r.CalleeName)
            .Distinct()
            .Take(2)
            .ToList();
        Assert.Equal(new[] { "ApiTwin", "apiTwin" }, callers);

        var callees = _reader.GetCallees("ApiTwin", exact: true)
            .Where(r => r.CallerName is "ApiTwin" or "apiTwin")
            .Select(r => r.CallerName)
            .Distinct()
            .Take(2)
            .ToList();
        Assert.Equal(new[] { "ApiTwin", "apiTwin" }, callees);

        var topReference = Assert.Single(_reader.SearchReferences("ApiTwin", limit: 1, exact: true));
        Assert.Equal("ApiTwin", topReference.SymbolName);
        Assert.Equal("tests/z_case.py", topReference.Path);

        var topCaller = Assert.Single(_reader.GetCallers("ApiTwin", limit: 1, exact: true));
        Assert.Equal("ApiTwin", topCaller.CalleeName);
        Assert.Equal("tests/z_case.py", topCaller.Path);

        var topCallee = Assert.Single(_reader.GetCallees("ApiTwin", limit: 1, exact: true));
        Assert.Equal("ApiTwin", topCallee.CallerName);
        Assert.Equal("tests/z_case.py", topCallee.Path);
    }

    [Fact]
    public void GetTransitiveCallers_ExactUsesUnicodeFoldForResolutionAndCallerMatch()
    {
        // Regression for #93: impact BFS used ASCII-only equality in both ResolveSymbolName()
        // and GetCallersExact(), so a mixed fullwidth/non-ASCII query could miss even when
        // the definition and caller rows were both present and fold-equivalent.
        // #93 回帰: impact BFS の 2 箇所が ASCII-only 比較だったため、fullwidth と
        // 非 ASCII 大文字を含むクエリで definition / caller が両方揃っていても取りこぼした。
        var symbolFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/intl.py",
            Lang = "python",
            Size = 48,
            Lines = 2,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = symbolFileId,
            ChunkIndex = 0,
            StartLine = 1,
            EndLine = 2,
            Content = "def café_init():\n    return True\n",
        }]);
        _writer.InsertSymbols([
            new SymbolRecord
            {
                FileId = symbolFileId,
                Kind = "function",
                Name = "café_init",
                Line = 1,
                StartLine = 1,
                EndLine = 2,
                BodyStartLine = 2,
                BodyEndLine = 2,
                Signature = "def café_init():",
            },
        ]);

        var callerFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/bootstrap.py",
            Lang = "python",
            Size = 58,
            Lines = 2,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = callerFileId,
            ChunkIndex = 0,
            StartLine = 1,
            EndLine = 2,
            Content = "def bootstrap():\n    return CAFÉ_INIT()\n",
        }]);
        _writer.InsertSymbols([
            new SymbolRecord
            {
                FileId = callerFileId,
                Kind = "function",
                Name = "bootstrap",
                Line = 1,
                StartLine = 1,
                EndLine = 2,
                BodyStartLine = 2,
                BodyEndLine = 2,
                Signature = "def bootstrap():",
            },
        ]);
        _writer.InsertReferences([
            new ReferenceRecord
            {
                FileId = callerFileId,
                SymbolName = "CAFÉ_INIT",
                ReferenceKind = "call",
                Line = 2,
                Column = 12,
                Context = "return CAFÉ_INIT()",
                ContainerKind = "function",
                ContainerName = "bootstrap",
            },
        ]);

        var (results, truncated) = _reader.GetTransitiveCallers("ＣＡＦÉ_ＩＮＩＴ", maxDepth: 1, limit: 10);

        Assert.False(truncated);
        var caller = Assert.Single(results);
        Assert.Equal("src/bootstrap.py", caller.Path);
        Assert.Equal("bootstrap", caller.CallerName);
        Assert.Equal("CAFÉ_INIT", caller.CalleeName);
        Assert.Equal(1, caller.Depth);
    }

    [Fact]
    public void GetDefinitions_ExactMatchesNameEquality()
    {
        var extraFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/auth_v2.py", Lang = "python", Size = 80, Lines = 4,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = extraFileId, ChunkIndex = 0, StartLine = 1, EndLine = 1,
            Content = "def authenticate_v2(user, password): pass",
        }]);
        _writer.InsertSymbols([
            new SymbolRecord { FileId = extraFileId, Kind = "function", Name = "authenticate_v2", Line = 1, StartLine = 1, EndLine = 1 },
        ]);

        var substring = _reader.GetDefinitions("authenticate", exact: false)
            .Select(r => r.Name).Distinct().OrderBy(n => n).ToList();
        Assert.Contains("authenticate", substring);
        Assert.Contains("authenticate_v2", substring);

        var exact = _reader.GetDefinitions("authenticate", exact: true)
            .Select(r => r.Name).Distinct().ToList();
        Assert.Equal(new[] { "authenticate" }, exact);
    }

    [Fact]
    public void AnalyzeSymbol_ExactPropagatesToBundledSubQueries()
    {
        // The bundled one-round-trip path (`inspect` / MCP `analyze_symbol`) must propagate
        // `exact` into every sub-query — otherwise the bundle keeps returning RunAsync/RunImpact
        // spillover even when the caller asked for precision. Codex adversarial review of #83.
        // bundle 側も `exact` を尊重すること（definitions / references / callers / callees）。
        InsertIndexedFile("src/auth_v2.py", "python",
            "def authenticate_v2(user, password):\n    authenticate(user, password)\n    return True\n\n" +
            "def wrapper(u, p):\n    return authenticate_v2(u, p)\n");

        var exactBundle = _reader.AnalyzeSymbol("authenticate", exact: true);
        Assert.All(exactBundle.Definitions, d => Assert.Equal("authenticate", d.Name));
        Assert.All(exactBundle.References, r => Assert.Equal("authenticate", r.SymbolName));
        Assert.All(exactBundle.Callers, c => Assert.Equal("authenticate", c.CalleeName));
        // Callees are filtered on container_name, so exact must reject `authenticate_v2` as a container.
        // callees は container_name で絞るため、authenticate_v2 を含んではいけない。
        Assert.DoesNotContain(exactBundle.Callees, c => c.CallerName == "authenticate_v2");

        var substringBundle = _reader.AnalyzeSymbol("authenticate", exact: false);
        Assert.Contains(substringBundle.Definitions, d => d.Name == "authenticate_v2");
    }

    [Fact]
    public void AnalyzeSymbol_ExactZeroHint_OnlyWhenWholeBundleIsEmpty()
    {
        InsertIndexedFile("src/handlers.cs", "csharp",
            """
            public class Handler
            {
                public void HandleRequest() { }
                public void HandleRequestAsync() { HandleRequest(); }
            }
            """);

        var exactMiss = _reader.AnalyzeSymbol("HandleRe", exact: true);
        Assert.NotNull(exactMiss.ExactZeroHint);
        Assert.Equal(2, exactMiss.ExactZeroHint!.RelaxedCount);
        Assert.Contains("HandleRequest", exactMiss.ExactZeroHint.SampleNames);
        Assert.Contains("HandleRequestAsync", exactMiss.ExactZeroHint.SampleNames);

        var exactHit = _reader.AnalyzeSymbol("HandleRequest", exact: true);
        Assert.Null(exactHit.ExactZeroHint);
    }

    [Fact]
    public void GraphReaders_ExactPredicatesAreIndexable()
    {
        // Guard: `references / callers / callees --exact` must stay SARGable so SQLite can
        // pick the new NOCASE covering indexes on symbol_references(symbol_name / container_name).
        // Mirrors SearchSymbols_ExactPredicateIsIndexable from #81.
        // references / callers / callees --exact 用の NOCASE index 使用を固定する回帰テスト。
        using var cmdRef = _db.Connection.CreateCommand();
        cmdRef.CommandText = "EXPLAIN QUERY PLAN SELECT r.line FROM symbol_references r WHERE r.symbol_name = @q COLLATE NOCASE";
        cmdRef.Parameters.AddWithValue("@q", "authenticate");
        var refPlan = new System.Text.StringBuilder();
        using (var rr = cmdRef.ExecuteReader())
            while (rr.Read()) refPlan.AppendLine(rr.GetString(3));
        Assert.Contains("idx_symbol_refs_name_nocase", refPlan.ToString());

        using var cmdCon = _db.Connection.CreateCommand();
        cmdCon.CommandText = "EXPLAIN QUERY PLAN SELECT r.line FROM symbol_references r WHERE r.container_name = @q COLLATE NOCASE";
        cmdCon.Parameters.AddWithValue("@q", "login");
        var conPlan = new System.Text.StringBuilder();
        using (var cr = cmdCon.ExecuteReader())
            while (cr.Read()) conPlan.AppendLine(cr.GetString(3));
        Assert.Contains("idx_symbol_refs_container_nocase", conPlan.ToString());
    }

    [Fact]
    public void GraphReaders_IgnoreLegacyReferencesFromUnsupportedLanguages()
    {
        var pythonFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/session.py",
            Lang = "python",
            Size = 80,
            Lines = 2,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = pythonFileId,
            ChunkIndex = 0,
            StartLine = 1,
            EndLine = 2,
            Content = "def login(user, password):\n    return authenticate(user, password)\n",
        }]);
        _writer.InsertReferences([
            new ReferenceRecord
            {
                FileId = pythonFileId,
                SymbolName = "authenticate",
                ReferenceKind = "call",
                Line = 2,
                Column = 12,
                Context = "return authenticate(user, password)",
                ContainerKind = "function",
                ContainerName = "login",
            },
        ]);

        var shellFileId = _writer.UpsertFile(new FileRecord
        {
            Path = "scripts/legacy.sh",
            Lang = "shell",
            Size = 48,
            Lines = 2,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = shellFileId,
            ChunkIndex = 0,
            StartLine = 1,
            EndLine = 2,
            Content = "login() {\n  authenticate \"$1\"\n}\n",
        }]);
        _writer.InsertReferences([
            new ReferenceRecord
            {
                FileId = shellFileId,
                SymbolName = "authenticate",
                ReferenceKind = "call",
                Line = 2,
                Column = 3,
                Context = "authenticate \"$1\"",
                ContainerKind = "function",
                ContainerName = "login",
            },
        ]);

        var references = _reader.SearchReferences("authenticate");
        var callers = _reader.GetCallers("authenticate");
        var callees = _reader.GetCallees("login");

        var reference = Assert.Single(references);
        Assert.Equal("src/session.py", reference.Path);

        var caller = Assert.Single(callers);
        Assert.Equal("src/session.py", caller.Path);

        var callee = Assert.Single(callees);
        Assert.Equal("src/session.py", callee.Path);
    }

    [Fact]
    public void GraphQueries_DefaultCountsDeduplicateConstructorCallAndInstantiateSites()
    {
        InsertIndexedFile("src/constructor_fixture_target.cs", "csharp",
            """
            public class Target
            {
                public Target() { }
            }
            """);
        InsertIndexedFile("src/constructor_fixture_caller.cs", "csharp",
            """
            public class Caller
            {
                public void Run()
                {
                    var target = new Target();
                }
            }
            """);

        var refs = _reader.SearchReferences("Target", lang: "csharp", exact: true, pathPatterns: ["constructor_fixture"]);
        var reference = Assert.Single(refs);
        Assert.Equal("instantiate", reference.ReferenceKind);
        Assert.Equal(1, _reader.CountSearchReferences("Target", lang: "csharp", exact: true, pathPatterns: ["constructor_fixture"]));

        var caller = Assert.Single(_reader.GetCallers("Target", lang: "csharp", exact: true, pathPatterns: ["constructor_fixture"]));
        Assert.Equal("Run", caller.CallerName);
        Assert.Equal(1, caller.ReferenceCount);

        var callee = Assert.Single(_reader.GetCallees("Run", lang: "csharp", exact: true, pathPatterns: ["constructor_fixture"]));
        Assert.Equal("Target", callee.CalleeName);
        Assert.Equal("instantiate", callee.ReferenceKind);
        Assert.Equal(1, callee.ReferenceCount);

        var (impact, truncated) = _reader.GetTransitiveCallers("Target", maxDepth: 1, limit: 10, lang: "csharp", pathPatterns: ["constructor_fixture"]);
        Assert.False(truncated);
        var impactCaller = Assert.Single(impact);
        Assert.Equal("Run", impactCaller.CallerName);
        Assert.Equal(1, impactCaller.ReferenceCount);

        var hotspot = Assert.Single(_reader.GetSymbolHotspots(10, "class", "csharp", ["constructor_fixture"], null, false), item => item.Symbol.Name == "Target");
        Assert.Equal(1, hotspot.ReferenceCount);

        var dependency = Assert.Single(_reader.GetFileDependencies(limit: 10, lang: "csharp", pathPatterns: ["constructor_fixture_caller.cs"], excludePathPatterns: null, excludeTests: false));
        Assert.Equal("src/constructor_fixture_caller.cs", dependency.SourcePath);
        Assert.Equal("src/constructor_fixture_target.cs", dependency.TargetPath);
        Assert.Equal(1, dependency.ReferenceCount);
    }

    [Fact]
    public void GetFileDependencies_DoesNotJoinSameNameTargetsAcrossLanguages()
    {
        InsertIndexedFile("src/Foo.cs", "csharp",
            """
            public class Foo
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/Caller.cs", "csharp",
            """
            public class Caller
            {
                public void Call(Foo foo)
                {
                    foo.Run();
                }
            }
            """);
        InsertIndexedFile("src/foo.py", "python",
            """
            def Run():
                return True
            """);

        var dependencies = _reader.GetFileDependencies(
            limit: 10,
            lang: "csharp",
            pathPatterns: ["src/Caller.cs"],
            excludePathPatterns: null,
            excludeTests: false);

        var dependency = Assert.Single(dependencies);
        Assert.Equal("src/Caller.cs", dependency.SourcePath);
        Assert.Equal("src/Foo.cs", dependency.TargetPath);
        Assert.Equal(1, dependency.ReferenceCount);
        Assert.DoesNotContain("foo.py", dependency.TargetPath, StringComparison.Ordinal);
    }

    [Fact]
    public void GetFileDependencies_IncludesMetadataReferencesAsCompileTimeDependencies()
    {
        // issue #293 follow-up: the attribute class `JsonConverter` is referenced
        // both as a runtime `new JsonConverter(...)` call AND as compile-time
        // attribute metadata `[JsonConverter(...)]`. Renaming or removing the
        // class breaks both sites, so `cdidx deps` MUST surface both edges as
        // real file-level dependencies. (`callers` / `callees` stay call-graph-
        // only and reject `--kind attribute|annotation` separately at the CLI /
        // MCP boundary — that is a different contract.)
        // issue #293 補足: attribute クラス `JsonConverter` は runtime の
        // `new JsonConverter(...)` としても、compile-time の `[JsonConverter(...)]`
        // 属性 metadata としても参照される。クラスを rename / 削除すれば両方の
        // サイトが壊れるため、`cdidx deps` は両方のエッジをファイル単位の本物の
        // 依存として出す必要がある。(`callers` / `callees` は call-graph 専用で、
        // metadata 種別は CLI / MCP boundary 側で別途拒否する)
        InsertIndexedFile("src/JsonConverterAttribute.cs", "csharp",
            """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public class JsonConverter : Attribute
            {
                public Type ConverterType { get; }
                public JsonConverter(Type converterType) => ConverterType = converterType;
            }
            """);
        // Metadata-only usage — attribute form. Compile-time dependency: renaming
        // `JsonConverter` breaks this file at build time.
        // metadata-only の利用 (attribute 形式)。compile-time 依存:
        // `JsonConverter` を rename すればこのファイルも build-time で壊れる。
        InsertIndexedFile("src/Serializer.cs", "csharp",
            """
            [JsonConverter(typeof(int))]
            public class SerializerConfig
            {
            }
            """);
        // Runtime dependency — `new JsonConverter(...)` is a `call` / `instantiate` edge.
        // 実行時の依存 — `new JsonConverter(...)` は `call` / `instantiate` 種別の edge。
        InsertIndexedFile("src/Caller.cs", "csharp",
            """
            public class Caller
            {
                public void Do()
                {
                    var c = new JsonConverter(typeof(int));
                }
            }
            """);

        var dependencies = _reader.GetFileDependencies(limit: 10, lang: "csharp");

        // Both Caller.cs (runtime `instantiate`) and Serializer.cs (attribute
        // metadata) must appear as dependencies of JsonConverterAttribute.cs.
        // Caller.cs (runtime `instantiate`) と Serializer.cs (attribute metadata)
        // の両方が JsonConverterAttribute.cs への依存として現れる。
        Assert.Equal(2, dependencies.Count);
        Assert.Contains(dependencies, d => d.SourcePath == "src/Caller.cs" && d.TargetPath == "src/JsonConverterAttribute.cs");
        Assert.Contains(dependencies, d => d.SourcePath == "src/Serializer.cs" && d.TargetPath == "src/JsonConverterAttribute.cs");
    }

    [Fact]
    public void GetFileDependencies_MatchesCSharpAttributeSuffixConvention()
    {
        // issue #293 follow-up: C# convention — a class `FooAttribute` is used in
        // source as `[Foo]`, so the reference site is stored with symbol_name `Foo`.
        // `deps` must canonicalize these so the attribute class file is still
        // recognized as a dependency target for pure-attribute consumers.
        // issue #293 補足: C# の規約では、クラス `FooAttribute` はソース中で `[Foo]`
        // として使われるため、参照サイトは symbol_name `Foo` として保存される。
        // `deps` はこれを正規化し、attribute 専用の consumer でも attribute クラスの
        // ファイルを依存 target として認識できるようにする。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class MyAuditAttribute : Attribute
            {
            }
            """);
        // Idiomatic `[MyAudit]` usage — symbol_name recorded as `MyAudit` but target
        // class is `MyAuditAttribute`.
        // 慣用的な `[MyAudit]` 利用 — symbol_name は `MyAudit` として記録されるが
        // target クラスは `MyAuditAttribute`。
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        var dependencies = _reader.GetFileDependencies(limit: 10, lang: "csharp");

        Assert.Contains(dependencies, d => d.SourcePath == "src/Svc.cs" && d.TargetPath == "src/MyAuditAttribute.cs");
    }

    [Fact]
    public void GetFileDependencies_CSharpGenericNoArgAttribute_StillIndexedAndResolvesToAttributeClass()
    {
        // issue #293 round-15 follow-up: generic no-arg C# attributes like
        // `[MyAudit<int>]` and multi-line `[\n MyAttr<int>\n]` must still be
        // indexed as `attribute` references so `deps` can route them through
        // the suffix-alias synthesizer to the real attribute class file.
        // Before the regex was widened these forms fell through both CallRegex
        // (no `(`) and the no-arg regex (generic `<...>` after the name broke
        // the `(?=[\],]|$)` anchor), producing zero edges.
        // issue #293 round-15 補足: `[MyAudit<int>]` や複数行の `[\n MyAttr<int>\n]`
        // のようなジェネリック引数なし属性も `attribute` として取り込まれ、
        // suffix alias を経由して実属性クラスへの依存エッジに正規化される
        // こと。正規表現の拡張前は両 regex とも拾えず、エッジが 0 件だった。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            using System;

            public sealed class MyAuditAttribute<T> : Attribute
            {
            }
            """);
        InsertIndexedFile("src/MyAttrAttribute.cs", "csharp",
            """
            using System;

            public sealed class MyAttrAttribute<T> : Attribute
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit<int>]
            [
                MyAttr<int>
            ]
            public class Svc
            {
            }
            """);

        var dependencies = _reader.GetFileDependencies(limit: 20, lang: "csharp");

        Assert.Contains(dependencies, d => d.SourcePath == "src/Svc.cs" && d.TargetPath == "src/MyAuditAttribute.cs");
        Assert.Contains(dependencies, d => d.SourcePath == "src/Svc.cs" && d.TargetPath == "src/MyAttrAttribute.cs");
    }

    [Fact]
    public void GetFileDependencies_CSharpGenericNoArgAttribute_AssemblyTarget_IsIndexed()
    {
        // issue #293 round-15 follow-up: `[assembly: MyAttr<string>]` — assembly
        // targeted generic no-arg attribute must also reach the attribute class.
        // issue #293 round-15 補足: `[assembly: MyAttr<string>]` のような
        // assembly targeted ジェネリック引数なし属性も同様にインデックスされ、
        // attribute クラスに解決されること。
        InsertIndexedFile("src/MyAttrAttribute.cs", "csharp",
            """
            using System;

            public sealed class MyAttrAttribute<T> : Attribute
            {
            }
            """);
        InsertIndexedFile("src/AssemblyInfo.cs", "csharp",
            """
            [assembly: MyAttr<string>]
            """);

        var dependencies = _reader.GetFileDependencies(limit: 20, lang: "csharp");

        Assert.Contains(dependencies, d => d.SourcePath == "src/AssemblyInfo.cs" && d.TargetPath == "src/MyAttrAttribute.cs");
    }

    [Fact]
    public void GetFileDependencies_CSharpAttributeRawDoesNotLeakToBareNameClass_WhenSuffixTargetExists()
    {
        // issue #293 follow-up: `[MyAudit]` in C# is stored as symbol_name='MyAudit'.
        // When both `class MyAudit` (plain class) and `class MyAuditAttribute`
        // (the real attribute target) exist, the metadata edge must resolve only
        // to `MyAuditAttribute` via the synthetic suffix alias. Keeping the raw
        // bare-name edge would over-report: `[MyAudit]` would falsely depend on
        // the unrelated plain `class MyAudit` file.
        // issue #293 補足: C# の `[MyAudit]` は symbol_name='MyAudit' で保存される。
        // `class MyAudit` (plain) と `class MyAuditAttribute` (本物の attribute)
        // が両方あるとき、metadata エッジは synthetic suffix alias 経由で
        // `MyAuditAttribute` だけに解決されるべき。raw の bare-name エッジを
        // 残すと、`[MyAudit]` が無関係な plain `class MyAudit` のファイルにも
        // 誤って依存してしまう。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class MyAuditAttribute : Attribute
            {
            }
            """);
        InsertIndexedFile("src/PlainMyAudit.cs", "csharp",
            """
            public class MyAudit
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        var dependencies = _reader.GetFileDependencies(limit: 10, lang: "csharp");

        // Only MyAuditAttribute.cs should be a dependency target for Svc.cs;
        // PlainMyAudit.cs must not appear.
        // Svc.cs の依存先は MyAuditAttribute.cs のみで、PlainMyAudit.cs は
        // 出現してはならない。
        Assert.Contains(dependencies, d => d.SourcePath == "src/Svc.cs" && d.TargetPath == "src/MyAuditAttribute.cs");
        Assert.DoesNotContain(dependencies, d => d.SourcePath == "src/Svc.cs" && d.TargetPath == "src/PlainMyAudit.cs");
    }

    [Fact]
    public void GetFileDependencies_CSharpAttributeDoesNotLeakToSameNameMethodOrProperty()
    {
        // issue #293 follow-up: `[MyAuditAttribute]` (fully qualified) must only
        // match a class-like attribute target. A method / property named
        // `MyAuditAttribute` in an unrelated file must never show up as a deps
        // edge from the metadata reference. Non-metadata call-graph edges keep
        // their previous behavior (they can still resolve to any symbol kind).
        // issue #293 補足: `[MyAuditAttribute]` (完全形) は class 系の attribute
        // target にしか一致してはならない。別ファイルの同名メソッド/プロパティ
        // `MyAuditAttribute` が metadata 参照の deps エッジに現れてはいけない。
        // 非 metadata の call-graph エッジは従来どおり任意の kind に解決できる。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class MyAuditAttribute : Attribute
            {
            }
            """);
        InsertIndexedFile("src/Helpers.cs", "csharp",
            """
            public class Helpers
            {
                public void MyAuditAttribute()
                {
                }
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAuditAttribute]
            public class Svc
            {
            }
            """);

        var dependencies = _reader.GetFileDependencies(limit: 10, lang: "csharp");

        Assert.DoesNotContain(dependencies, d => d.SourcePath == "src/Svc.cs" && d.TargetPath == "src/Helpers.cs");
    }

    [Fact]
    public void GetFileDependencies_CSharpAttributeAmbiguityCountsSameFileDuplicateClassDefinitions()
    {
        // issue #293 follow-up: when a single source file defines TWO same-named
        // class-like attribute targets under different namespaces (idiomatic C#
        // with multiple `namespace { ... }` blocks in one .cs file), the metadata
        // edge must be dropped as ambiguous just like the multi-file case. A
        // path-level count (COUNT DISTINCT target_path) would see `count = 1`
        // because both definitions live in the same file, so the previous
        // target_ambiguity CTE falsely treated the target as unambiguous. The
        // rewritten CTE joins back through files + symbols so it counts at
        // symbol-identity level and correctly sees `count = 2`.
        // issue #293 補足: 1 つの .cs ファイルに別名前空間で同名 class-like が 2 つ
        // 定義されている場合 (C# でよくある `namespace { ... }` 複数ブロック形式)
        // でも、複数ファイルのときと同様に metadata edge は ambiguous として落とす
        // 必要がある。path 単位 (COUNT DISTINCT target_path) だと両方が同じ file に
        // あるため count=1 となり、従来の target_ambiguity では誤って一意扱いされた。
        // 書き直した CTE は files + symbols に JOIN し直すため、symbol identity 単位
        // で count=2 を正しく検出する。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            using System;

            namespace A
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class MyAuditAttribute : Attribute
                {
                }
            }

            namespace B
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class MyAuditAttribute : Attribute
                {
                }
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        var dependencies = _reader.GetFileDependencies(limit: 10, lang: "csharp");

        // Even though both MyAuditAttribute definitions live in the same file, the
        // metadata reference is still ambiguous and must not produce a deps edge.
        // 同じファイル内にある 2 つの MyAuditAttribute 定義でも metadata 参照は
        // 曖昧扱いのため、deps edge を出してはならない。
        Assert.DoesNotContain(dependencies, d => d.SourcePath == "src/Svc.cs" && d.TargetPath == "src/MyAuditAttribute.cs");
    }

    [Fact]
    public void GetFileDependencies_CSharpAttributeDoesNotFanOutWhenMultipleSameNameAttributeClasses()
    {
        // issue #293 follow-up: if multiple same-named attribute classes exist
        // (e.g. two `MyAuditAttribute` classes in separate namespaces/files),
        // a metadata reference `[MyAudit]` must not fan out to BOTH files. We
        // cannot statically resolve which one the C# compiler picks without
        // namespace / using analysis, so we drop the ambiguous metadata edge
        // and let `impact` / `references` surface both candidates to the user.
        // issue #293 補足: 同名 attribute クラスが複数ある場合 (例: 別名前空間/別
        // ファイルに 2 つの `MyAuditAttribute` がある場合)、metadata 参照
        // `[MyAudit]` を両方に fan-out させない。cdidx は namespace / using を
        // 解析しないため正しい解決ができず、あいまいな metadata エッジは落として
        // 両候補は `impact` / `references` 経由でユーザーに示す。
        InsertIndexedFile("src/A/MyAuditAttribute.cs", "csharp",
            """
            using System;

            namespace A
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class MyAuditAttribute : Attribute
                {
                }
            }
            """);
        InsertIndexedFile("src/B/MyAuditAttribute.cs", "csharp",
            """
            using System;

            namespace B
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class MyAuditAttribute : Attribute
                {
                }
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        var dependencies = _reader.GetFileDependencies(limit: 10, lang: "csharp");

        // Neither fan-out edge should exist; the metadata reference is ambiguous.
        // あいまいな metadata 参照はどちらの fan-out エッジも出してはならない。
        Assert.DoesNotContain(dependencies, d => d.SourcePath == "src/Svc.cs" && d.TargetPath == "src/A/MyAuditAttribute.cs");
        Assert.DoesNotContain(dependencies, d => d.SourcePath == "src/Svc.cs" && d.TargetPath == "src/B/MyAuditAttribute.cs");
    }

    [Fact]
    public void SearchReferences_MatchesCSharpAttributeSuffixConvention_Substring()
    {
        // issue #293 follow-up: `references MyAuditAttribute` (substring mode) must
        // find `[MyAudit]` call sites so `references` / `inspect` / `analyze_symbol`
        // stay consistent with `deps` / `impact` canonicalization.
        // issue #293 補足: `references MyAuditAttribute`（部分一致モード）が `[MyAudit]`
        // 参照サイトを見つけられなければならず、`references` / `inspect` /
        // `analyze_symbol` が `deps` / `impact` の正規化と整合する必要がある。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class MyAuditAttribute : Attribute
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        var results = _reader.SearchReferences("MyAuditAttribute", lang: "csharp");

        Assert.Contains(results, r => r.Path == "src/Svc.cs" && r.ReferenceKind == "attribute");
    }

    [Fact]
    public void SearchReferences_MatchesCSharpAttributeSuffixConvention_Exact()
    {
        // Same scenario under `--exact` — the suffix alias must be applied even when
        // exact-name matching is requested, otherwise `references MyAuditAttribute
        // --exact` loses the attribute call site.
        // `--exact` 指定下でも同様 — exact match の場合でも suffix alias を適用しない
        // と、`references MyAuditAttribute --exact` は attribute 参照サイトを取りこぼす。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class MyAuditAttribute : Attribute
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        var results = _reader.SearchReferences("MyAuditAttribute", lang: "csharp", exact: true);

        Assert.Contains(results, r => r.Path == "src/Svc.cs" && r.ReferenceKind == "attribute");
    }

    [Fact]
    public void SearchReferences_CSharpAttributeSuffixAliasDoesNotBleedToOtherLanguages()
    {
        // Alias must be C# only — a Java `@MyAudit(...)` annotation using the
        // suffix convention is not part of the Java ecosystem, so querying for
        // `MyAuditAttribute` under Java scope must not spuriously match `MyAudit`.
        // alias は C# 限定 — Java の `@MyAudit(...)` annotation は suffix 規約を使わない
        // ので、Java スコープで `MyAuditAttribute` を指定したときに `MyAudit` に
        // 誤って match してはならない。
        InsertIndexedFile("src/Svc.java", "java",
            """
            @MyAudit
            public class Svc {
            }
            """);

        var results = _reader.SearchReferences("MyAuditAttribute", lang: "java");

        Assert.Empty(results);
    }

    [Fact]
    public void SearchReferences_CSharpAttributeSuffixAlias_NotAppliedToCallKind()
    {
        // Adversarial review #7 follow-up: the suffix alias must NOT bleed into
        // `--kind call` queries. `references FooAttribute --kind call --lang csharp`
        // must not match a plain `Foo()` call — that would be a false positive.
        // adversarial review #7 補足: suffix alias を `--kind call` クエリに波及させない。
        // `references FooAttribute --kind call --lang csharp` が素の `Foo()` 呼び出しに
        // 一致してはならない（誤一致になる）。
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            public class Svc
            {
                public void Call()
                {
                    MyAudit();
                }
            }
            """);

        var results = _reader.SearchReferences("MyAuditAttribute", lang: "csharp", referenceKind: "call");

        Assert.DoesNotContain(results, r => r.SymbolName == "MyAudit");
    }

    [Fact]
    public void SearchReferences_CSharpAttributeSuffixAlias_UnscopedLangStillLimitsToCSharpAttributeRows()
    {
        // When `--lang` is omitted, the alias must still only match C# attribute rows.
        // A Java `@MyAudit(...)` or a bare `MyAudit()` call must not leak through.
        // `--lang` を省略したときも、alias は C# の attribute 行にしか一致してはならない。
        // Java の `@MyAudit(...)` や素の `MyAudit()` 呼び出しが漏れてはならない。
        InsertIndexedFile("src/Svc.java", "java",
            """
            @MyAudit
            public class Svc {
            }
            """);
        InsertIndexedFile("src/Caller.cs", "csharp",
            """
            public class Caller
            {
                public void Go()
                {
                    MyAudit();
                }
            }
            """);
        InsertIndexedFile("src/Target.cs", "csharp",
            """
            [MyAudit]
            public class Target
            {
            }
            """);

        var results = _reader.SearchReferences("MyAuditAttribute");

        // Should include the C# attribute site on Target.cs …
        Assert.Contains(results, r => r.Path == "src/Target.cs" && r.ReferenceKind == "attribute");
        // … but must NOT include the Java annotation nor the C# call row via alias.
        Assert.DoesNotContain(results, r => r.Path == "src/Svc.java");
        Assert.DoesNotContain(results, r => r.Path == "src/Caller.cs" && r.ReferenceKind == "call");
    }

    [Fact]
    public void SearchReferences_CSharpAttributeSuffixAlias_CaseInsensitiveQuery()
    {
        // The surrounding exact / substring paths are case-insensitive (folded or
        // NOCASE), so the suffix-stripping step must also be case-insensitive —
        // `references myauditattribute` / `MyAuditATTRIBUTE --exact` / etc. must
        // still produce the `MyAudit` alias and reach the `[MyAudit]` site.
        // 周辺の exact / substring 経路は case-insensitive（folded or NOCASE）なので、
        // suffix 除去も case-insensitive であるべき。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            using System;

            public sealed class MyAuditAttribute : Attribute
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        var lowercaseResults = _reader.SearchReferences("myauditattribute", lang: "csharp");
        Assert.Contains(lowercaseResults, r => r.Path == "src/Svc.cs" && r.ReferenceKind == "attribute");

        var mixedCaseExactResults = _reader.SearchReferences("MyAuditATTRIBUTE", lang: "csharp", exact: true);
        Assert.Contains(mixedCaseExactResults, r => r.Path == "src/Svc.cs" && r.ReferenceKind == "attribute");
    }

    [Fact]
    public void GetFileDependencies_CSharpAttributeAliasOnlyMatchesClassLikeTargets()
    {
        // issue #293 review: the C# attribute suffix alias UNION synthesizes a
        // `FooAttribute` lookup key for `[Foo]` references. Without a kind guard the
        // subsequent name-only join would spuriously attribute the consumer to any
        // file that merely defines a function / property / variable also named
        // `FooAttribute`. Only class-like target symbols should match synthetic alias
        // rows.
        // issue #293 レビュー指摘: `[Foo]` 用の alias UNION は `FooAttribute` という
        // lookup key を合成するが、kind によるガードが無いと、偶然 `FooAttribute`
        // という名前を持つ関数 / プロパティ / 変数を含むファイルにまで依存が張られて
        // しまう。合成 alias 行は class 系の target にのみ一致すべき。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class MyAuditAttribute : Attribute
            {
            }
            """);
        // Unrelated file containing a function named `MyAuditAttribute` — not an
        // attribute class, so `[MyAudit]` must not produce a dependency edge to it.
        // 無関係なファイルに関数として `MyAuditAttribute` が居るケース。
        // `[MyAudit]` はこのファイルへの依存を作ってはいけない。
        InsertIndexedFile("src/Util.cs", "csharp",
            """
            public static class Util
            {
                public static void MyAuditAttribute()
                {
                }
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        var dependencies = _reader.GetFileDependencies(limit: 20, lang: "csharp");

        Assert.Contains(dependencies, d => d.SourcePath == "src/Svc.cs" && d.TargetPath == "src/MyAuditAttribute.cs");
        Assert.DoesNotContain(dependencies, d => d.SourcePath == "src/Svc.cs" && d.TargetPath == "src/Util.cs");
    }

    [Fact]
    public void GetFileDependencyHints_SuppressesCSharpAttributeMetadataBypassOnAmbiguousTarget()
    {
        // issue #293 review: when two classes share the `MyAuditAttribute` name
        // *within the active impact scope*, a `[MyAudit]` reference row only
        // carries the short name and cannot be uniquely attributed to either
        // target. In that ambiguous case the `impact` metadata evidence bypass
        // must be skipped so rename / removal blast radius is not over-reported.
        // issue #293 レビュー指摘: impact スコープ内で同名の `MyAuditAttribute`
        // クラスが複数存在するとき、`[MyAudit]` 参照行は短縮名しか持たず、
        // どちらの target にも一意に紐付けられない。この曖昧なケースでは
        // `impact` の metadata evidence bypass を行わず、rename / 削除の影響
        //範囲を過大報告しないようにする。
        InsertIndexedFile("src/A/Inner1/MyAuditAttribute.cs", "csharp",
            """
            namespace A.Inner1;

            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);
        InsertIndexedFile("src/A/Inner2/MyAuditAttribute.cs", "csharp",
            """
            namespace A.Inner2;

            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);
        // Pure attribute consumer in src/A/ — no structured type evidence exists for
        // `MyAuditAttribute` other than the `[MyAudit]` use site itself.
        // src/A/ に純粋な attribute consumer — `MyAuditAttribute` に対する構造化された
        // 型証拠は `[MyAudit]` use site 以外には無い。
        InsertIndexedFile("src/A/Svc.cs", "csharp",
            """
            namespace A;

            [MyAudit]
            public class Svc
            {
            }
            """);

        // Both ambiguous definitions are within the `src/A/` scope; without the
        // ambiguity guard, the metadata bypass would fabricate a heuristic edge
        // even though the `[MyAudit]` target is qualifier-ambiguous.
        // src/A/ スコープ内に曖昧な定義が 2 件ある。ambiguity guard が無ければ、
        // `[MyAudit]` の target が qualifier 曖昧でも metadata bypass が heuristic
        // エッジを作ってしまう。
        var result = _reader.AnalyzeImpact(
            "MyAuditAttribute",
            maxDepth: 3,
            limit: 20,
            lang: "csharp",
            pathPatterns: new[] { "src/A/" });

        Assert.DoesNotContain(result.FileImpacts, f => f.SourcePath == "src/A/Svc.cs");
    }

    [Fact]
    public void GetFileDependencyHints_CSharpAttributeMetadataBypassAppliesWhenTargetUnambiguous()
    {
        // issue #293 review: the ambiguity guard must only fire when genuinely
        // ambiguous. With a single class-like `MyAuditAttribute` definition the
        // metadata bypass should still surface the `[MyAudit]` consumer as a
        // file-level hint, preserving the legitimate pure-attribute consumer case.
        // issue #293 レビュー指摘: ambiguity guard は本当に曖昧なときだけ発動すべき。
        // `MyAuditAttribute` の class 定義が 1 件しかない場合は従来通り metadata
        // bypass で `[MyAudit]` consumer を file-level hint として出し、純粋な
        // attribute consumer の正当な検出を保つ。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        var result = _reader.AnalyzeImpact("MyAuditAttribute", maxDepth: 3, limit: 20, lang: "csharp");

        Assert.Contains(result.FileImpacts, f => f.SourcePath == "src/Svc.cs" && f.TargetPath == "src/MyAuditAttribute.cs");
    }

    [Fact]
    public void GetFileDependencyHints_MetadataBypassAmbiguityGuard_CountsSameFileDuplicateDefinitions()
    {
        // issue #293 follow-up: the `impact` metadata bypass ambiguity guard must
        // count class-like definitions at symbol-identity level rather than at path
        // level. A single .cs file with two same-named `MyAuditAttribute` class
        // declarations under different namespaces is still ambiguous — metadata
        // reference rows only keep the short name `MyAudit` and cannot resolve
        // between `A.MyAuditAttribute` and `B.MyAuditAttribute`. Previously the
        // guard counted `SELECT DISTINCT f.path` so both definitions collapsed to
        // 1 and the bypass falsely fired, mis-attributing `[MyAudit]` consumers to
        // the impact of a specific target when the true resolution is unknown.
        // issue #293 補足: `impact` の metadata bypass 曖昧性ガードは、path 単位
        // ではなく symbol identity 単位で class-like 定義を数える必要がある。1 つの
        // .cs ファイル内に別名前空間で `MyAuditAttribute` が 2 つ定義されていても、
        // metadata 参照は短縮名 `MyAudit` しか持たず `A.MyAuditAttribute` と
        // `B.MyAuditAttribute` を区別できないため依然として曖昧。従来は
        // `SELECT DISTINCT f.path` で数えていたため両定義が 1 に潰れ、bypass が
        // 誤って発動し `[MyAudit]` consumer を特定 target の影響範囲へ誤帰属させていた。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            using System;

            namespace A
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class MyAuditAttribute : Attribute
                {
                }
            }

            namespace B
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class MyAuditAttribute : Attribute
                {
                }
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        var result = _reader.AnalyzeImpact("MyAuditAttribute", maxDepth: 3, limit: 20, lang: "csharp");

        // Two same-named class-like definitions in one file still make the target
        // ambiguous, so the `[MyAudit]` consumer must not surface as a file-level
        // impact hint — the metadata evidence bypass should fall through to the
        // normal structured-evidence check, which `[MyAudit]`-only consumers fail.
        // 同じファイル内の 2 つの同名 class-like 定義でも target は曖昧なので、
        // `[MyAudit]` consumer は file-level impact hint に現れてはいけない。
        // metadata evidence bypass は通常の structured-evidence 判定へフォール
        // スルーし、pure `[MyAudit]` consumer はそこで落ちる。
        Assert.DoesNotContain(result.FileImpacts, f => f.SourcePath == "src/Svc.cs" && f.TargetPath == "src/MyAuditAttribute.cs");
    }

    [Fact]
    public void GetFileDependencyHints_CSharpAttributeSuffixAlias_DoesNotLeakToSameFileSiblings()
    {
        // issue #293 round-12 follow-up: the C# `Attribute` suffix alias used by
        // ResolveImpactFallbackNames must only be applied to the resolved
        // definition's own name. If it were applied to every same-file fallback
        // name (e.g. a nested `BarAttribute` inside the file that defines
        // `FooAttribute`), `impact FooAttribute` would falsely claim `[Bar]` use
        // sites as its own blast radius.
        // issue #293 round-12 追加: ResolveImpactFallbackNames の C# `Attribute`
        // suffix 別名は、解決済み定義自身の名前にだけ適用すべき。same-file
        // fallback 名全体（例: `FooAttribute` と同一ファイルに nested で存在する
        // `BarAttribute`）にまで strip を適用すると、`impact FooAttribute` が
        // `[Bar]` 利用を自身の影響範囲として誤報告してしまう。
        InsertIndexedFile("src/FooAttribute.cs", "csharp",
            """
            public sealed class FooAttribute : System.Attribute
            {
                public sealed class BarAttribute : System.Attribute
                {
                }
            }
            """);
        // A separate file uses `[Bar]` — that must NOT show up in
        // `impact FooAttribute` because it references `BarAttribute`, not
        // `FooAttribute`.
        // 別ファイルで `[Bar]` を使う — これは `BarAttribute` の参照であり、
        // `FooAttribute` の `impact` には出てはならない。
        InsertIndexedFile("src/UseBar.cs", "csharp",
            """
            [Bar]
            public class UseBar
            {
            }
            """);

        var result = _reader.AnalyzeImpact("FooAttribute", maxDepth: 3, limit: 20, lang: "csharp");

        Assert.DoesNotContain(result.FileImpacts, f => f.SourcePath == "src/UseBar.cs");
    }

    [Fact]
    public void GetFileDependencyHints_MetadataBypassAmbiguityGuard_RespectsLangScope()
    {
        // issue #293 round-11 follow-up: the ambiguity guard must honor the active
        // `--lang` scope. A same-named class in an unrelated language must not
        // suppress the C# metadata bypass because attribute reference rows are
        // already language-qualified through the graph-supported `f.lang = 'csharp'`
        // join on the reference side.
        // issue #293 round-11 追加: ambiguity guard は active な `--lang` スコープを
        // 尊重すべき。別言語に同名クラスが存在しても C# の metadata bypass を
        // 潰してはならない — 参照側の join で既に言語修飾されているため、曖昧性は
        // 言語スコープ内でのみ判定する。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);
        // Unrelated Java class / annotation sharing the unqualified name — must not
        // affect the C#-only impact query.
        // 無関係な Java 側の同名クラス / アノテーション — C# 限定の impact クエリに
        // 影響してはならない。
        InsertIndexedFile("src/java/MyAuditAttribute.java", "java",
            """
            package pkg;

            public @interface MyAuditAttribute {
            }
            """);

        var result = _reader.AnalyzeImpact("MyAuditAttribute", maxDepth: 3, limit: 20, lang: "csharp");

        Assert.Contains(result.FileImpacts, f => f.SourcePath == "src/Svc.cs" && f.TargetPath == "src/MyAuditAttribute.cs");
    }

    [Fact]
    public void GetFileDependencyHints_MetadataBypassAmbiguityGuard_RespectsPathScope()
    {
        // issue #293 round-11 follow-up: ambiguity guard must honor `--path`
        // scoping. A same-named class outside the requested path subtree should
        // not suppress the bypass inside that subtree.
        // issue #293 round-11 追加: ambiguity guard は `--path` スコープを尊重すべき。
        // 要求した path サブツリー外にある同名クラスが、サブツリー内の bypass を
        // 潰してはならない。
        InsertIndexedFile("src/A/MyAuditAttribute.cs", "csharp",
            """
            namespace A;

            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);
        InsertIndexedFile("src/A/Svc.cs", "csharp",
            """
            namespace A;

            [MyAudit]
            public class Svc
            {
            }
            """);
        // Out-of-scope same-named definition in src/B/ — must not affect the
        // src/A/-scoped impact query.
        // スコープ外 src/B/ の同名定義 — src/A/ 限定の impact クエリに影響してはならない。
        InsertIndexedFile("src/B/MyAuditAttribute.cs", "csharp",
            """
            namespace B;

            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);

        var result = _reader.AnalyzeImpact(
            "MyAuditAttribute",
            maxDepth: 3,
            limit: 20,
            lang: "csharp",
            pathPatterns: new[] { "src/A/" });

        Assert.Contains(result.FileImpacts, f => f.SourcePath == "src/A/Svc.cs" && f.TargetPath == "src/A/MyAuditAttribute.cs");
    }

    [Fact]
    public void GetFileDependencyHints_MetadataBypassAmbiguityGuard_CliPathPatternEscaping_SuppressesWhenInScopeIsAmbiguous()
    {
        // issue #293 round-15 follow-up: path / exclude-path parameters must be
        // wrapped with `%...%` and routed through EscapeLikeQuery so the LIKE
        // predicate accepts CLI-style prefixes like `src/A/`. Without the wrap
        // the ambiguity count would underflow to 1 (unambiguous), and the
        // metadata bypass would falsely fire even though two MyAuditAttribute
        // classes exist side-by-side in the requested subtree.
        // issue #293 round-15 補足: path / exclude-path のバインドは他の reader
        // 経路と同じ `%...%` + EscapeLikeQuery に揃える必要がある。生値で渡すと
        // `src/A/` のような CLI 形では LIKE が一致せず、要求したサブツリーに
        // 同名 MyAuditAttribute が 2 件存在しても曖昧性カウントが 1 に落ち、
        // 本来抑止すべき metadata bypass が誤発火してしまう。
        InsertIndexedFile("src/A/Inner1/MyAuditAttribute.cs", "csharp",
            """
            namespace A.Inner1;

            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);
        InsertIndexedFile("src/A/Inner2/MyAuditAttribute.cs", "csharp",
            """
            namespace A.Inner2;

            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);
        InsertIndexedFile("src/A/Svc.cs", "csharp",
            """
            namespace A;

            [MyAudit]
            public class Svc
            {
            }
            """);

        var result = _reader.AnalyzeImpact(
            "MyAuditAttribute",
            maxDepth: 3,
            limit: 20,
            lang: "csharp",
            pathPatterns: new[] { "src/A/" });

        Assert.DoesNotContain(result.FileImpacts, f =>
            f.SourcePath == "src/A/Svc.cs" &&
            (f.TargetPath == "src/A/Inner1/MyAuditAttribute.cs" || f.TargetPath == "src/A/Inner2/MyAuditAttribute.cs"));
    }

    [Fact]
    public void GetFileDependencies_CSharp_PlainClassWithAttributeSuffixName_DoesNotCountAsAmbiguity()
    {
        // issue #293 round-16: same metadata-eligibility filter must apply to
        // the `deps` command. target_files.has_metadata_target_kind and the
        // target_ambiguity JOIN both require C# class-like targets to inherit
        // from an Attribute-suffixed base, so a plain `MyAuditAttribute`
        // cannot ambiguate the edge from `Svc.cs` to the real attribute class.
        // issue #293 round-16: 同じ適格性フィルタを deps にも適用する。
        // target_files.has_metadata_target_kind と target_ambiguity JOIN は
        // C# では Attribute suffix 継承を要求するため、plain `MyAuditAttribute` が
        // 存在しても実 attribute クラスへのエッジは残る。
        InsertIndexedFile("src/Real/MyAuditAttribute.cs", "csharp",
            """
            namespace Real;

            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);
        InsertIndexedFile("src/Unrelated/MyAuditAttribute.cs", "csharp",
            """
            namespace Unrelated;

            public sealed class MyAuditAttribute
            {
            }
            """);
        InsertIndexedFile("src/Real/Svc.cs", "csharp",
            """
            namespace Real;

            [MyAudit]
            public class Svc
            {
            }
            """);

        var deps = _reader.GetFileDependencies(
            limit: 50,
            lang: "csharp");

        Assert.Contains(deps, d =>
            d.SourcePath == "src/Real/Svc.cs" &&
            d.TargetPath == "src/Real/MyAuditAttribute.cs");
        Assert.DoesNotContain(deps, d =>
            d.SourcePath == "src/Real/Svc.cs" &&
            d.TargetPath == "src/Unrelated/MyAuditAttribute.cs");
    }

    [Fact]
    public void GetFileDependencies_CSharpNestedGenericNoArgAttribute_ResolvesToAttributeClass()
    {
        // issue #293 round-16: the no-arg C# attribute regex must handle
        // nested generic type arguments (e.g. `[MyAttr<Dictionary<string, int>>]`).
        // Previously the inner `<...>` segment excluded `>`, which broke on the
        // first inner `>` and classified the reference as a call.
        // issue #293 round-16: 引数なし C# 属性 regex が
        // `[MyAttr<Dictionary<string, int>>]` のようなネスト generic を
        // 扱えること。以前は内側の `<...>` セグメントが `>` を除外していて、
        // 最初の内側 `>` で崩れて call として誤分類されていた。
        InsertIndexedFile("src/MyAttrAttribute.cs", "csharp",
            """
            using System;
            using System.Collections.Generic;

            public sealed class MyAttrAttribute : Attribute
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            using System.Collections.Generic;

            [MyAttr<Dictionary<string, int>>]
            public class Svc
            {
            }
            """);

        var deps = _reader.GetFileDependencies(
            limit: 50,
            lang: "csharp");

        Assert.Contains(deps, d =>
            d.SourcePath == "src/Svc.cs" &&
            d.TargetPath == "src/MyAttrAttribute.cs");
    }

    [Fact]
    public void GetFileDependencies_CSharp_IndirectAttributeInheritance_ResolvesAsMetadataTarget()
    {
        // issue #293 round-17: the metadata-eligibility filter must not require
        // the immediate base class to end in `Attribute`. Indirect inheritance
        // like `class MyAuditAttribute : BaseAudit` where `BaseAudit : Attribute`
        // is a valid `[MyAudit]` target at compile time. The previous strict
        // pattern (`signature LIKE '%: %Attribute%'`) wrongly excluded the
        // indirectly-derived class and dropped the deps edge. The loosened
        // pattern (`signature LIKE '%: %'`) accepts any class with an
        // inheritance clause, which is the best portable approximation since
        // SQL cannot resolve base types transitively.
        // issue #293 round-17: metadata 適格性フィルタは直接基底が
        // `Attribute` で終わることを要求してはならない。
        // `class MyAuditAttribute : BaseAudit` で `BaseAudit : Attribute` の
        // ような間接継承も `[MyAudit]` の有効な target である。以前の
        // 厳格パターンは間接継承を弾いて deps エッジを落としていた。
        // 緩和パターンは「継承節を持つ class」を近似として採用する。
        InsertIndexedFile("src/BaseAudit.cs", "csharp",
            """
            namespace App;

            public abstract class BaseAudit : System.Attribute
            {
            }
            """);
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            namespace App;

            public sealed class MyAuditAttribute : BaseAudit
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            namespace App;

            [MyAudit]
            public class Svc
            {
            }
            """);

        var deps = _reader.GetFileDependencies(
            limit: 50,
            lang: "csharp");

        Assert.Contains(deps, d =>
            d.SourcePath == "src/Svc.cs" &&
            d.TargetPath == "src/MyAuditAttribute.cs");
    }

    [Fact]
    public void GetFileDependencies_JavaScriptFunctionDecorator_ResolvesAsDependency()
    {
        // issue #293 round-18: JavaScript/TypeScript decorators legitimately target
        // factory `function`s (e.g. `function sealed(target) { ... }`), not only
        // class-like definitions. The metadata-target predicate must accept
        // `function` for JS/TS or decorator edges to a function target are dropped
        // from `deps`.
        // issue #293 round-18: JS/TS decorator は `function sealed(target){...}` のような
        // factory 関数も正当な target となる。JS/TS では `function` を metadata target の
        // 対象 kind として許可しないと、function を対象とする decorator edge が deps から欠落する。
        InsertIndexedFile("src/decorators.js", "javascript",
            """
            export function sealed(target) {
                Object.freeze(target);
            }
            """);
        InsertIndexedFile("src/model.js", "javascript",
            """
            import { sealed } from './decorators.js';

            @sealed
            class Foo {
            }
            """);

        var deps = _reader.GetFileDependencies(
            limit: 50,
            lang: "javascript");

        Assert.Contains(deps, d =>
            d.SourcePath == "src/model.js" &&
            d.TargetPath == "src/decorators.js");
    }

    [Fact]
    public void GetFileDependencies_CSharp_LegacyDbWithNullSignature_StillResolvesAttributeEdge()
    {
        // issue #293 round-19: the metadata-target signature clause must degrade
        // gracefully when the `symbols.signature` column exists but individual
        // rows carry NULL values — the common shape of a DB whose schema was
        // migrated in place (`TryMigrateForRead`) without reindexing. Requiring
        // `signature LIKE '%: %'` would silently drop the real
        // `[MyAudit]` → `class MyAuditAttribute : System.Attribute` edge there,
        // so the clause must treat NULL signature as eligible (equivalent to the
        // column-missing `1 = 1` fallback).
        // issue #293 round-19: metadata-target の signature 句は、列は存在するが
        // row の値が NULL の legacy-migration DB でも degrade する必要がある。
        // LIKE を強要すると本物の `[MyAudit]` edge が silent に落ちる。
        // 列欠落時の `1 = 1` fallback と同じく NULL も eligible にする。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        // Simulate the partial-migration shape: signature column is present but the
        // C# class row has a NULL signature, as if the schema were upgraded in place
        // without re-running extraction.
        // partial-migration の形を再現: signature 列はあるが C# class 行の signature が
        // NULL の状態 — その場 schema 移行後に再抽出していない DB と同じ。
        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE symbols SET signature = NULL WHERE name = 'MyAuditAttribute' AND kind = 'class'";
            cmd.ExecuteNonQuery();
        }

        var deps = _reader.GetFileDependencies(
            limit: 50,
            lang: "csharp");

        Assert.Contains(deps, d =>
            d.SourcePath == "src/Svc.cs" &&
            d.TargetPath == "src/MyAuditAttribute.cs");
    }

    [Fact]
    public void GetFileDependencies_CSharp_SameNameInterface_DoesNotBlockMetadataEdge()
    {
        // issue #293 round-18: ambiguity should only count truly attribute-eligible
        // duplicates. In C#, only `class` can inherit from `System.Attribute` —
        // a same-named `interface` or `struct` cannot be an attribute target, so it
        // must not suppress the metadata deps edge to the legitimate attribute class.
        // issue #293 round-18: ambiguity 判定は attribute 適格な重複だけを数えるべき。
        // C# では `class` のみが `System.Attribute` を継承できるため、同名の
        // `interface` や `struct` が存在しても metadata deps edge を抑止してはならない。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);
        InsertIndexedFile("src/IMyAudit.cs", "csharp",
            """
            public interface MyAuditAttribute
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);

        var deps = _reader.GetFileDependencies(
            limit: 50,
            lang: "csharp");

        Assert.Contains(deps, d =>
            d.SourcePath == "src/Svc.cs" &&
            d.TargetPath == "src/MyAuditAttribute.cs");
    }

    [Fact]
    public void GetFileDependencyHints_MetadataBypassAmbiguityGuard_RespectsExcludeTests()
    {
        // issue #293 round-11 follow-up: ambiguity guard must honor
        // `--exclude-tests`. A same-named class only present in tests should not
        // suppress the bypass when the caller has already excluded tests from the
        // impact scope.
        // issue #293 round-11 追加: ambiguity guard は `--exclude-tests` を尊重すべき。
        // test 配下にしか存在しない同名定義が、test を除外した impact クエリの
        // bypass を潰してはならない。
        InsertIndexedFile("src/MyAuditAttribute.cs", "csharp",
            """
            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);
        InsertIndexedFile("src/Svc.cs", "csharp",
            """
            [MyAudit]
            public class Svc
            {
            }
            """);
        // Test-only same-named definition — must be filtered out when the caller
        // passes excludeTests=true so the bypass stays active in the source scope.
        // test 配下にしかない同名定義 — excludeTests=true のときはスコープ外になり、
        // source 側の bypass を維持すべき。
        InsertIndexedFile("tests/CodeIndex.Tests/MyAuditAttributeTests.cs", "csharp",
            """
            public sealed class MyAuditAttribute : System.Attribute
            {
            }
            """);

        var result = _reader.AnalyzeImpact(
            "MyAuditAttribute",
            maxDepth: 3,
            limit: 20,
            lang: "csharp",
            excludeTests: true);

        Assert.Contains(result.FileImpacts, f => f.SourcePath == "src/Svc.cs" && f.TargetPath == "src/MyAuditAttribute.cs");
    }

    [Fact]
    public void GetGroupedSymbolHotspots_CollapsesDuplicateNamesWithoutBareJoinInflation()
    {
        InsertIndexedFile("src/Alpha.cs", "csharp",
            """
            public class Alpha
            {
                private void SharedHelper() { }

                public void Use()
                {
                    SharedHelper();
                    SharedHelper();
                }
            }
            """);
        InsertIndexedFile("src/Beta.cs", "csharp",
            """
            public class Beta
            {
                private void SharedHelper() { }

                public void Use()
                {
                    SharedHelper();
                }
            }
            """);

        var grouped = _reader.GetGroupedSymbolHotspots(
            limit: 10,
            kind: "function",
            lang: "csharp",
            pathPatterns: ["src/"],
            excludePathPatterns: null,
            excludeTests: false);

        var shared = Assert.Single(grouped.Where(result => result.Symbol.Name == "SharedHelper"));
        Assert.Equal(3, shared.ReferenceCount);
        Assert.Equal(2, shared.DefinitionSites);
        Assert.Equal(2, shared.Paths.Count);
        Assert.Contains("src/Alpha.cs", shared.Paths);
        Assert.Contains("src/Beta.cs", shared.Paths);
    }

    [Fact]
    public void GraphQueries_ConstructorReferencesAreInstantiateOnly()
    {
        InsertIndexedFile("src/constructor_kind_target.cs", "csharp",
            """
            namespace N
            {
                public class Target
                {
                    public Target() { }
                }
            }
            """);
        InsertIndexedFile("src/constructor_kind_caller.cs", "csharp",
            """
            public class Caller
            {
                public void Run()
                {
                    var target = new N.Target();
                    var other = new global::N.Target();
                }
            }
            """);

        var instantiateRefs = _reader.SearchReferences("Target", lang: "csharp", referenceKind: "instantiate", exact: true, pathPatterns: ["constructor_kind"]).ToList();
        Assert.Equal(2, instantiateRefs.Count);
        Assert.All(instantiateRefs, reference => Assert.Equal("instantiate", reference.ReferenceKind));

        Assert.Empty(_reader.SearchReferences("Target", lang: "csharp", referenceKind: "call", exact: true, pathPatterns: ["constructor_kind"]));
        Assert.Equal(0, _reader.CountSearchReferences("Target", lang: "csharp", referenceKind: "call", exact: true, pathPatterns: ["constructor_kind"]));
        Assert.Equal(2, _reader.CountSearchReferences("Target", lang: "csharp", referenceKind: "instantiate", exact: true, pathPatterns: ["constructor_kind"]));

        Assert.Empty(_reader.GetCallees("Run", lang: "csharp", referenceKind: "call", exact: true, pathPatterns: ["constructor_kind"]));

        var instantiateCallee = Assert.Single(_reader.GetCallees("Run", lang: "csharp", referenceKind: "instantiate", exact: true, pathPatterns: ["constructor_kind"]));
        Assert.Equal("instantiate", instantiateCallee.ReferenceKind);
        Assert.Equal(2, instantiateCallee.ReferenceCount);

        Assert.Empty(_reader.GetCallers("Target", lang: "csharp", referenceKind: "call", exact: true, pathPatterns: ["constructor_kind"]));
        var instantiateCaller = Assert.Single(_reader.GetCallers("Target", lang: "csharp", referenceKind: "instantiate", exact: true, pathPatterns: ["constructor_kind"]));
        Assert.Equal(2, instantiateCaller.ReferenceCount);
    }

    [Fact]
    public void GraphQueries_DefaultGraphQueriesKeepSubscribeRowsVisible()
    {
        InsertIndexedFile("src/event_publisher.cs", "csharp",
            """
            using System;

            public class Publisher
            {
                public event EventHandler? Changed;
            }
            """);
        InsertIndexedFile("src/event_subscriber.cs", "csharp",
            """
            using System;

            public class Subscriber
            {
                public void Hook(Publisher publisher)
                {
                    publisher.Changed += OnChanged;
                }

                private void OnChanged(object? sender, EventArgs e) { }
            }
            """);

        var reference = Assert.Single(_reader.SearchReferences("Changed", lang: "csharp", exact: true, pathPatterns: ["event_"]));
        Assert.Equal("subscribe", reference.ReferenceKind);
        Assert.Equal("Hook", reference.ContainerName);
        Assert.Equal(1, _reader.CountSearchReferences("Changed", lang: "csharp", exact: true, pathPatterns: ["event_"]));

        var caller = Assert.Single(_reader.GetCallers("Changed", lang: "csharp", exact: true, pathPatterns: ["event_"]));
        Assert.Equal("Hook", caller.CallerName);
        Assert.Equal("Changed", caller.CalleeName);
        Assert.Equal(1, caller.ReferenceCount);
        Assert.Equal(1, _reader.CountCallers("Changed", lang: "csharp", exact: true, pathPatterns: ["event_"]));

        var callee = Assert.Single(_reader.GetCallees("Hook", lang: "csharp", exact: true, pathPatterns: ["event_"]));
        Assert.Equal("Hook", callee.CallerName);
        Assert.Equal("Changed", callee.CalleeName);
        Assert.Equal("subscribe", callee.ReferenceKind);
        Assert.Equal(1, callee.ReferenceCount);
        Assert.Equal(1, _reader.CountCallees("Hook", lang: "csharp", exact: true, pathPatterns: ["event_"]));

        var analysis = _reader.AnalyzeSymbol("Changed", limit: 5, lang: "csharp", pathPatterns: ["event_"], exact: true);
        var bundledReference = Assert.Single(analysis.References);
        Assert.Equal("subscribe", bundledReference.ReferenceKind);
        Assert.Equal("Hook", bundledReference.ContainerName);
        var bundledCaller = Assert.Single(analysis.Callers);
        Assert.Equal("Hook", bundledCaller.CallerName);
        Assert.Empty(analysis.Callees);

        var callerAnalysis = _reader.AnalyzeSymbol("Hook", limit: 5, lang: "csharp", pathPatterns: ["event_"], exact: true);
        var bundledCallee = Assert.Single(callerAnalysis.Callees);
        Assert.Equal("Hook", bundledCallee.CallerName);
        Assert.Equal("Changed", bundledCallee.CalleeName);
        Assert.Equal("subscribe", bundledCallee.ReferenceKind);
    }

    [Fact]
    public void GetTransitiveCallers_FollowsSubscribeEdges()
    {
        // Regression: impact BFS must share the call-graph contract with callers/callees,
        // so event subscriptions (`Changed += OnChanged`) also participate in the transitive
        // caller chain rather than being stripped like metadata edges.
        // リグレッション: impact BFS も callers/callees と同じ call-graph 契約を共有し、
        // イベント購読 (`Changed += OnChanged`) が transitive caller chain に含まれること。
        InsertIndexedFile("src/impact_subscribe_publisher.cs", "csharp",
            """
            using System;

            public class SubPublisher
            {
                public event EventHandler? Changed;
            }
            """);
        InsertIndexedFile("src/impact_subscribe_subscriber.cs", "csharp",
            """
            using System;

            public class SubSubscriber
            {
                public void Hook(SubPublisher publisher)
                {
                    publisher.Changed += OnChanged;
                }

                private void OnChanged(object? sender, EventArgs e) { }
            }
            """);

        var (impact, truncated) = _reader.GetTransitiveCallers(
            "Changed", maxDepth: 2, limit: 10, lang: "csharp", pathPatterns: ["impact_subscribe_"]);

        Assert.False(truncated);
        var caller = Assert.Single(impact);
        Assert.Equal("Hook", caller.CallerName);
    }

    [Fact]
    public void GetTransitiveCallers_ReturnsAllDirectCallersAcrossPages()
    {
        const int callerCount = 205;
        for (int i = 0; i < callerCount; i++)
        {
            var callerFileId = _writer.UpsertFile(new FileRecord
            {
                Path = $"src/caller_{i:D3}.py",
                Lang = "python",
                Size = 96,
                Lines = 2,
                Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            _writer.InsertChunks([new ChunkRecord
            {
                FileId = callerFileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 2,
                Content = $"def caller_{i:D3}():\n    return authenticate('user', 'pw')\n",
            }]);
            _writer.InsertReferences([
                new ReferenceRecord
                {
                    FileId = callerFileId,
                    SymbolName = "authenticate",
                    ReferenceKind = "call",
                    Line = 2,
                    Column = 12,
                    Context = "return authenticate('user', 'pw')",
                    ContainerKind = "function",
                    ContainerName = $"caller_{i:D3}",
                },
            ]);
        }

        var (results, truncated) = _reader.GetTransitiveCallers("authenticate", maxDepth: 1, limit: 300);

        Assert.False(truncated);
        Assert.Equal(callerCount, results.Count);
        Assert.Equal(callerCount, results.Select(result => $"{result.Path}:{result.CallerName}").Distinct(StringComparer.Ordinal).Count());
        Assert.All(results, result => Assert.Equal(1, result.Depth));
    }

    [Fact]
    public void AnalyzeImpact_ClassSymbolReturnsHeuristicFileDependencyHints()
    {
        InsertIndexedFile("src/FolderDiffService.cs", "csharp",
            """
            public class FolderDiffService
            {
                public void ExecuteFolderDiffAsync() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Run(FolderDiffService service)
                {
                    service.ExecuteFolderDiffAsync();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FolderDiffService", maxDepth: 3, limit: 10);

        Assert.Equal("file_dependency_hints", analysis.ImpactMode);
        Assert.True(analysis.Heuristic);
        Assert.Empty(analysis.Callers);
        Assert.True(analysis.HasClassLikeDefinitions);
        Assert.False(analysis.HasMultipleDefinitions);
        Assert.False(analysis.HasMultipleDefinitionFiles);
        Assert.Equal(1, analysis.HintCount);
        var edge = Assert.Single(analysis.FileImpacts);
        Assert.Equal("src/App.cs", edge.SourcePath);
        Assert.Equal("src/FolderDiffService.cs", edge.TargetPath);
        Assert.Contains("ExecuteFolderDiffAsync", edge.Symbols);
    }

    [Fact]
    public void AnalyzeImpact_ClassAndNamespaceWithSameName_StillReturnsHeuristicHints()
    {
        InsertIndexedFile("src/FooService.cs", "csharp",
            """
            namespace FooService;

            public class FooService
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Boot(FooService service)
                {
                    service.Run();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FooService", maxDepth: 3, limit: 10);

        Assert.Equal("file_dependency_hints", analysis.ImpactMode);
        Assert.True(analysis.Heuristic);
        Assert.True(analysis.HasMultipleDefinitions);
        Assert.False(analysis.HasMultipleDefinitionFiles);
        Assert.Equal(2, analysis.DefinitionCount);
        Assert.Equal(1, analysis.HintCount);
        Assert.Equal("src/App.cs", Assert.Single(analysis.FileImpacts).SourcePath);
    }

    [Fact]
    public void AnalyzeImpact_FoldEquivalentClassDefinitions_ReportAmbiguity()
    {
        InsertIndexedFile("src/FooService.cs", "csharp",
            """
            public class FooService
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/FullwidthFooService.cs", "csharp",
            """
            public class ＦｏｏＳｅｒｖｉｃｅ
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Boot(FooService service)
                {
                    service.Run();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FooService", maxDepth: 3, limit: 10);

        Assert.Equal("none", analysis.ImpactMode);
        Assert.False(analysis.Heuristic);
        Assert.Empty(analysis.FileImpacts);
        Assert.Equal(2, analysis.DefinitionCount);
        Assert.True(analysis.HasMultipleDefinitions);
        Assert.Equal("multiple_definition_files", analysis.ZeroResultReason);
    }

    [Fact]
    public void AnalyzeImpact_PartialClassWithoutReverseEdges_ExplainsMultipleDefinitions()
    {
        InsertIndexedFile("src/Worker.Part1.cs", "csharp",
            """
            public partial class Worker
            {
                public void Start() { }
            }
            """);
        InsertIndexedFile("src/Worker.Part2.cs", "csharp",
            """
            public partial class Worker
            {
                public void Stop() { }
            }
            """);

        var analysis = _reader.AnalyzeImpact("Worker", maxDepth: 3, limit: 10);

        Assert.Equal("none", analysis.ImpactMode);
        Assert.Empty(analysis.Callers);
        Assert.Empty(analysis.FileImpacts);
        Assert.True(analysis.HasClassLikeDefinitions);
        Assert.True(analysis.HasMultipleDefinitions);
        Assert.True(analysis.HasMultipleDefinitionFiles);
        Assert.Equal("multiple_definition_files", analysis.ZeroResultReason);
        Assert.Contains("deps --path <definition-path> --reverse", analysis.Suggestion);
    }

    [Fact]
    public void AnalyzeImpact_DuplicateDefinitionsInOneFile_ExplainsAmbiguity()
    {
        InsertIndexedFile("src/Services.cs", "csharp",
            """
            namespace A
            {
                public class FooService
                {
                    public void Run() { }
                }
            }

            namespace B
            {
                public class FooService
                {
                    public void Run() { }
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FooService", maxDepth: 3, limit: 10);

        Assert.Equal("none", analysis.ImpactMode);
        Assert.Empty(analysis.Callers);
        Assert.Empty(analysis.FileImpacts);
        Assert.Equal(2, analysis.DefinitionCount);
        Assert.Equal(1, analysis.DefinitionFileCount);
        Assert.True(analysis.HasMultipleDefinitions);
        Assert.False(analysis.HasMultipleDefinitionFiles);
        Assert.Equal("multiple_definitions", analysis.ZeroResultReason);
        Assert.Contains("fully qualified or member symbol query", analysis.Suggestion);
    }

    [Fact]
    public void AnalyzeImpact_ClassCollisionWithoutTypeEvidenceReturnsNoHints()
    {
        InsertIndexedFile("src/FooService.cs", "csharp",
            """
            public class FooService
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/BarService.cs", "csharp",
            """
            public class BarService
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Boot(BarService service)
                {
                    service.Run();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FooService", maxDepth: 3, limit: 10);

        Assert.Equal("none", analysis.ImpactMode);
        Assert.False(analysis.Heuristic);
        Assert.Empty(analysis.Callers);
        Assert.Empty(analysis.FileImpacts);
        Assert.Equal(0, analysis.HintCount);
        Assert.Equal("class_symbol_no_symbol_callers", analysis.ZeroResultReason);
    }

    [Fact]
    public void AnalyzeImpact_NamespaceDoesNotFallbackToFileDependencies()
    {
        InsertIndexedFile("src/Services.cs", "csharp",
            """
            namespace Acme;

            public class FooService
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            namespace Acme;

            public class App
            {
                public void Boot(FooService service)
                {
                    service.Run();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("Acme", maxDepth: 3, limit: 10);

        Assert.Equal("none", analysis.ImpactMode);
        Assert.Empty(analysis.Callers);
        Assert.Empty(analysis.FileImpacts);
        Assert.Equal("non_callable_symbol_kind", analysis.ZeroResultReason);
    }

    [Fact]
    public void AnalyzeImpact_ImportOnlyQueryReportsNonCallableSymbolKind()
    {
        InsertIndexedFile("src/app.py", "python",
            """
            import requests
            """);

        var analysis = _reader.AnalyzeImpact("requests", maxDepth: 3, limit: 10);

        Assert.Equal("none", analysis.ImpactMode);
        Assert.Empty(analysis.Callers);
        Assert.Empty(analysis.FileImpacts);
        Assert.Equal(1, analysis.DefinitionCount);
        Assert.Equal("import", Assert.Single(analysis.Definitions).Kind);
        Assert.Equal("non_callable_symbol_kind", analysis.ZeroResultReason);
        Assert.Contains("definition <symbol>", analysis.Suggestion);
    }

    [Fact]
    public void AnalyzeImpact_UnresolvedExternalCallOnlyWithoutTypeEvidenceReturnsNoHints()
    {
        InsertIndexedFile("src/FolderDiffService.cs", "csharp",
            """
            public class FolderDiffService
            {
                public void ExecuteFolderDiffAsync() { }
            }
            """);
        InsertIndexedFile("src/ExternalConsumer.cs", "csharp",
            """
            public class ExternalConsumer
            {
                public void Boot()
                {
                    ExecuteFolderDiffAsync();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FolderDiffService", maxDepth: 3, limit: 10);

        Assert.Equal("none", analysis.ImpactMode);
        Assert.False(analysis.Heuristic);
        Assert.Empty(analysis.Callers);
        Assert.Equal(0, analysis.HintCount);
        Assert.Empty(analysis.FileImpacts);
        Assert.Equal("class_symbol_no_symbol_callers", analysis.ZeroResultReason);
    }

    [Fact]
    public void AnalyzeImpact_UnicodeTypeEvidenceStillEnablesHeuristicHints()
    {
        InsertIndexedFile("src/ＦｏｏＳｅｒｖｉｃｅ.cs", "csharp",
            """
            public class ＦｏｏＳｅｒｖｉｃｅ
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Boot(ＦｏｏＳｅｒｖｉｃｅ service)
                {
                    service.Run();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("ＦｏｏＳｅｒｖｉｃｅ", maxDepth: 3, limit: 10);

        Assert.Equal("file_dependency_hints", analysis.ImpactMode);
        Assert.True(analysis.Heuristic);
        Assert.Equal(1, analysis.HintCount);
        Assert.Equal("src/App.cs", Assert.Single(analysis.FileImpacts).SourcePath);
    }

    [Fact]
    public void AnalyzeImpact_CommentOnlyTypeMentionDoesNotCountAsTypeEvidence()
    {
        InsertIndexedFile("src/FooService.cs", "csharp",
            """
            public class FooService
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/OtherService.cs", "csharp",
            """
            public class OtherService
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Boot(OtherService service)
                {
                    service.Run(); // TODO: maybe replace with FooService later
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FooService", maxDepth: 3, limit: 10);

        Assert.Equal("none", analysis.ImpactMode);
        Assert.False(analysis.Heuristic);
        Assert.Empty(analysis.FileImpacts);
        Assert.Equal(0, analysis.HintCount);
        Assert.Equal("class_symbol_no_symbol_callers", analysis.ZeroResultReason);
    }

    [Fact]
    public void AnalyzeImpact_StringLiteralTypeMentionDoesNotCountAsTypeEvidence()
    {
        InsertIndexedFile("src/FooService.cs", "csharp",
            """
            public class FooService
            {
                public void Execute() { }
            }
            """);
        InsertIndexedFile("src/Worker.cs", "csharp",
            """
            public class Worker
            {
                public void Execute() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Boot(Worker worker)
                {
                    var label = "FooService";
                    worker.Execute();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FooService", maxDepth: 3, limit: 10);

        Assert.Equal("none", analysis.ImpactMode);
        Assert.False(analysis.Heuristic);
        Assert.Empty(analysis.FileImpacts);
        Assert.Equal(0, analysis.HintCount);
        Assert.Equal("class_symbol_no_symbol_callers", analysis.ZeroResultReason);
    }

    [Fact]
    public void AnalyzeImpact_ExcludeTestsIgnoresOutOfScopeDuplicateDefinitions()
    {
        InsertIndexedFile("src/FooService.cs", "csharp",
            """
            public class FooService
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("tests/FooServiceTests.cs", "csharp",
            """
            public class FooService
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Boot(FooService service)
                {
                    service.Run();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FooService", maxDepth: 3, limit: 10, excludeTests: true);

        Assert.Equal("file_dependency_hints", analysis.ImpactMode);
        Assert.True(analysis.Heuristic);
        Assert.False(analysis.HasMultipleDefinitionFiles);
        Assert.Equal(1, analysis.DefinitionFileCount);
        Assert.Equal(1, analysis.HintCount);
        Assert.Equal("src/FooService.cs", Assert.Single(analysis.Definitions).Path);
        Assert.Equal("src/App.cs", Assert.Single(analysis.FileImpacts).SourcePath);
    }

    [Fact]
    public void AnalyzeImpact_IgnoresUnsupportedLanguageDuplicates()
    {
        InsertIndexedFile("src/FooService.cs", "csharp",
            """
            public class FooService
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/tools.sh", "shell",
            """
            FooService() {
              :
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Boot(FooService service)
                {
                    service.Run();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FooService", maxDepth: 3, limit: 10);

        Assert.Equal("file_dependency_hints", analysis.ImpactMode);
        Assert.True(analysis.Heuristic);
        Assert.False(analysis.HasMultipleDefinitions);
        Assert.False(analysis.HasMultipleDefinitionFiles);
        Assert.Equal(1, analysis.DefinitionFileCount);
        Assert.Equal("src/FooService.cs", Assert.Single(analysis.Definitions).Path);
        Assert.Equal("src/App.cs", Assert.Single(analysis.FileImpacts).SourcePath);
    }

    [Fact]
    public void AnalyzeImpact_ExactDefinitionResolutionSkipsUnsupportedMatchesBeforeLimit()
    {
        for (int i = 0; i < 60; i++)
        {
            InsertIndexedFile($"scripts/Foo{i:D2}.sh", "shell",
                """
                Foo() {
                  :
                }
                """);
        }

        InsertIndexedFile("src/Foo.cs", "csharp",
            """
            public class Foo
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Boot(Foo service)
                {
                    service.Run();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("Foo", maxDepth: 3, limit: 10);

        Assert.Equal("file_dependency_hints", analysis.ImpactMode);
        Assert.True(analysis.Heuristic);
        Assert.Equal(1, analysis.DefinitionCount);
        Assert.Equal("src/Foo.cs", Assert.Single(analysis.Definitions).Path);
        Assert.Equal("src/App.cs", Assert.Single(analysis.FileImpacts).SourcePath);
    }

    [Fact]
    public void AnalyzeImpact_SubstringTypeEvidenceDoesNotCountAsStructuredEvidence()
    {
        InsertIndexedFile("src/Foo.cs", "csharp",
            """
            public class Foo
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/FooService.cs", "csharp",
            """
            public class FooService
            {
                public void Run() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Handle(FooService service)
                {
                    service.Run();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("Foo", maxDepth: 3, limit: 10);

        Assert.Equal("none", analysis.ImpactMode);
        Assert.False(analysis.Heuristic);
        Assert.Empty(analysis.FileImpacts);
        Assert.Equal(0, analysis.HintCount);
        Assert.Equal("class_symbol_no_symbol_callers", analysis.ZeroResultReason);
    }

    [Fact]
    public void AnalyzeImpact_HeuristicHintsSetTruncatedWhenLimitReached()
    {
        InsertIndexedFile("src/FolderDiffService.cs", "csharp",
            """
            public class FolderDiffService
            {
                public void ExecuteFolderDiffAsync() { }
            }
            """);
        InsertIndexedFile("src/App1.cs", "csharp",
            """
            public class App1
            {
                public void Boot(FolderDiffService service)
                {
                    service.ExecuteFolderDiffAsync();
                }
            }
            """);
        InsertIndexedFile("src/App2.cs", "csharp",
            """
            public class App2
            {
                public void Boot(FolderDiffService service)
                {
                    service.ExecuteFolderDiffAsync();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FolderDiffService", maxDepth: 3, limit: 1);

        Assert.Equal("file_dependency_hints", analysis.ImpactMode);
        Assert.True(analysis.Heuristic);
        Assert.True(analysis.Truncated);
        Assert.Single(analysis.FileImpacts);
        Assert.Equal(1, analysis.HintCount);
    }

    [Fact]
    public void AnalyzeImpact_HeuristicHintsKeepActualReferenceCount()
    {
        InsertIndexedFile("src/FolderDiffService.cs", "csharp",
            """
            public class FolderDiffService
            {
                public void ExecuteFolderDiffAsync() { }
            }
            """);
        InsertIndexedFile("src/App.cs", "csharp",
            """
            public class App
            {
                public void Boot(FolderDiffService service)
                {
                    service.ExecuteFolderDiffAsync();
                    service.ExecuteFolderDiffAsync();
                    service.ExecuteFolderDiffAsync();
                }
            }
            """);

        var analysis = _reader.AnalyzeImpact("FolderDiffService", maxDepth: 3, limit: 10);

        Assert.Equal("file_dependency_hints", analysis.ImpactMode);
        var edge = Assert.Single(analysis.FileImpacts);
        Assert.Equal(3, edge.ReferenceCount);
        Assert.Equal("ExecuteFolderDiffAsync", edge.Symbols);
    }

    [Fact]
    public void ListFiles_ReturnsAllFiles()
    {
        var results = _reader.ListFiles();
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void ListFiles_FiltersByLanguage()
    {
        var results = _reader.ListFiles(lang: "python");
        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
    }

    [Fact]
    public void ListFiles_FiltersByNamePattern()
    {
        var results = _reader.ListFiles(query: "api");
        Assert.Single(results);
        Assert.Equal("src/api.js", results[0].Path);
    }

    [Fact]
    public void ListFiles_MultiplePathPatterns_AreOred()
    {
        // Two --path values should match any file whose path matches either pattern.
        // 2つの --path 値は、どちらかのパターンにマッチするファイルを返す。
        var results = _reader.ListFiles(pathPatterns: new[] { "auth", "docs/" });

        Assert.Equal(2, results.Count);
        var paths = results.Select(r => r.Path).ToHashSet();
        Assert.Contains("src/auth.py", paths);
        Assert.Contains("docs/notes.md", paths);
    }

    [Fact]
    public void ListFiles_PathFiltersAndExcludePaths_WorkTogether()
    {
        var results = _reader.ListFiles(pathPatterns: new[] { "src/" }, excludePathPatterns: ["api"]);

        Assert.Single(results);
        Assert.Equal("src/auth.py", results[0].Path);
    }

    [Fact]
    public void ListFiles_IncludesSymbolCount()
    {
        var results = _reader.ListFiles(query: "api");
        Assert.Equal(2, results[0].SymbolCount); // ApiClient + fetchData
    }

    [Fact]
    public void ListFiles_ReturnsFreshnessMetadata()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/fresh.cs",
            Lang = "csharp",
            Size = 120,
            Lines = 6,
            Checksum = "fresh-checksum",
            Modified = new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = fileId,
            ChunkIndex = 0,
            StartLine = 1,
            EndLine = 6,
            Content = "public class Fresh { public void Run() { } }",
        }]);

        var file = Assert.Single(_reader.ListFiles(query: "fresh.cs"));
        Assert.Equal("fresh-checksum", file.Checksum);
        Assert.Equal(new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc), file.Modified);
        Assert.NotNull(file.IndexedAt);
    }

    [Fact]
    public void GetStatus_ReturnsCorrectCounts()
    {
        var status = _reader.GetStatus();
        Assert.Equal(3, status.Files);
        Assert.Equal(3, status.Chunks);
        Assert.Equal(3, status.Symbols);
        Assert.Equal(1, status.References);
        Assert.NotNull(status.IndexedAt);
    }

    [Fact]
    public void GetStatus_IncludesLanguageBreakdown()
    {
        var status = _reader.GetStatus();
        Assert.Equal(3, status.Languages.Count);
        Assert.Equal(1, status.Languages["python"]);
        Assert.Equal(1, status.Languages["javascript"]);
        Assert.Equal(1, status.Languages["markdown"]);
    }

    [Fact]
    public void GetRepoMap_ReturnsOverviewSectionsAndEntrypoints()
    {
        InsertIndexedFile("src/Program.cs", "csharp", "public class Program\n{\n    public static void Main(string[] args)\n    {\n        var client = new ApiClient();\n    }\n}\n");

        var map = _reader.GetRepoMap(limit: 5, excludeTests: true);

        Assert.True(map.FileCount >= 3);
        Assert.Contains(map.Languages, item => item.Lang == "csharp");
        Assert.Contains(map.Modules, item => item.Module == "src");
        Assert.NotEmpty(map.TopFiles);
        Assert.NotEmpty(map.LargestFiles);
        Assert.NotEmpty(map.SymbolRichFiles);
        Assert.NotEmpty(map.ReferenceRichFiles);
        Assert.Contains(map.Entrypoints, item => item.Name == "Main" && item.Path == "src/Program.cs");
    }

    [Fact]
    public void GetRepoMap_AddsFileFallbackEntrypointForTopLevelProgram()
    {
        InsertIndexedFile("src/Program.cs", "csharp", "var client = new ApiClient();\nConsole.WriteLine(client);\n");

        var map = _reader.GetRepoMap(limit: 5, pathPatterns: new[] { "src/Program.cs" });

        Assert.Contains(map.Entrypoints, item => item.Kind == "file" && item.Name == "Program.cs" && item.Path == "src/Program.cs");
    }

    [Fact]
    public void GetRepoMap_KeepsScopedFreshnessAndAddsWorkspaceFreshness()
    {
        InsertIndexedFile("src/Program.cs", "csharp", "public class Program\n{\n    public static void Main(string[] args)\n    {\n    }\n}\n",
            modified: new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc));
        InsertIndexedFile("docs/guide.md", "markdown", "# Guide\n",
            modified: new DateTime(2025, 6, 3, 0, 0, 0, DateTimeKind.Utc));

        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            UPDATE files
            SET indexed_at = CASE path
                WHEN 'src/auth.py' THEN '2025-06-01 00:00:00'
                WHEN 'src/api.js' THEN '2025-06-01 00:00:00'
                WHEN 'src/Program.cs' THEN '2025-06-02 00:00:00'
                WHEN 'docs/guide.md' THEN '2025-06-04 00:00:00'
                WHEN 'docs/notes.md' THEN '2025-06-04 00:00:00'
                ELSE indexed_at
            END
            WHERE path IN ('src/auth.py', 'src/api.js', 'src/Program.cs', 'docs/guide.md', 'docs/notes.md')
            """;
        cmd.ExecuteNonQuery();

        var map = _reader.GetRepoMap(limit: 5, pathPatterns: new[] { "src/Program.cs" });

        Assert.Equal(new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc), map.IndexedAt);
        Assert.Equal(new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc), map.LatestModified);
        Assert.Equal(new DateTime(2025, 6, 4, 0, 0, 0, DateTimeKind.Utc), map.WorkspaceIndexedAt);
        Assert.Equal(new DateTime(2025, 6, 3, 0, 0, 0, DateTimeKind.Utc), map.WorkspaceLatestModified);
    }

    [Fact]
    public void GetFileByPath_ReturnsExactMatchWithFullMetadata()
    {
        // Seed data: src/api.js — Size=800, Lines=50, Modified=2025-06-01, 2 symbols (ApiClient, fetchData)
        // シードデータ: src/api.js — Size=800, Lines=50, Modified=2025-06-01, シンボル2個
        var file = _reader.GetFileByPath("src/api.js");
        Assert.NotNull(file);
        Assert.Equal("src/api.js", file!.Path);
        Assert.Equal("javascript", file.Lang);
        Assert.Equal(800, file.Size);
        Assert.Equal(50, file.Lines);
        Assert.Equal(2, file.SymbolCount);
        Assert.Equal(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), file.Modified);
        Assert.NotNull(file.IndexedAt);

        // Substring or partial path must return null / 部分一致は null を返す
        Assert.Null(_reader.GetFileByPath("api.js"));
        Assert.Null(_reader.GetFileByPath("api"));
        Assert.Null(_reader.GetFileByPath("src/api"));
        Assert.Null(_reader.GetFileByPath("nonexistent.py"));
    }

    [Fact]
    public void AnalyzeSymbol_BundlesDefinitionGraphAndNearbyContext()
    {
        var analysis = _reader.AnalyzeSymbol("fetchData", limit: 5, lang: "javascript", includeBody: true);

        var definition = Assert.Single(analysis.Definitions);
        Assert.Equal("fetchData", definition.Name);
        Assert.NotNull(analysis.File);
        Assert.Equal("src/api.js", analysis.File!.Path);
        Assert.NotNull(analysis.WorkspaceIndexedAt);
        Assert.Equal(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), analysis.WorkspaceLatestModified);
        Assert.Equal("javascript", analysis.GraphLanguage);
        Assert.True(analysis.GraphSupported);
        Assert.Contains("indexed", analysis.GraphSupportReason);
        Assert.Contains(analysis.NearbySymbols, item => item.Name == "ApiClient");
        Assert.Contains(analysis.Callees, item => item.CalleeName == "fetch");
    }

    [Fact]
    public void AnalyzeSymbol_PrefersExactDefinitionAsPrimaryAnchorWhenSubstringMatchesOverlap()
    {
        InsertIndexedFile("src/Services/ILoggerService.cs", "csharp",
            """
            public interface ILoggerService
            {
                void Log(string message);
            }
            """);
        InsertIndexedFile("src/Services/LoggerService.cs", "csharp",
            """
            public class LoggerService : ILoggerService
            {
                public void Log(string message) { }
                public void Execute() { }
            }
            """);

        var analysis = _reader.AnalyzeSymbol("loggerservice", limit: 1, lang: "csharp");

        Assert.NotNull(analysis.File);
        Assert.Equal("src/Services/LoggerService.cs", analysis.File!.Path);
        var definition = Assert.Single(analysis.Definitions);
        Assert.Equal("LoggerService", definition.Name);
        Assert.Equal("src/Services/LoggerService.cs", definition.Path);
        Assert.All(analysis.NearbySymbols, item => Assert.Equal("src/Services/LoggerService.cs", item.Path));
    }

    [Fact]
    public void AnalyzeSymbol_NonExactDoesNotUseFoldOnlyExactAnchor()
    {
        InsertIndexedFile("src/Intl/FullwidthRun.cs", "csharp",
            """
            public class Holder
            {
                public void Ｒｕｎ() { }
            }
            """);

        var analysis = _reader.AnalyzeSymbol("Run", limit: 1, lang: "csharp", exact: false);

        Assert.Null(analysis.File);
        Assert.Empty(analysis.Definitions);
        Assert.Empty(analysis.NearbySymbols);
        Assert.Null(analysis.ExactIndexAvailable);
        Assert.Null(analysis.DegradedReason);
    }

    [Fact]
    public void AnalyzeSymbol_UnsupportedLanguage_ReportsGraphSupportMetadata()
    {
        var analysis = _reader.AnalyzeSymbol("Heading", limit: 5, lang: "markdown");

        Assert.Equal("markdown", analysis.GraphLanguage);
        Assert.False(analysis.GraphSupported);
        Assert.Contains("not indexed", analysis.GraphSupportReason);
        Assert.Empty(analysis.Definitions);
        Assert.Empty(analysis.References);
        Assert.Empty(analysis.Callers);
        Assert.Empty(analysis.Callees);
    }

    public void Dispose()
    {
        _db.Dispose();
        DeleteDbPath();
    }

    private void DeleteDbPath()
    {
        if (!File.Exists(_dbPath))
            return;

        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (UnauthorizedAccessException)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
    }

    // --- Outline tests / アウトラインテスト ---

    [Fact]
    public void GetOutline_ReturnsSymbolsOrderedByLine()
    {
        var outline = _reader.GetOutline("src/auth.py");

        Assert.NotNull(outline);
        Assert.Equal("src/auth.py", outline!.Path);
        Assert.Equal("python", outline.Lang);
        Assert.True(outline.SymbolCount > 0);
        Assert.True(outline.TotalLines > 0);

        // Symbols should be ordered by line / シンボルは行順であるべき
        for (int i = 1; i < outline.Symbols.Count; i++)
            Assert.True(outline.Symbols[i].Line >= outline.Symbols[i - 1].Line,
                $"Symbol at index {i} (line {outline.Symbols[i].Line}) should be >= previous (line {outline.Symbols[i - 1].Line})");
    }

    [Fact]
    public void GetOutline_FileWithNoSymbols_ReturnsEmptyList()
    {
        var outline = _reader.GetOutline("docs/notes.md");

        Assert.NotNull(outline);
        Assert.Equal("docs/notes.md", outline!.Path);
        Assert.Equal(0, outline.SymbolCount);
        Assert.Empty(outline.Symbols);
    }

    [Fact]
    public void GetOutline_NonexistentFile_ReturnsNull()
    {
        var outline = _reader.GetOutline("nonexistent/file.cs");

        Assert.Null(outline);
    }

    [Fact]
    public void GetOutline_NullStartEndLine_FallsBackToLine()
    {
        // Insert a file with a symbol that has NULL start_line/end_line (#46)
        // start_line/end_lineがNULLのシンボルを持つファイルを挿入（#46）
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/nullcol.cs", Lang = "csharp", Size = 100, Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks([new ChunkRecord
        {
            FileId = fileId, ChunkIndex = 0, StartLine = 1, EndLine = 10,
            Content = "class Foo { void Bar() {} }",
        }]);
        // Insert symbol with NULL start_line and end_line via raw SQL /
        // start_lineとend_lineがNULLのシンボルを生SQLで挿入
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO symbols (file_id, kind, name, line, start_line, end_line)
                            VALUES (@fid, 'function', 'Bar', 5, NULL, NULL)";
        cmd.Parameters.AddWithValue("@fid", fileId);
        cmd.ExecuteNonQuery();

        var outline = _reader.GetOutline("src/nullcol.cs");

        Assert.NotNull(outline);
        var sym = Assert.Single(outline!.Symbols);
        Assert.Equal("Bar", sym.Name);
        Assert.Equal(5, sym.Line);
        // Falls back to line value when start_line/end_line are NULL / NULLの場合lineにフォールバック
        Assert.Equal(5, sym.StartLine);
        Assert.Equal(5, sym.EndLine);
    }

    [Fact]
    public void GetUnusedSymbols_ClassifiesConfidenceBucketsAndSortsPrivateFirst()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/config/unused_fixture.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 20,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "Hidden",
                Line = 3,
                StartLine = 3,
                EndLine = 3,
                Signature = "private void Hidden() { }",
                Visibility = "private",
                ContainerKind = "class",
                ContainerName = "ExportedApi",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "InternalOnly",
                Line = 5,
                StartLine = 5,
                EndLine = 5,
                Signature = "internal void InternalOnly() { }",
                Visibility = "internal",
                ContainerKind = "class",
                ContainerName = "ExportedApi",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "PathResolver",
                Line = 1,
                StartLine = 1,
                EndLine = 1,
                Signature = "public class PathResolver",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "AdoptionService",
                Line = 7,
                StartLine = 7,
                EndLine = 7,
                Signature = "public class AdoptionService",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "TokenService",
                Line = 8,
                StartLine = 8,
                EndLine = 8,
                Signature = "public class TokenService",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "AppSettings",
                Line = 9,
                StartLine = 9,
                EndLine = 11,
                Signature = "public class AppSettings",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "ApplyConfiguration",
                Line = 12,
                StartLine = 12,
                EndLine = 12,
                Signature = "public void ApplyConfiguration()",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "AppSettings",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "UseIOptions",
                Line = 13,
                StartLine = 13,
                EndLine = 13,
                Signature = "public void UseIOptions()",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "AppSettings",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "ConnectionString",
                Line = 10,
                StartLine = 10,
                EndLine = 10,
                Signature = "public string ConnectionString { get; set; }",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "AppSettings",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: "csharp",
            pathPatterns: ["unused_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.Equal(["Hidden", "InternalOnly", "PathResolver", "ConnectionString", "AdoptionService", "TokenService", "AppSettings", "ApplyConfiguration", "UseIOptions"], unused.Select(symbol => symbol.Name).ToArray());
        Assert.Equal("likely_unused_private", unused[0].UnusedBucket);
        Assert.Equal("medium", unused[0].UnusedConfidence);
        Assert.Equal("maybe_unused_nonpublic", unused[1].UnusedBucket);
        Assert.Equal("low", unused[1].UnusedConfidence);
        Assert.Equal("public_or_exported_no_refs", unused[2].UnusedBucket);
        Assert.Equal("reflection_or_config_suspect", unused[3].UnusedBucket);
        Assert.Contains("config or attribute-driven reflection", unused[3].UnusedReason);
        Assert.Equal("public_or_exported_no_refs", unused[4].UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", unused[5].UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", unused[6].UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", unused[7].UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", unused[8].UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "PathResolver").UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "AdoptionService").UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "TokenService").UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "ApplyConfiguration").UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "UseIOptions").UnusedBucket);
    }

    [Fact]
    public void GetUnusedSymbols_PlainCliOptionsProperties_StayInPublicBucket()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/cli_options_fixture.cs",
            Lang = "csharp",
            Size = 160,
            Lines = 6,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "CliOptions",
                Line = 1,
                StartLine = 1,
                EndLine = 4,
                Signature = "public sealed class CliOptions",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "ShowHelp",
                Line = 3,
                StartLine = 3,
                EndLine = 3,
                Signature = "public bool ShowHelp { get; init; }",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "CliOptions",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "ProjectPath",
                Line = 4,
                StartLine = 4,
                EndLine = 4,
                Signature = "public string? ProjectPath { get; init; }",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "CliOptions",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: "csharp",
            pathPatterns: ["cli_options_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "ShowHelp").UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "ProjectPath").UnusedBucket);
    }

    [Fact]
    public void GetUnusedSymbols_PrivateHelperWithSameFileUse_IsNotLabeledHighConfidence()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/local_use_fixture.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 20,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 8,
                Content = """
                public class LocalUseFixture
                {
                    public void Run() { Hidden(); }
                    private void Hidden() { }
                }
                """,
            }
        ]);
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "Run",
                Line = 3,
                StartLine = 3,
                EndLine = 3,
                Signature = "public void Run() { Hidden(); }",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "LocalUseFixture",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "Hidden",
                Line = 4,
                StartLine = 4,
                EndLine = 4,
                Signature = "private void Hidden() { }",
                Visibility = "private",
                ContainerKind = "class",
                ContainerName = "LocalUseFixture",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: "csharp",
            pathPatterns: ["local_use_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        var hidden = Assert.Single(unused, symbol => symbol.Name == "Hidden");
        Assert.Equal("likely_unused_private", hidden.UnusedBucket);
        Assert.Equal("medium", hidden.UnusedConfidence);
        Assert.Contains("same-file uses may still be missed", hidden.UnusedReason);
    }

    [Fact]
    public void GetUnusedSymbols_ReflectionAttributedProperty_IsClassifiedAsSuspect()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_unused_fixture.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 8,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName("full_name")]
                    public string FullName { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 6,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "FullName",
                Line = 5,
                StartLine = 5,
                EndLine = 5,
                Signature = "public string FullName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: "csharp",
            pathPatterns: ["reflection_unused_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "UserDto").UnusedBucket);
        var property = Assert.Single(unused, symbol => symbol.Name == "FullName");
        Assert.Equal("reflection_or_config_suspect", property.UnusedBucket);
        Assert.Contains("attribute-driven reflection surface", property.UnusedReason);
    }

    [Fact]
    public void GetUnusedSymbols_MultilineReflectionAttribute_IsClassifiedAsSuspect()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_multiline_fixture.cs",
            Lang = "csharp",
            Size = 240,
            Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 9,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName(
                        "full_name")]
                    public string FullName { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 7,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "FullName",
                Line = 7,
                StartLine = 5,
                EndLine = 7,
                Signature = "public string FullName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: "csharp",
            pathPatterns: ["reflection_multiline_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        var property = Assert.Single(unused, symbol => symbol.Name == "FullName");
        Assert.Equal("reflection_or_config_suspect", property.UnusedBucket);
    }

    [Fact]
    public void GetUnusedSymbols_CommentBetweenAttributeAndProperty_IsClassifiedAsSuspect()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_comment_fixture.cs",
            Lang = "csharp",
            Size = 260,
            Lines = 11,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 10,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName("full_name")]
                    /// Bound from JSON payload.
                    public string FullName { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 7,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "FullName",
                Line = 7,
                StartLine = 7,
                EndLine = 7,
                Signature = "public string FullName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: "csharp",
            pathPatterns: ["reflection_comment_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        var property = Assert.Single(unused, symbol => symbol.Name == "FullName");
        Assert.Equal("reflection_or_config_suspect", property.UnusedBucket);
    }

    [Fact]
    public void GetUnusedSymbols_QualifiedAndSuffixedAttributes_AreClassifiedCorrectly()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_qualified_fixture.cs",
            Lang = "csharp",
            Size = 420,
            Lines = 14,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 13,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [global::System.Text.Json.Serialization.JsonPropertyName("full_name")]
                    public string QualifiedName { get; set; } = string.Empty;

                    [JsonPropertyNameAttribute("display_name")]
                    public string SuffixedName { get; set; } = string.Empty;

                    [System.Text.Json.Serialization.JsonIgnoreAttribute]
                    public string IgnoredName { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 12,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "QualifiedName",
                Line = 6,
                StartLine = 6,
                EndLine = 6,
                Signature = "public string QualifiedName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "SuffixedName",
                Line = 9,
                StartLine = 9,
                EndLine = 9,
                Signature = "public string SuffixedName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "IgnoredName",
                Line = 12,
                StartLine = 12,
                EndLine = 12,
                Signature = "public string IgnoredName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: "csharp",
            pathPatterns: ["reflection_qualified_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.Equal("reflection_or_config_suspect", Assert.Single(unused, symbol => symbol.Name == "QualifiedName").UnusedBucket);
        Assert.Equal("reflection_or_config_suspect", Assert.Single(unused, symbol => symbol.Name == "SuffixedName").UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "IgnoredName").UnusedBucket);
    }

    [Fact]
    public void GetUnusedSymbols_BlockCommentBetweenAttributeAndProperty_IsClassifiedAsSuspect()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_block_comment_fixture.cs",
            Lang = "csharp",
            Size = 320,
            Lines = 11,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 10,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName("full_name")]
                    /* bound from payload
                       via serializer */
                    public string FullName { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 8,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "FullName",
                Line = 8,
                StartLine = 8,
                EndLine = 8,
                Signature = "public string FullName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: "csharp",
            pathPatterns: ["reflection_block_comment_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        var property = Assert.Single(unused, symbol => symbol.Name == "FullName");
        Assert.Equal("reflection_or_config_suspect", property.UnusedBucket);
    }

    [Fact]
    public void GetUnusedSymbols_LargePublicLimit_IsNotCappedAtBudget()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/large_public_unused_fixture.cs",
            Lang = "csharp",
            Size = 16000,
            Lines = 2600,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 1,
                Content = "public class PublicNoise0000 { }",
            }
        ]);

        var symbols = new List<SymbolRecord>();
        for (var i = 0; i < 2500; i++)
        {
            symbols.Add(new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = $"PublicNoise{i:D4}",
                Line = i + 1,
                StartLine = i + 1,
                EndLine = i + 1,
                Signature = $"public class PublicNoise{i:D4} {{ }}",
                Visibility = "public",
            });
        }
        _writer.InsertSymbols(symbols);

        var unused = _reader.GetUnusedSymbols(limit: 3000, kind: null, lang: "csharp",
            pathPatterns: ["large_public_unused_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.Equal(2500, unused.Count);
    }

    [Fact]
    public void GetUnusedSymbols_UnsupportedLanguageReturnsEmpty()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "script.sh",
            Lang = "shell",
            Size = 64,
            Lines = 4,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "helper",
                Line = 1,
                StartLine = 1,
                EndLine = 3,
                Signature = "helper() {",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 20, kind: null, lang: "shell",
            pathPatterns: null, excludePathPatterns: null, excludeTests: false);
        var count = _reader.CountUnusedSymbols(kind: null, lang: "shell",
            pathPatterns: null, excludePathPatterns: null, excludeTests: false);

        Assert.Empty(unused);
        Assert.Equal(0, count.Count);
        Assert.Equal(0, count.FileCount);
    }

    [Fact]
    public void GetUnusedSymbols_IgnoreAttributes_DoNotClassifyAsSuspect()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_ignore_fixture.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 12,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 10,
                Content = """
                using System.Runtime.Serialization;
                using System.Text.Json.Serialization;

                public class LegacyDto
                {
                    [JsonIgnore]
                    public string LegacyField { get; set; } = string.Empty;
                    [IgnoreDataMember]
                    public string LegacyAlias { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "LegacyDto",
                Line = 4,
                StartLine = 4,
                EndLine = 9,
                Signature = "public class LegacyDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "LegacyField",
                Line = 6,
                StartLine = 6,
                EndLine = 6,
                Signature = "public string LegacyField { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "LegacyDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "LegacyAlias",
                Line = 8,
                StartLine = 8,
                EndLine = 8,
                Signature = "public string LegacyAlias { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "LegacyDto",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: "csharp",
            pathPatterns: ["reflection_ignore_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "LegacyField").UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "LegacyAlias").UnusedBucket);
    }

    [Fact]
    public void GetUnusedSymbols_MissingChunks_DegradesReflectionClassificationWithoutCrashing()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_missing_chunks_fixture.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 8,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName("full_name")]
                    public string FullName { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 6,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "FullName",
                Line = 5,
                StartLine = 5,
                EndLine = 5,
                Signature = "public string FullName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);
        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = "DROP TABLE chunks;";
            cmd.ExecuteNonQuery();
        }

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: "csharp",
            pathPatterns: ["reflection_missing_chunks_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "FullName").UnusedBucket);
    }

    [Fact]
    public void GetUnusedSymbols_AdjacentProperties_DoNotLeakAttributeContext()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_adjacent_fixture.cs",
            Lang = "csharp",
            Size = 400,
            Lines = 16,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 14,
                Content = """
                using System.Runtime.Serialization;
                using System.Text.Json.Serialization;

                public class MixedDto
                {
                    [JsonPropertyName("decorated")]
                    public string Decorated { get; set; } = string.Empty;
                    public string Plain { get; set; } = string.Empty;
                    [JsonIgnore]
                    public string Ignored { get; set; } = string.Empty;
                    [JsonPropertyName("tagged")]
                    public string Tagged { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "MixedDto",
                Line = 4,
                StartLine = 4,
                EndLine = 11,
                Signature = "public class MixedDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "Decorated",
                Line = 7,
                StartLine = 7,
                EndLine = 7,
                Signature = "public string Decorated { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "MixedDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "Plain",
                Line = 8,
                StartLine = 8,
                EndLine = 8,
                Signature = "public string Plain { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "MixedDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "Ignored",
                Line = 10,
                StartLine = 10,
                EndLine = 10,
                Signature = "public string Ignored { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "MixedDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "Tagged",
                Line = 12,
                StartLine = 12,
                EndLine = 12,
                Signature = "public string Tagged { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "MixedDto",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: "csharp",
            pathPatterns: ["reflection_adjacent_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.Equal("reflection_or_config_suspect", Assert.Single(unused, symbol => symbol.Name == "Decorated").UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "Plain").UnusedBucket);
        Assert.Equal("public_or_exported_no_refs", Assert.Single(unused, symbol => symbol.Name == "Ignored").UnusedBucket);
        Assert.Equal("reflection_or_config_suspect", Assert.Single(unused, symbol => symbol.Name == "Tagged").UnusedBucket);
    }

    [Fact]
    public void GetUnusedSymbols_SmallLimitDiversifiesAcrossBuckets()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/diversified_unused_fixture.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 20,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 8,
                Content = """
                public class LocalUseFixture
                {
                    public void Run() { Hidden(); }
                    private void Hidden() { }
                    internal void InternalOnly() { }
                }
                """,
            }
        ]);
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "LocalUseFixture",
                Line = 1,
                StartLine = 1,
                EndLine = 5,
                Signature = "public class LocalUseFixture",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "Run",
                Line = 3,
                StartLine = 3,
                EndLine = 3,
                Signature = "public void Run() { Hidden(); }",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "LocalUseFixture",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "Hidden",
                Line = 4,
                StartLine = 4,
                EndLine = 4,
                Signature = "private void Hidden() { }",
                Visibility = "private",
                ContainerKind = "class",
                ContainerName = "LocalUseFixture",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "InternalOnly",
                Line = 5,
                StartLine = 5,
                EndLine = 5,
                Signature = "internal void InternalOnly() { }",
                Visibility = "internal",
                ContainerKind = "class",
                ContainerName = "LocalUseFixture",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 3, kind: null, lang: "csharp",
            pathPatterns: ["diversified_unused_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.Equal(["Hidden", "InternalOnly", "LocalUseFixture"], unused.Select(symbol => symbol.Name).ToArray());
        Assert.Equal(["likely_unused_private", "maybe_unused_nonpublic", "public_or_exported_no_refs"], unused.Select(symbol => symbol.UnusedBucket).ToArray());
    }

    [Fact]
    public void GetUnusedSymbols_SmallLimitIncludesReflectionAttributedSuspect()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_diversified_unused_fixture.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 12,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 10,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName("full_name")]
                    public string FullName { get; set; } = string.Empty;
                    public void Run() { Hidden(); }
                    private void Hidden() { }
                    internal void InternalOnly() { }
                }
                """,
            }
        ]);
        _writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 8,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "FullName",
                Line = 5,
                StartLine = 5,
                EndLine = 5,
                Signature = "public string FullName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "Run",
                Line = 6,
                StartLine = 6,
                EndLine = 6,
                Signature = "public void Run() { Hidden(); }",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "Hidden",
                Line = 7,
                StartLine = 7,
                EndLine = 7,
                Signature = "private void Hidden() { }",
                Visibility = "private",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "InternalOnly",
                Line = 8,
                StartLine = 8,
                EndLine = 8,
                Signature = "internal void InternalOnly() { }",
                Visibility = "internal",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);

        var unused = _reader.GetUnusedSymbols(limit: 4, kind: null, lang: "csharp",
            pathPatterns: ["reflection_diversified_unused_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.Equal(["Hidden", "InternalOnly", "UserDto", "FullName"], unused.Select(symbol => symbol.Name).ToArray());
        Assert.Equal(["likely_unused_private", "maybe_unused_nonpublic", "public_or_exported_no_refs", "reflection_or_config_suspect"], unused.Select(symbol => symbol.UnusedBucket).ToArray());
    }

    [Fact]
    public void GetUnusedSymbols_LargePublicNoiseStillFindsReflectionSuspect()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_noise_fixture.cs",
            Lang = "csharp",
            Size = 4000,
            Lines = 1200,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 8,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName("full_name")]
                    public string FullName { get; set; } = string.Empty;
                }
                """,
            }
        ]);

        var symbols = new List<SymbolRecord>();
        for (var i = 0; i < 1100; i++)
        {
            symbols.Add(new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = $"PublicNoise{i:D4}",
                Line = 20 + i,
                StartLine = 20 + i,
                EndLine = 20 + i,
                Signature = $"public class PublicNoise{i:D4}",
                Visibility = "public",
            });
        }
        symbols.Add(new SymbolRecord
        {
            FileId = fileId,
            Kind = "class",
            Name = "UserDto",
            Line = 3,
            StartLine = 3,
            EndLine = 6,
            Signature = "public class UserDto",
            Visibility = "public",
        });
        symbols.Add(new SymbolRecord
        {
            FileId = fileId,
            Kind = "property",
            Name = "FullName",
            Line = 5,
            StartLine = 5,
            EndLine = 5,
            Signature = "public string FullName { get; set; } = string.Empty;",
            Visibility = "public",
            ContainerKind = "class",
            ContainerName = "UserDto",
        });
        _writer.InsertSymbols(symbols);

        var unused = _reader.GetUnusedSymbols(limit: 4, kind: null, lang: "csharp",
            pathPatterns: ["reflection_noise_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.Contains(unused, symbol => symbol.Name == "FullName" && symbol.UnusedBucket == "reflection_or_config_suspect");
    }

    [Fact]
    public void GetUnusedSymbols_BoundedPublicOverfetch_DoesNotScanLateReflectionSuspect()
    {
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_budget_fixture.cs",
            Lang = "csharp",
            Size = 12000,
            Lines = 2600,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        _writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 2405,
                EndLine = 2412,
                Content = """
                using System.Text.Json.Serialization;

                public class LateDto
                {
                    [JsonPropertyName("late_name")]
                    public string LateName { get; set; } = string.Empty;
                }
                """,
            }
        ]);

        var symbols = new List<SymbolRecord>();
        for (var i = 0; i < 2200; i++)
        {
            symbols.Add(new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = $"PublicNoise{i:D4}",
                Line = 20 + i,
                StartLine = 20 + i,
                EndLine = 20 + i,
                Signature = $"public class PublicNoise{i:D4}",
                Visibility = "public",
            });
        }
        symbols.Add(new SymbolRecord
        {
            FileId = fileId,
            Kind = "class",
            Name = "LateDto",
            Line = 2407,
            StartLine = 2407,
            EndLine = 2410,
            Signature = "public class LateDto",
            Visibility = "public",
        });
        symbols.Add(new SymbolRecord
        {
            FileId = fileId,
            Kind = "property",
            Name = "LateName",
            Line = 2409,
            StartLine = 2409,
            EndLine = 2409,
            Signature = "public string LateName { get; set; } = string.Empty;",
            Visibility = "public",
            ContainerKind = "class",
            ContainerName = "LateDto",
        });
        _writer.InsertSymbols(symbols);

        var unused = _reader.GetUnusedSymbols(limit: 4, kind: null, lang: "csharp",
            pathPatterns: ["reflection_budget_fixture.cs"], excludePathPatterns: null, excludeTests: false);

        Assert.DoesNotContain(unused, symbol => symbol.Name == "LateName");
        Assert.Equal(["public_or_exported_no_refs", "public_or_exported_no_refs", "public_or_exported_no_refs", "public_or_exported_no_refs"],
            unused.Select(symbol => symbol.UnusedBucket).ToArray());
    }

    [Fact]
    public void GetUnusedSymbols_NullStartEndLine_DoesNotCrash()
    {
        // Regression: #49 — legacy indexes can have NULL start_line/end_line on symbol rows.
        // cdidx unused crashed with "The data is NULL at ordinal 5" before the COALESCE fix.
        // リグレッション: #49 — 古いインデックスは symbols 行の start_line/end_line が NULL になりうる。
        var fileId = _writer.UpsertFile(new FileRecord
        {
            Path = "src/unused_null.cs", Lang = "csharp", Size = 100, Lines = 10,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO symbols (file_id, kind, name, line, start_line, end_line)
                            VALUES (@fid, 'function', 'Orphan', 7, NULL, NULL)";
        cmd.Parameters.AddWithValue("@fid", fileId);
        cmd.ExecuteNonQuery();

        var unused = _reader.GetUnusedSymbols(limit: 10, kind: null, lang: null,
            pathPatterns: null, excludePathPatterns: null, excludeTests: false);

        var sym = Assert.Single(unused, s => s.Name == "Orphan");
        Assert.Equal(7, sym.Line);
        Assert.Equal(7, sym.StartLine);
        Assert.Equal(7, sym.EndLine);
    }

    [Fact]
    public void SearchReferences_ClampsLongSingleLineContextAroundMatch()
    {
        var longLine = "const x = 0; " + new string('a', 320) + " target(); " + new string('b', 320);
        InsertIndexedFile("dist/app.js", "javascript", longLine);

        var result = Assert.Single(_reader.SearchReferences("target", limit: 1, maxLineWidth: 96));

        Assert.True(result.ContextTruncated);
        Assert.Contains("target()", result.Context);
        Assert.True(result.Context.Length <= 96);
    }

    [Fact]
    public void GetExcerpt_ClampsLongSingleLineContentAroundFocus()
    {
        var longLine = new string('a', 320) + "TARGET" + new string('b', 320);
        InsertIndexedFile("dist/data.txt", "text", longLine);

        var excerpt = _reader.GetExcerpt(
            "dist/data.txt",
            1,
            1,
            maxLineWidth: 96,
            focusLine: 1,
            focusColumn: longLine.IndexOf("TARGET", StringComparison.Ordinal) + 1,
            focusLength: "TARGET".Length);

        Assert.NotNull(excerpt);
        Assert.True(excerpt!.ContentTruncated);
        Assert.DoesNotContain(longLine, excerpt.Content);
        Assert.Contains("TARGET", excerpt.Content);
        Assert.True(excerpt.Content.Length <= 96);
    }

    [Fact]
    public void GetExcerpt_WithoutFocusStillClampsLongSingleLineContent()
    {
        var longLine = new string('a', 320) + "TARGET" + new string('b', 320);
        InsertIndexedFile("dist/no-focus.txt", "text", longLine);

        var excerpt = _reader.GetExcerpt("dist/no-focus.txt", 1, 1, maxLineWidth: 96);

        Assert.NotNull(excerpt);
        Assert.True(excerpt!.ContentTruncated);
        Assert.DoesNotContain(longLine, excerpt.Content);
        Assert.True(excerpt.Content.Length <= 96);
    }

    [Fact]
    public void GetExcerpt_FocusColumnOutsideFocusedLineReturnsNull()
    {
        var longLine = new string('a', 320) + "TARGET" + new string('b', 320);
        InsertIndexedFile("dist/focus-column-range.txt", "text", longLine);

        var excerpt = _reader.GetExcerpt(
            "dist/focus-column-range.txt",
            1,
            1,
            maxLineWidth: 40,
            focusLine: 1,
            focusColumn: 9999,
            focusLength: 6);

        Assert.Null(excerpt);
    }

    [Fact]
    public void FindInFiles_ClampsLongSingleLineSnippetAroundMatch()
    {
        var longLine = new string('a', 320) + "target" + new string('b', 320);
        InsertIndexedFile("dist/search.txt", "text", longLine);

        var result = Assert.Single(_reader.FindInFiles("target", 1, pathPatterns: ["dist/search.txt"], exact: true, maxLineWidth: 96));

        Assert.True(result.SnippetTruncated);
        Assert.Contains("target", result.Snippet);
        Assert.True(result.Snippet.Length <= 96);
    }
}
