using CodeIndex.Cli;
using CodeIndex.Database;
using CodeIndex.Models;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Tests;

/// <summary>
/// End-to-end upgrade-path coverage for <see cref="DbContext.TryMigrateForRead"/>.
/// Seeds a DB with the legacy symbol schema (no start_line / end_line / signature / etc.),
/// then opens it through the normal read path so the opportunistic migration runs and
/// leaves the newly added columns as NULL — reproducing the real-world #58 failure mode.
/// レガシー symbol スキーマから read path 経由でアップグレードするシナリオを検証する。
/// </summary>
public class LegacySchemaMigrationTests : IDisposable
{
    private readonly string _dbDir;
    private readonly string _dbPath;

    public LegacySchemaMigrationTests()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), $"codeindex_legacy_upgrade_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dbDir);
        _dbPath = Path.Combine(_dbDir, "codeindex.db");
        SeedLegacyDb(_dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_dbDir))
            {
                // Restore writable perms in case a test left the dir read-only. / 読み取り専用状態を戻す。
                if (!OperatingSystem.IsWindows())
                {
                    try { File.SetUnixFileMode(_dbDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }
                }
                Directory.Delete(_dbDir, recursive: true);
            }
        }
        catch { /* ignore */ }
    }

    private static void SeedLegacyDb(string dbPath)
    {
        // Build a DB that matches the pre-column layout: no start_line / end_line /
        // body_start_line / body_end_line / signature / visibility / return_type /
        // container_kind / container_name on symbols, no symbol_references, no file_issues,
        // no checksum / indexed_at on files. This mirrors what older cdidx binaries produced.
        // 旧 cdidx が生成していたスキーマ。追加カラム・テーブルをすべて欠落させる。
        var builder = new SqliteConnectionStringBuilder { DataSource = dbPath };
        using var conn = new SqliteConnection(builder.ConnectionString);
        conn.Open();
        Exec(conn, @"CREATE TABLE files (
                        id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        path        TEXT NOT NULL UNIQUE,
                        lang        TEXT,
                        size        INTEGER,
                        lines       INTEGER,
                        modified    DATETIME
                    )");
        Exec(conn, @"CREATE TABLE chunks (
                        id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        file_id     INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                        chunk_index INTEGER NOT NULL,
                        start_line  INTEGER,
                        end_line    INTEGER,
                        content     TEXT,
                        UNIQUE(file_id, chunk_index)
                    )");
        Exec(conn, @"CREATE TABLE symbols (
                        id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        file_id     INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                        kind        TEXT,
                        name        TEXT,
                        line        INTEGER
                    )");

        // Seed one file and a handful of symbols on the legacy schema.
        // レガシースキーマに対してファイル1つとシンボルを数件投入。
        Exec(conn, "INSERT INTO files (id, path, lang, size, lines, modified) VALUES (1, 'src/Legacy.cs', 'csharp', 200, 20, '2025-06-01 00:00:00')");
        Exec(conn, @"INSERT INTO chunks (file_id, chunk_index, start_line, end_line, content) VALUES
                     (1, 0, 1, 20, 'class Legacy' || char(10) ||
                                   '{' || char(10) ||
                                   '    // top' || char(10) ||
                                   '' || char(10) ||
                                   '    void Alpha() {}' || char(10) ||
                                   '' || char(10) ||
                                   '' || char(10) ||
                                   '' || char(10) ||
                                   '    void Beta() {}' || char(10) ||
                                   '}')");
        Exec(conn, "INSERT INTO symbols (file_id, kind, name, line) VALUES (1, 'class', 'Legacy', 1)");
        Exec(conn, "INSERT INTO symbols (file_id, kind, name, line) VALUES (1, 'function', 'Alpha', 5)");
        Exec(conn, "INSERT INTO symbols (file_id, kind, name, line) VALUES (1, 'function', 'Beta', 9)");
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void SeedGraphDbWithoutExactFallbackIndexes(string dbPath)
    {
        using var db = new DbContext(dbPath);
        db.InitializeSchema();
        var writer = new DbWriter(db.Connection);

        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/session.py",
            Lang = "python",
            Size = 48,
            Lines = 2,
            Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = Guid.NewGuid().ToString("N"),
        });
        writer.InsertChunks([
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 2,
                Content = "def login(user, password):\n    return Run(user)\n",
            }
        ]);
        writer.InsertSymbols([
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "login",
                Line = 1,
                StartLine = 1,
                EndLine = 2,
                Signature = "def login(user, password):",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "Run",
                Line = 2,
                StartLine = 2,
                EndLine = 2,
                Signature = "Run(user)",
            }
        ]);
        writer.InsertReferences([
            new ReferenceRecord
            {
                FileId = fileId,
                SymbolName = "Run",
                ReferenceKind = "call",
                Line = 2,
                Column = 12,
                Context = "return Run(user)",
                ContainerKind = "function",
                ContainerName = "login",
            }
        ]);
        writer.MarkGraphReady();

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            DROP INDEX IF EXISTS idx_symbol_refs_name_nocase;
            DROP INDEX IF EXISTS idx_symbol_refs_container_nocase;
            PRAGMA wal_checkpoint(TRUNCATE);
            """;
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    private static void DropSymbolExactFallbackIndex(string dbPath)
    {
        using var db = new DbContext(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            DROP INDEX IF EXISTS idx_symbols_name_nocase;
            PRAGMA wal_checkpoint(TRUNCATE);
            """;
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void TryMigrateForRead_LegacyDb_ReadPathsDoNotCrash()
    {
        // Open through the normal read path — TryMigrateForRead adds the missing columns as NULL.
        // 通常の read path で開くと TryMigrateForRead が欠損カラムを NULL で追加する。
        using var db = new DbContext(_dbPath);
        db.TryMigrateForRead();

        // Confirm the migration actually ran: the new columns should now exist and be NULL.
        // マイグレーションが実際に走ったことを確認。
        using (var check = db.Connection.CreateCommand())
        {
            check.CommandText = "SELECT start_line, end_line, body_start_line, body_end_line, signature, visibility, return_type, container_kind, container_name FROM symbols WHERE name = 'Alpha'";
            using var r = check.ExecuteReader();
            Assert.True(r.Read());
            for (int i = 0; i < 9; i++) Assert.True(r.IsDBNull(i));
        }

        var reader = new DbReader(db.Connection);

        // GetOutline — falls back to line for NULL start/end. / start/end が NULL のとき line にフォールバック。
        var outline = reader.GetOutline("src/Legacy.cs");
        Assert.NotNull(outline);
        Assert.Equal(3, outline!.Symbols.Count);
        var alphaOutline = outline.Symbols.Single(s => s.Name == "Alpha");
        Assert.Equal(5, alphaOutline.Line);
        Assert.Equal(5, alphaOutline.StartLine);
        Assert.Equal(5, alphaOutline.EndLine);

        // SearchSymbols — by-name lookup must not crash on NULL ordinals.
        // by-name 検索が NULL 列でクラッシュしないこと。
        var hits = reader.SearchSymbols("Alpha");
        var alpha = Assert.Single(hits, s => s.Name == "Alpha");
        Assert.Equal("src/Legacy.cs", alpha.Path);
        Assert.Equal(5, alpha.StartLine);
        Assert.Equal(5, alpha.EndLine);

        // GetNearbySymbols — neighbor lookup around a focus line with NULL start_line.
        // 周辺シンボル取得。
        var nearby = reader.GetNearbySymbols("src/Legacy.cs", focusLine: 5, limit: 10);
        Assert.Contains(nearby, s => s.Name == "Beta");

        // GetUnusedSymbols — must NOT claim Alpha is unused just because TryMigrateForRead
        // created symbol_references as an empty table. A legacy DB upgraded on writable
        // storage has the tables but no graph content yet, so the only safe signal is
        // degraded (empty + GraphTableAvailable=false) until a real index run populates it.
        // migration で空の symbol_references を作っただけの DB で Alpha を「未使用」と
        // 報告してはいけない。populate されるまでは縮退扱いで空を返すのが正しい。
        var unused = reader.GetUnusedSymbols(limit: 10, kind: null, lang: null,
            pathPatterns: null, excludePathPatterns: null, excludeTests: false);
        Assert.Empty(unused);

        // AnalyzeSymbol — bundled inspect path exercises definitions + nearby + references.
        // inspect バンドル経路。
        var bundle = reader.AnalyzeSymbol("Alpha");
        Assert.NotNull(bundle);
        Assert.NotEmpty(bundle.Definitions);
        Assert.Equal("Alpha", bundle.Definitions[0].Name);
        Assert.Equal(5, bundle.Definitions[0].StartLine);
        // Same trust-boundary rule as read-only: migrated-but-unpopulated graph/issue tables
        // must be flagged degraded so callers cannot silently trust the empties.
        // 空テーブルは必ず縮退シグナルで呼び出し側に伝える。
        Assert.False(bundle.GraphTableAvailable);
        Assert.Empty(bundle.References);
        Assert.Empty(bundle.Callers);
        Assert.Empty(bundle.Callees);
        var status = reader.GetStatus();
        Assert.False(status.GraphTableAvailable);
        Assert.False(status.IssuesTableAvailable);
    }

    [Fact]
    public void TryMigrateForRead_ReadOnlyLegacyDb_MigrationSkippedAndReadPathsSurvive()
    {
        // Read-only half of the contract: when the connection cannot write, TryMigrateForRead
        // must silently no-op (SQLITE_READONLY / code 8), the legacy columns must remain absent,
        // and the reader fallback expressions must still drive the query paths without crashing.
        // read-only 契約: 書き込みできない接続では TryMigrateForRead が黙って no-op になり、
        // レガシーカラムは追加されないまま、fallback 式で read path が動き続ける必要がある。
        using var db = new DbContext(_dbPath);

        // Simulate a read-only connection at the SQL layer so ALTER TABLE fails with SQLITE_READONLY.
        // query_only は SQL レイヤで書き込みを禁止し、ALTER TABLE が SQLITE_READONLY で失敗する。
        using (var pragma = db.Connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA query_only = ON";
            pragma.ExecuteNonQuery();
        }

        // Should not throw — the SQLITE_READONLY catch inside TryMigrateForRead swallows it.
        // SQLITE_READONLY を握り潰す catch があるため例外は出ない。
        db.TryMigrateForRead();

        // The added columns must still be missing — migration really was skipped.
        // マイグレーションが本当にスキップされ、追加カラムが未だ存在しないことを確認。
        using (var check = db.Connection.CreateCommand())
        {
            check.CommandText = "PRAGMA table_info(symbols)";
            using var r = check.ExecuteReader();
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (r.Read()) cols.Add(r.GetString(1));
            Assert.DoesNotContain("start_line", cols);
            Assert.DoesNotContain("end_line", cols);
            Assert.DoesNotContain("signature", cols);
            Assert.DoesNotContain("visibility", cols);
        }

        // DbReader must detect the legacy layout at construction and pick the fallback SQL.
        // DbReader は構築時にレガシーレイアウトを検出し、fallback SQL に切り替える必要がある。
        var reader = new DbReader(db.Connection);

        var outline = reader.GetOutline("src/Legacy.cs");
        Assert.NotNull(outline);
        var alphaOutline = outline!.Symbols.Single(s => s.Name == "Alpha");
        Assert.Equal(5, alphaOutline.StartLine);
        Assert.Equal(5, alphaOutline.EndLine);

        var hits = reader.SearchSymbols("Alpha");
        Assert.Contains(hits, s => s.Name == "Alpha" && s.StartLine == 5);

        var nearby = reader.GetNearbySymbols("src/Legacy.cs", focusLine: 5, limit: 10);
        Assert.Contains(nearby, s => s.Name == "Beta");

        // #58 crash site — must not throw even when both the NEW columns AND the
        // symbol_references table are absent (migration skipped on read-only). Without a
        // references table every symbol would look unused, so the correct degradation is an
        // empty list rather than a flood of false positives or a crash.
        // 追加カラムと symbol_references テーブルの両方が欠けていてもクラッシュしないこと。
        // 参照が無い環境では全シンボルが未使用に見えるため、空リストへ縮退するのが正しい。
        var unused = reader.GetUnusedSymbols(limit: 10, kind: null, lang: null,
            pathPatterns: null, excludePathPatterns: null, excludeTests: false);
        Assert.Empty(unused);

        // AnalyzeSymbol still resolves definitions/nearby from the symbols table, but
        // references/callers/callees must degrade to empty lists instead of crashing.
        // definitions と nearby は symbols テーブルから解決でき、references 系は空に縮退する。
        var bundle = reader.AnalyzeSymbol("Alpha");
        Assert.NotEmpty(bundle.Definitions);
        Assert.Equal("Alpha", bundle.Definitions[0].Name);
        Assert.Equal(5, bundle.Definitions[0].StartLine);
        Assert.Empty(bundle.References);
        Assert.Empty(bundle.Callers);
        Assert.Empty(bundle.Callees);
        // Degraded-state signal: callers must be able to tell "table missing" from "real 0 hits".
        // テーブル欠損と真の 0 件を区別するシグナル。
        Assert.False(bundle.GraphTableAvailable);

        // Public commands backed by missing tables must also degrade, not crash.
        // 欠損テーブルに依存する公開コマンドも、クラッシュせず空を返すこと。
        Assert.Empty(reader.GetFileDependencies());
        Assert.Empty(reader.GetIssues());

        // Status must surface the degraded state so AI clients cannot silently misread a
        // missing-table 0-count as a real "clean" result.
        // 欠損テーブルによる 0 を「本当に 0」と誤解しないように status が必ずシグナルを出す。
        var status = reader.GetStatus();
        Assert.False(status.GraphTableAvailable);
        Assert.False(status.IssuesTableAvailable);
    }

    [Fact]
    public void DbContext_ReadOnlyFilesystem_FallsBackToReadOnlyOpen()
    {
        // Verify the DbContext read-only fallback for real — when the containing directory is
        // read-only, SQLite cannot create -journal / -shm / -wal side files, so a default
        // (writable) open fails with SQLITE_CANTOPEN. DbContext must retry with Mode=ReadOnly
        // and the subsequent read paths must work end-to-end on the legacy schema.
        // ディレクトリが read-only だと SQLite は副ファイルを作れず writable open が失敗するため、
        // DbContext は Mode=ReadOnly へフォールバックしなければならない。
        if (OperatingSystem.IsWindows())
        {
            // Windows directory permissions don't block SQLite journal creation the same way.
            // Windows のディレクトリパーミッションは journal 生成ブロックの意味が異なるためスキップ。
            return;
        }

        var originalMode = File.GetUnixFileMode(_dbDir);
        SqliteConnection.ClearAllPools();
        try
        {
            // Remove write and execute bits so -journal cannot be created. r-x only.
            // 書き込み・グループ・other を落として、副ファイル生成を禁止する。
            File.SetUnixFileMode(_dbDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            using var db = new DbContext(_dbPath);
            Assert.True(db.IsReadOnly, "DbContext should have fallen back to read-only open.");

            // Migration is skipped (catches SQLITE_READONLY). Reader still builds and query
            // paths — including the previously unguarded deps / issues — degrade to empty.
            // マイグレーションはスキップされ、deps / issues も空で縮退する。
            db.TryMigrateForRead();
            var reader = new DbReader(db.Connection);
            Assert.Empty(reader.GetFileDependencies());
            Assert.Empty(reader.GetIssues());

            var outline = reader.GetOutline("src/Legacy.cs");
            Assert.NotNull(outline);
            Assert.Equal(3, outline!.Symbols.Count);
        }
        finally
        {
            File.SetUnixFileMode(_dbDir, originalMode);
            SqliteConnection.ClearAllPools();
        }
    }

    [Fact]
    public void InterruptedIndex_SchemaInitializedButNotCompleted_StaysDegraded()
    {
        // An index run that creates the schema (InitializeSchema) but is killed before
        // MarkIndexComplete runs must NOT be trusted as authoritative. PRAGMA user_version
        // should remain 0 until the very end of a successful index, so downstream reads see
        // the empty graph/issues tables as degraded rather than "clean".
        // InitializeSchema だけ走って MarkIndexComplete に到達しなかった index は、
        // user_version=0 のまま残り、readers は空テーブルを縮退扱いにしなければならない。
        var interruptedDir = Path.Combine(Path.GetTempPath(), $"codeindex_interrupted_{Guid.NewGuid():N}");
        Directory.CreateDirectory(interruptedDir);
        var interruptedDb = Path.Combine(interruptedDir, "codeindex.db");
        try
        {
            using (var db = new DbContext(interruptedDb))
            {
                db.InitializeSchema();
                // simulate interruption: no writes, no MarkIndexComplete.
                // 中断シミュレーション: 書き込みも MarkIndexComplete も走らない。
                Assert.Equal(0, db.GetUserVersion());
            }

            SqliteConnection.ClearAllPools();

            using (var db = new DbContext(interruptedDb))
            {
                var reader = new DbReader(db.Connection);
                // Empty graph/issues tables on an unstamped DB must be reported degraded.
                // 版印なしの DB で空テーブルは縮退として扱うこと。
                var status = reader.GetStatus();
                Assert.False(status.GraphTableAvailable);
                Assert.False(status.IssuesTableAvailable);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(interruptedDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PartialIndex_GraphReadyStampedButNotIssuesReady_IsTreatedAsPartialTrust()
    {
        // MCP indexing runs no validate pass, so only MarkGraphReady() fires. The reader
        // must trust graph data but still flag issues as degraded. This locks in the split
        // so MCP-built DBs cannot silently pass `validate` with a false clean signal.
        // MCP は graph のみ stamp するため、issues は縮退のまま残さねばならない。
        var dir = Path.Combine(Path.GetTempPath(), $"codeindex_partial_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "codeindex.db");
        try
        {
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                db.ClearReadyFlags();
                var writer = new DbWriter(db.Connection);
                writer.MarkGraphReady(); // MCP-style: only graph, no issues
                var reader = new DbReader(db.Connection);
                var status = reader.GetStatus();
                Assert.True(status.GraphTableAvailable);
                Assert.False(status.IssuesTableAvailable);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void InterruptedRefresh_OnAlreadyStampedDb_DemotesTrustUntilCompletion()
    {
        // Refresh safety: an already-stamped DB that begins a re-index must be demoted to
        // degraded at the start (ClearReadyFlags), so an interrupted refresh does not leave
        // mixed/partial state looking authoritative.
        // 既に stamp 済みの DB でも reindex 開始時に ClearReadyFlags() で degraded に戻すこと。
        var dir = Path.Combine(Path.GetTempPath(), $"codeindex_refresh_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "codeindex.db");
        try
        {
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.MarkGraphReady();
                writer.MarkIssuesReady();
                writer.MarkFoldReady();
                Assert.Equal(DbContext.CurrentSchemaVersion, db.GetUserVersion());

                // Simulate the start of a refresh: clear readiness. An interrupted refresh
                // would leave it at 0 — trust is correctly demoted.
                // refresh 開始を模擬。中断されればここで止まり、縮退のまま残る。
                db.ClearReadyFlags();
                Assert.Equal(0, db.GetUserVersion());

                var reader = new DbReader(db.Connection);
                var status = reader.GetStatus();
                Assert.False(status.GraphTableAvailable);
                Assert.False(status.IssuesTableAvailable);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DbContext_UriConnectionString_OpensReadOnlyWithImmutableFlag()
    {
        // CLI escape hatch: --db file:///abs/path?immutable=1 must be passed through to
        // SQLite so users on restricted sandboxes can bypass the normal open path. The
        // constructor should detect the URI prefix, skip the writable-open attempt, and
        // mark the connection read-only.
        // --db に URI を渡された場合は writable open をスキップし read-only で開くこと。
        var fileUri = new Uri(_dbPath).AbsoluteUri + "?immutable=1";
        using var db = new DbContext(fileUri);
        Assert.True(db.IsReadOnly);

        var reader = new DbReader(db.Connection);
        var outline = reader.GetOutline("src/Legacy.cs");
        Assert.NotNull(outline);
        Assert.Equal(3, outline!.Symbols.Count);
    }

    [Fact]
    public void ReadOnlyDb_MissingExactGraphFallbackIndexes_SurfacesDegradedSignal()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"codeindex_exact_signal_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "codeindex.db");
        try
        {
            SeedGraphDbWithoutExactFallbackIndexes(dbPath);

            var fileUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            using var db = new DbContext(fileUri);
            db.TryMigrateForRead();
            var reader = new DbReader(db.Connection, db.IsReadOnly);

            var referencesSignal = reader.GetReferencesExactQuerySignal();
            var callersSignal = reader.GetCallersExactQuerySignal();
            var calleesSignal = reader.GetCalleesExactQuerySignal();
            var bundle = reader.AnalyzeSymbol("Run", exact: true);

            Assert.False(referencesSignal.ExactIndexAvailable);
            Assert.Contains("idx_symbol_refs_name_nocase", referencesSignal.DegradedReason);
            Assert.False(callersSignal.ExactIndexAvailable);
            Assert.Contains("idx_symbol_refs_name_nocase", callersSignal.DegradedReason);
            Assert.False(calleesSignal.ExactIndexAvailable);
            Assert.Contains("idx_symbol_refs_container_nocase", calleesSignal.DegradedReason);
            Assert.False(bundle.ExactIndexAvailable ?? true);
            Assert.Contains("idx_symbol_refs_name_nocase", bundle.DegradedReason);
            Assert.Contains("idx_symbol_refs_container_nocase", bundle.DegradedReason);
            Assert.Single(bundle.References);
            Assert.Single(bundle.Callers);
            Assert.Empty(bundle.Callees);

            var nonExactBundle = reader.AnalyzeSymbol("Run", exact: false);
            Assert.Null(nonExactBundle.ExactIndexAvailable);
            Assert.Null(nonExactBundle.DegradedReason);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ReadOnlyDb_MissingExactSymbolFallbackIndex_SurfacesDegradedSignal()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"codeindex_symbol_exact_signal_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "codeindex.db");
        try
        {
            SeedGraphDbWithoutExactFallbackIndexes(dbPath);
            DropSymbolExactFallbackIndex(dbPath);

            var fileUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            using var db = new DbContext(fileUri);
            db.TryMigrateForRead();
            var reader = new DbReader(db.Connection, db.IsReadOnly);

            var symbolsSignal = reader.GetSymbolsExactQuerySignal();
            var definitionSignal = reader.GetDefinitionExactQuerySignal();
            var symbolResults = reader.SearchSymbols("Run", exact: true);
            var definitionResults = reader.GetDefinitions("Run", exact: true);

            Assert.False(symbolsSignal.ExactIndexAvailable);
            Assert.Contains("idx_symbols_name_nocase", symbolsSignal.DegradedReason);
            Assert.False(definitionSignal.ExactIndexAvailable);
            Assert.Contains("idx_symbols_name_nocase", definitionSignal.DegradedReason);
            Assert.Single(symbolResults);
            Assert.Single(definitionResults);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritableLegacyDb_MissingExactSymbolFallbackIndex_SelfHealsDuringReadMigration()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"codeindex_symbol_exact_writable_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "codeindex.db");
        try
        {
            SeedGraphDbWithoutExactFallbackIndexes(dbPath);
            DropSymbolExactFallbackIndex(dbPath);

            using var db = new DbContext(dbPath);
            db.TryMigrateForRead();
            var reader = new DbReader(db.Connection, db.IsReadOnly);

            var symbolsSignal = reader.GetSymbolsExactQuerySignal();
            var definitionSignal = reader.GetDefinitionExactQuerySignal();

            Assert.True(symbolsSignal.ExactIndexAvailable);
            Assert.Null(symbolsSignal.DegradedReason);
            Assert.True(definitionSignal.ExactIndexAvailable);
            Assert.Null(definitionSignal.DegradedReason);

            using var check = db.Connection.CreateCommand();
            check.CommandText = "SELECT 1 FROM sqlite_master WHERE type='index' AND name='idx_symbols_name_nocase'";
            Assert.NotNull(check.ExecuteScalar());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void UpdateMode_OnLegacyDb_MustNotStampReadiness()
    {
        // Update mode (--commits / --files) touches only a subset of files, so stamping
        // readiness on a previously-degraded DB would falsely promote every untouched file
        // to authoritative. This test stamps `canStampReadiness=false`-equivalent semantics
        // at the DbWriter boundary and verifies no ready bit flips on.
        // update モードが legacy DB を trusted に昇格させないことを確認。
        var dir = Path.Combine(Path.GetTempPath(), $"codeindex_update_legacy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "codeindex.db");
        try
        {
            // Seed a legacy-shaped DB: InitializeSchema ran but readiness never stamped
            // (simulates a DB created by an older binary or a prior partial run).
            // 旧版バイナリ相当: スキーマだけ作って readiness は打たれていない状態。
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                Assert.Equal(0, db.GetUserVersion());
            }

            // Simulate RunIndex entry: capture wasFullyReady BEFORE clearing. Since the
            // starting user_version is 0, wasFullyReady is false, so canStampReadiness is
            // false in update mode — MarkReady must not fire.
            // RunIndex を模擬: wasFullyReady=false のため update モードでは stamp しない。
            using (var db = new DbContext(dbPath))
            {
                var wasFullyReady = db.GetUserVersion() == DbContext.CurrentSchemaVersion;
                Assert.False(wasFullyReady);
                db.ClearReadyFlags();
                // pretend RunUpdateMode ran with errors==0 but canStampReadiness=!isUpdate||wasFullyReady = false
                // so neither MarkGraphReady nor MarkIssuesReady fires.
                Assert.Equal(0, db.GetUserVersion());

                var reader = new DbReader(db.Connection);
                var status = reader.GetStatus();
                Assert.False(status.GraphTableAvailable);
                Assert.False(status.IssuesTableAvailable);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DbPathResolver_NormalizesFileUri_ForProjectRootResolution()
    {
        // The `--db file:///.cdidx/codeindex.db?immutable=1` escape hatch must not poison
        // metadata. ResolveProjectRootForQuery previously passed the raw URI into
        // Path.GetFullPath, producing /cwd/file:/abs/... garbage. Now it normalizes URIs
        // to local paths before the `.cdidx` heuristic, so workspace / git lookups recover
        // the same project root as a plain filesystem path.
        // --db が file: URI の場合でも、.cdidx 判定前にローカルパス化して正しい project root を返す。
        var dir = Path.Combine(Path.GetTempPath(), $"codeindex_uri_root_{Guid.NewGuid():N}");
        var cdidxDir = Path.Combine(dir, ".cdidx");
        Directory.CreateDirectory(cdidxDir);
        var dbPath = Path.Combine(cdidxDir, "codeindex.db");
        try
        {
            File.WriteAllBytes(dbPath, Array.Empty<byte>());
            var uri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var plainRoot = DbPathResolver.ResolveProjectRootForQuery(dbPath);
            var uriRoot = DbPathResolver.ResolveProjectRootForQuery(uri);
            Assert.Equal(plainRoot, uriRoot);
            Assert.Equal(Path.GetFullPath(dir), Path.GetFullPath(uriRoot));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DbContext_BareFileUri_FallsBackToFilesystemPath()
    {
        // A bare `file://` URI without immutable=1 / mode=ro must NOT take the read-only
        // escape hatch — SQLite's default open mode is read-write-CREATE, which would turn
        // a `status` on a missing-path URI into silent DB creation. Instead, DbContext
        // normalizes the URI to a filesystem path and uses the normal writable-open path.
        // 明示的 read-only 要求なしの file: URI は writable-open ではなく通常経路にフォールバック。
        using var db = new DbContext(new Uri(_dbPath).AbsoluteUri);
        Assert.False(db.IsReadOnly);
        var reader = new DbReader(db.Connection);
        Assert.NotNull(reader.GetOutline("src/Legacy.cs"));
    }

    [Fact]
    public void Rebuild_ClearsReadyFlagsBeforeDroppingTables()
    {
        // Crash-window guard: a previously-stamped DB that begins a --rebuild must clear the
        // readiness bits BEFORE DropAll / InitializeSchema, so a crash between recreating
        // empty tables and end-of-run stamping cannot leave old bits blessing empty data.
        // This mirrors the ordering in IndexCommandRunner / McpToolHandlers.
        // rebuild 開始時は DropAll より前に readiness をクリアする順序の固定。
        var dir = Path.Combine(Path.GetTempPath(), $"codeindex_rebuild_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "codeindex.db");
        try
        {
            // Stamp the DB as a previously-successful index.
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.MarkGraphReady();
                writer.MarkIssuesReady();
                writer.MarkFoldReady();
                Assert.Equal(DbContext.CurrentSchemaVersion, db.GetUserVersion());
            }
            SqliteConnection.ClearAllPools();

            // Simulate the production rebuild sequence: open → ClearReadyFlags → DropAll →
            // InitializeSchema. Interrupt before any writes or MarkReady. Bits must be 0.
            // 本番の rebuild 順序を模擬し、stamp 前に中断。readiness は 0 でなければならない。
            using (var db = new DbContext(dbPath))
            {
                db.ClearReadyFlags();
                db.DropAll();
                db.InitializeSchema();
                // intentionally no writes, no MarkGraphReady / MarkIssuesReady
                Assert.Equal(0, db.GetUserVersion());

                var reader = new DbReader(db.Connection);
                var status = reader.GetStatus();
                Assert.False(status.GraphTableAvailable);
                Assert.False(status.IssuesTableAvailable);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CompletedIndex_MarkIndexCompleteStampsUserVersion_AndTablesAreTrusted()
    {
        // Companion to the interrupted-index test: a full successful cycle
        // (InitializeSchema → writes → MarkIndexComplete) stamps user_version and the tables
        // are then trusted even if empty (legitimate docs-only repo).
        // 成功 index では MarkIndexComplete が版印を打ち、空テーブルでも trusted になる。
        var completedDir = Path.Combine(Path.GetTempPath(), $"codeindex_completed_{Guid.NewGuid():N}");
        Directory.CreateDirectory(completedDir);
        var completedDb = Path.Combine(completedDir, "codeindex.db");
        try
        {
            using (var db = new DbContext(completedDb))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.MarkGraphReady();
                writer.MarkIssuesReady();
                writer.MarkFoldReady();
                Assert.Equal(DbContext.CurrentSchemaVersion, db.GetUserVersion());

                var reader = new DbReader(db.Connection);
                var status = reader.GetStatus();
                Assert.True(status.GraphTableAvailable);
                Assert.True(status.IssuesTableAvailable);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(completedDir, recursive: true); } catch { }
        }
    }
}
