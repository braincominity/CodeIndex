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

    private void InsertIndexedFile(string path, string lang, string content, DateTime? modified = null)
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
}
