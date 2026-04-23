using CodeIndex.Indexer;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

/// <summary>
/// Manages SQLite connection and schema initialization.
/// SQLite接続とスキーマ初期化を管理する。
/// </summary>
public class DbContext : IDisposable
{
    private static readonly string[] RequiredCodeIndexTables =
    [
        "files",
        "chunks",
        "symbols",
    ];

    private readonly SqliteConnection _connection;
    private readonly bool _isReadOnly;

    public SqliteConnection Connection => _connection;
    public bool IsReadOnly => _isReadOnly;

    public static bool TryValidateExistingCodeIndexDb(string dbPath, out string message, out bool isNotFound)
    {
        message = string.Empty;
        isNotFound = false;

        if (dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && UriRequestsReadOnly(dbPath))
        {
            message = $"database must be writable for backfill-fold: {dbPath}";
            return false;
        }

        var openTarget = dbPath;
        if (dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = TryGetLocalPath(dbPath);
            if (normalized != null)
            {
                if (!File.Exists(normalized))
                {
                    message = $"database not found: {dbPath}";
                    isNotFound = true;
                    return false;
                }

                openTarget = normalized;
            }
        }
        else if (!File.Exists(dbPath))
        {
            message = $"database not found: {dbPath}";
            isNotFound = true;
            return false;
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = openTarget,
                Mode = SqliteOpenMode.ReadWrite,
            };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            using var reader = cmd.ExecuteReader();
            var tables = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read())
                tables.Add(reader.GetString(0));

            if (RequiredCodeIndexTables.All(tables.Contains))
                return true;

            message = $"database is not an existing CodeIndex DB: {dbPath}";
            return false;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 14)
        {
            message = $"database not found: {dbPath}";
            isNotFound = true;
            return false;
        }
        catch (SqliteException)
        {
            message = $"database is not an existing CodeIndex DB: {dbPath}";
            return false;
        }
    }

    public DbContext(string dbPath)
    {
        // Explicit URI form (file:///abs/path?immutable=1 etc.) — the user has opted into
        // a read-only open with SQLite-specific URI flags. Skip the writable-open attempt
        // and all write-oriented pragmas. This is the CLI escape hatch for sandboxes where
        // even SqliteOpenMode.ReadOnly cannot touch -shm/-wal side files.
        // URI 形式が渡された場合は writable open を省き、直接 read-only として扱う。
        if (dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            // URI escape hatch — but ONLY when the caller explicitly requested read-only
            // semantics. A bare `file:///path.db` without `immutable=1` / `mode=ro` falls
            // through to the normal filesystem path, otherwise SQLite's default open mode
            // is read-write-CREATE and a read command like `status` would silently create
            // or mutate a database. For read-only URIs we open the connection directly
            // and skip TryMigrateForRead / writable pragmas.
            // 明示的に read-only を要求した URI のみエスケープハッチ扱い。裸の file: URI は
            // 通常経路にフォールバックさせて read-write-CREATE の副作用を防ぐ。
            if (UriRequestsReadOnly(dbPath))
            {
                _connection = new SqliteConnection($"Data Source={dbPath}");
                _connection.Open();
                RegisterConnectionFunctions(_connection);
                _isReadOnly = true;
                Execute("PRAGMA busy_timeout=5000");
                return;
            }

            // Bare file: URI — normalize to a filesystem path and fall through.
            // immutable/mode=ro 指定のない file: URI はローカルパスに戻して通常経路で開く。
            var normalized = TryGetLocalPath(dbPath);
            if (normalized != null)
                dbPath = normalized;
        }

        // Use SqliteConnectionStringBuilder to prevent connection string injection
        // via paths containing ';' or other special characters.
        // SqliteConnectionStringBuilderで接続文字列インジェクションを防止する。
        var builder = new SqliteConnectionStringBuilder { DataSource = dbPath };

        try
        {
            _connection = new SqliteConnection(builder.ConnectionString);
            _connection.Open();
            RegisterConnectionFunctions(_connection);

            // Enable WAL mode and verify it was applied / WALモードを有効にし適用を確認
            var journalMode = ExecuteScalar("PRAGMA journal_mode=WAL");
            if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
                Console.Error.WriteLine($"Warning: WAL mode not enabled (got '{journalMode}')");
        }
        catch (SqliteException ex) when (IsReadOnlyOpenError(ex))
        {
            // Retry as read-only so indexes living on read-only filesystems / WORM storage /
            // sandbox mounts still drive the degraded read path (no WAL, no migration, no writes).
            // The immutable=1 URI flag is the crucial second step: without it, SQLite still tries
            // to read/lock the -shm/-wal side files and may fail with CANTOPEN on a sandbox that
            // allows reading the DB but nothing else in the directory. Immutable tells SQLite the
            // file will never change, bypassing all journal/wal machinery.
            // read-only FS / サンドボックスでも縮退 read path を動かせるようフォールバック。
            // immutable=1 を付けないと SQLite は -shm/-wal を触ろうとして CANTOPEN で落ちることがある。
            _connection?.Dispose();
            _connection = OpenReadOnly(dbPath);
            RegisterConnectionFunctions(_connection);
            _isReadOnly = true;
        }

        // Set busy timeout to avoid immediate SQLITE_BUSY errors on concurrent access
        // 同時アクセス時の即座のSQLITE_BUSYエラーを回避するためビジータイムアウトを設定
        Execute("PRAGMA busy_timeout=5000");

        if (!_isReadOnly)
        {
            Execute("PRAGMA foreign_keys=ON");
            var fkResult = ExecuteScalar("PRAGMA foreign_keys");
            if (fkResult != "1")
                Console.Error.WriteLine("Warning: foreign_keys pragma not enabled");
        }
    }

    // SQLITE_READONLY(8), SQLITE_CANTOPEN(14), SQLITE_IOERR(10). A read-only filesystem
    // typically surfaces as CANTOPEN because -journal/-shm cannot be created.
    // read-only FS では -journal / -shm を作れず CANTOPEN(14) を返すことが多い。
    private static bool IsReadOnlyOpenError(SqliteException ex) =>
        ex.SqliteErrorCode is 8 or 14 or 10;

    // Detect whether a SQLite URI explicitly requests read-only semantics. Only URIs that
    // set `immutable=1` or `mode=ro` take the read-only escape hatch — plain `file:`
    // URIs must not, or SQLite would open read-write-CREATE and a `status` call could
    // silently mutate or create the target DB.
    // `immutable=1` or `mode=ro` が明示されている場合のみ read-only として扱う。
    private static bool UriRequestsReadOnly(string uriText)
    {
        var qIdx = uriText.IndexOf('?');
        if (qIdx < 0) return false;
        var query = uriText[(qIdx + 1)..];
        foreach (var raw in query.Split('&'))
        {
            var seg = raw.Trim();
            if (seg.Equals("immutable=1", StringComparison.OrdinalIgnoreCase)) return true;
            if (seg.Equals("mode=ro", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // Best-effort: extract the filesystem path from a SQLite URI so -wal checks can run.
    // Returns null if parsing fails; the caller simply skips the gate in that case.
    // URI から filesystem path を取り出すベストエフォート。失敗したらゲートをスキップ。
    private static string? TryGetLocalPath(string uriText)
    {
        try
        {
            // Trim the query string (?immutable=1 etc.) before parsing so LocalPath is clean.
            var qIdx = uriText.IndexOf('?');
            var trimmed = qIdx >= 0 ? uriText[..qIdx] : uriText;
            var uri = new Uri(trimmed);
            return uri.IsFile ? uri.LocalPath : null;
        }
        catch
        {
            return null;
        }
    }

    private static SqliteConnection OpenReadOnly(string dbPath)
    {
        // Attempt 1: Mode=ReadOnly. Works for most read-only FS scenarios and, crucially,
        // still reads hot -wal state so nothing committed but not yet checkpointed is lost.
        // 第一段: Mode=ReadOnly。多くの read-only 環境で動作し、hot -wal の未チェックポイント
        // 済みコミットも正しく読める。
        try
        {
            var roBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
            };
            var conn = new SqliteConnection(roBuilder.ConnectionString);
            conn.Open();
            return conn;
        }
        catch (SqliteException)
        {
            // Attempt 2: immutable=1 URI. This bypasses -shm/-wal entirely, which is the only
            // way to survive a sandbox that cannot touch side files. Trade-off documented:
            // if the base DB has uncheckpointed WAL state, immutable will serve data that
            // predates those commits. We warn to stderr so the caller can see it, but do not
            // block — a file-size heuristic on `-wal` produces false positives (WAL files
            // remain allocated after checkpoint), and real hot-WAL detection requires the
            // very -shm/-wal access the sandbox is blocking. The explicit escape hatch
            // `--db file:///...?immutable=1` is the user's way to opt into the same
            // trade-off knowingly.
            // サンドボックスで -shm/-wal に触れない場合の最終手段。hot WAL 誤判定を避けるため、
            // ファイルサイズでの拒否はやめ、stderr 警告のみ出してフォールバック。
            Console.Error.WriteLine("Warning: falling back to SQLite immutable=1 read-only open. " +
                "If the base DB has uncheckpointed WAL state, the snapshot may be stale. " +
                "Re-run cdidx on writable storage to checkpoint WAL if this matters.");

            // Build the connection string directly instead of routing through
            // SqliteConnectionStringBuilder. The builder quotes DataSource values that
            // contain special characters, and the extra quoting was enough in some sandboxes
            // (observed by Codex: raw sqlite3 file:///... ?immutable=1 succeeds while the
            // builder-wrapped form fails with SQLITE_CANTOPEN). Uri.AbsoluteUri already
            // percent-encodes everything unsafe in a connection-string context (spaces, %,
            // ;, ", ', etc. all become %XX), so a raw concatenation is still injection-safe
            // for this specific input shape. Mode=ReadOnly is redundant with immutable=1 but
            // kept explicit so cdidx's intent is visible in logs / traces.
            // builder は DataSource を quote して URI 解釈を壊すため直接組む。
            // Uri.AbsoluteUri が全ての危険文字を %-エンコードするので raw 連結でも injection 安全。
            var fileUri = new Uri(Path.GetFullPath(dbPath)).AbsoluteUri; // e.g. file:///abs/path.db
            var rawConnStr = $"Data Source={fileUri}?immutable=1;Mode=ReadOnly";
            var conn = new SqliteConnection(rawConnStr);
            conn.Open();
            return conn;
        }
    }

    internal static void RegisterConnectionFunctions(SqliteConnection connection)
    {
        static int? ToNullableInt(long? value)
            => value is null || value < int.MinValue || value > int.MaxValue ? null : (int)value.Value;

        connection.CreateFunction(
            "sql_leaf_name",
            (string? name) => string.IsNullOrWhiteSpace(name) ? null : SqlNameResolver.GetLeafName(name));
        connection.CreateFunction(
            "sql_leaf_name_folded",
            (string? name) =>
            {
                if (string.IsNullOrWhiteSpace(name))
                    return null;

                var leafName = SqlNameResolver.GetLeafName(name);
                return leafName.Length == 0 ? null : NameFold.Fold(leafName) ?? leafName;
            });
        connection.CreateFunction(
            "sql_normalize_name",
            (string? name) => string.IsNullOrWhiteSpace(name) ? null : SqlNameResolver.NormalizeQualifiedName(name));
        connection.CreateFunction(
            "sql_normalize_name_folded",
            (string? name) =>
            {
                if (string.IsNullOrWhiteSpace(name))
                    return null;

                var normalizedName = SqlNameResolver.NormalizeQualifiedName(name);
                return normalizedName.Length == 0 ? null : NameFold.Fold(normalizedName) ?? normalizedName;
            });
        connection.CreateFunction(
            "sql_segment_count",
            (string? name) => string.IsNullOrWhiteSpace(name) ? (int?)null : SqlNameResolver.GetSegmentCount(name));
        connection.CreateFunction(
            "sql_context_has_name",
            (string? context, string? query) => SqlNameResolver.ContextContainsQualifiedName(context, query) ? 1 : 0);
        connection.CreateFunction(
            "sql_context_has_name_folded",
            (string? context, string? query) => SqlNameResolver.ContextContainsQualifiedNameFolded(context, query) ? 1 : 0);
        connection.CreateFunction(
            "sql_context_has_name_at",
            (string? context, string? query, long? columnNumber) =>
                SqlNameResolver.ContextContainsQualifiedNameAtColumn(context, query, ToNullableInt(columnNumber)) ? 1 : 0);
        connection.CreateFunction(
            "sql_context_has_name_folded_at",
            (string? context, string? query, long? columnNumber) =>
                SqlNameResolver.ContextContainsQualifiedNameFoldedAtColumn(context, query, ToNullableInt(columnNumber)) ? 1 : 0);
        connection.CreateFunction(
            "sql_context_like_name_at",
            (string? context, string? query, long? columnNumber) =>
                SqlNameResolver.ContextContainsQualifiedNameLikeAtColumn(context, query, ToNullableInt(columnNumber)) ? 1 : 0);
        connection.CreateFunction(
            "sql_context_like_name_folded_at",
            (string? context, string? query, long? columnNumber) =>
                SqlNameResolver.ContextContainsQualifiedNameLikeFoldedAtColumn(context, query, ToNullableInt(columnNumber)) ? 1 : 0);
        connection.CreateFunction(
            "sql_resolve_reference_name",
            (string? symbolName, string? context, string? containerName) =>
            {
                var resolved = SqlNameResolver.ResolveReferenceName(symbolName, context, containerName);
                return resolved.Length == 0 ? null : resolved;
            });
        connection.CreateFunction(
            "sql_resolve_reference_name_folded",
            (string? symbolName, string? context, string? containerName) =>
            {
                var resolved = SqlNameResolver.ResolveReferenceNameFolded(symbolName, context, containerName);
                return resolved.Length == 0 ? null : resolved;
            });
        connection.CreateFunction(
            "sql_resolve_reference_name_at",
            (string? symbolName, string? context, string? containerName, long? columnNumber) =>
            {
                var resolved = SqlNameResolver.ResolveReferenceNameAtColumn(symbolName, context, containerName, ToNullableInt(columnNumber));
                return resolved.Length == 0 ? null : resolved;
            });
        connection.CreateFunction(
            "sql_resolve_reference_name_folded_at",
            (string? symbolName, string? context, string? containerName, long? columnNumber) =>
            {
                var resolved = SqlNameResolver.ResolveReferenceNameFoldedAtColumn(symbolName, context, containerName, ToNullableInt(columnNumber));
                return resolved.Length == 0 ? null : resolved;
            });
    }

    /// <summary>
    /// Initialize the database schema (tables, indexes, FTS).
    /// データベーススキーマ（テーブル、インデックス、FTS）を初期化する。
    /// </summary>
    // Readiness bitmap stamped into PRAGMA user_version at the end of a successful index.
    // Split so the CLI (graph + issues) and MCP (graph only, no validation pass) can mark
    // different subsets of trust independently.
    // index の成功末尾で user_version に打つビットマップ。CLI と MCP が独立に立てる。
    public const int GraphReadyFlag = 1;
    public const int IssuesReadyFlag = 2;
    // bit 2 (FoldReadyFlag, #86) — name_folded columns (Unicode NFKC + lowerInvariant) fully
    // backfilled on symbols and symbol_references. Set only after a full scan populates every
    // row's folded value so `--exact` queries can use the folded index path for Unicode
    // casing (Ä/ä). Legacy DBs without fold stay on the COLLATE NOCASE fallback until reindex.
    // bit 2 (FoldReadyFlag, #86): name_folded 列の完全バックフィル完了を示す。
    public const int FoldReadyFlag = 4;
    public const int CurrentSchemaVersion = GraphReadyFlag | IssuesReadyFlag | FoldReadyFlag; // 7 — full CLI readiness
    // Query-semantic readiness for hotspot family grouping. Stored in codeindex_meta instead of
    // PRAGMA user_version because this guards a higher-level interpretation contract
    // (`family_key` / `container_qualified_name` are authoritative for the whole DB), not
    // low-level table availability.
    // hotspots family grouping 用 readiness。table の有無ではなく query 意味論の trust を表す。
    public const int HotspotFamilyVersion = 2;
    public const string HotspotFamilyVersionMetaKey = "hotspot_family_version";
    public const string HotspotFamilyMarkerFingerprintMetaKey = "hotspot_family_marker_fingerprint";
    public static string GetHotspotFamilyVersionMetaKey(string lang) => $"hotspot_family_version_{lang}";
    public static string GetHotspotFamilyMarkerFingerprintMetaKey(string lang) => $"hotspot_family_marker_fingerprint_{lang}";
    public const int CSharpSymbolNameContractVersion = 2;
    public const string CSharpSymbolNameContractVersionMetaKey = "csharp_symbol_name_contract_version";
    public const string IndexedProjectRootMetaKey = "indexed_project_root";

    public int GetUserVersion()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : (result is int i ? i : 0);
    }

    // Reset readiness bits. Called at the START of every index run so an interrupted run
    // on an already-stamped DB demotes the trust signal to degraded until the end-of-run
    // stamp is written on fully successful completion.
    // index 開始時にビットをクリア。途中で落ちた場合は縮退状態のまま残す。
    public void ClearReadyFlags()
    {
        Execute("PRAGMA user_version = 0");
    }

    /// <summary>
    /// Read a string value from `codeindex_meta`. Returns null when absent or the table
    /// hasn't been created (legacy DBs, read-only sandboxes where migration was skipped).
    /// codeindex_meta からの読み取り。テーブル未作成や未登録キーは null を返す。
    /// </summary>
    public string? GetMetaString(string key)
    {
        if (!TableExists("codeindex_meta")) return null;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM codeindex_meta WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var raw = cmd.ExecuteScalar();
        return raw is string s ? s : null;
    }

    private bool TableExists(string name)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        return cmd.ExecuteScalar() != null;
    }

    public void InitializeSchema()
    {
        // Files table / ファイルテーブル
        Execute(@"
            CREATE TABLE IF NOT EXISTS files (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                path        TEXT    NOT NULL UNIQUE,
                lang        TEXT,
                size        INTEGER,
                lines       INTEGER,
                checksum    TEXT,
                modified    DATETIME,
                indexed_at  DATETIME DEFAULT CURRENT_TIMESTAMP
            )");

        // Chunks table / チャンクテーブル
        Execute(@"
            CREATE TABLE IF NOT EXISTS chunks (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id     INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                chunk_index INTEGER NOT NULL,
                start_line  INTEGER,
                end_line    INTEGER,
                content     TEXT,
                UNIQUE(file_id, chunk_index)
            )");

        // Symbols table / シンボルテーブル
        Execute(@"
            CREATE TABLE IF NOT EXISTS symbols (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id         INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                kind            TEXT,
                name            TEXT,
                line            INTEGER,
                start_line      INTEGER,
                end_line        INTEGER,
                body_start_line INTEGER,
                body_end_line   INTEGER,
                signature       TEXT,
                container_kind  TEXT,
                container_name  TEXT,
                container_qualified_name TEXT,
                family_key      TEXT,
                visibility      TEXT,
                return_type     TEXT
            )");

        // Indexed references table / 参照インデックステーブル
        Execute(@"
            CREATE TABLE IF NOT EXISTS symbol_references (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id         INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                symbol_name     TEXT,
                reference_kind  TEXT,
                line            INTEGER,
                column_number   INTEGER,
                context         TEXT,
                container_kind  TEXT,
                container_name  TEXT
            )");

        // File validation issues table / ファイル検証問題テーブル
        Execute(@"
            CREATE TABLE IF NOT EXISTS file_issues (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id         INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                kind            TEXT NOT NULL,
                line            INTEGER NOT NULL DEFAULT 0,
                message         TEXT NOT NULL
            )");

        // Key-value metadata: fold algorithm version, future per-subsystem schema markers
        // that don't fit in PRAGMA user_version's 3-bit readiness bitmap. See
        // NameFold.Version and DbReader fold-ready gate.
        // メタデータ用 key-value: fold のアルゴリズム版数など、user_version bitmap に収まらない情報。
        Execute(@"
            CREATE TABLE IF NOT EXISTS codeindex_meta (
                key    TEXT PRIMARY KEY NOT NULL,
                value  TEXT
            )");

        // Schema migrations for existing DBs / 既存DB向けスキーマ移行
        EnsureColumn("files", "checksum", "TEXT");
        EnsureColumn("files", "modified", "DATETIME");
        EnsureColumn("files", "indexed_at", "DATETIME");
        EnsureColumn("symbols", "start_line", "INTEGER");
        EnsureColumn("symbols", "end_line", "INTEGER");
        EnsureColumn("symbols", "body_start_line", "INTEGER");
        EnsureColumn("symbols", "body_end_line", "INTEGER");
        EnsureColumn("symbols", "signature", "TEXT");
        EnsureColumn("symbols", "container_kind", "TEXT");
        EnsureColumn("symbols", "container_name", "TEXT");
        EnsureColumn("symbols", "container_qualified_name", "TEXT");
        EnsureColumn("symbols", "family_key", "TEXT");
        EnsureColumn("symbols", "visibility", "TEXT");
        EnsureColumn("symbols", "return_type", "TEXT");
        // #86: Unicode-aware folded name columns for `--exact` name matching across all
        // `--exact` command variants. Populated by the writer via NameFold.Fold; NULL on
        // legacy rows until a full reindex, in which case the reader falls back to the
        // COLLATE NOCASE path (correct for ASCII, misses non-ASCII casing — #86 fix).
        // #86: --exact 用の Unicode 折り畳み列。レガシー行は NULL のまま、再 index で埋まる。
        EnsureColumn("symbols", "name_folded", "TEXT");
        EnsureColumn("symbol_references", "symbol_name_folded", "TEXT");
        EnsureColumn("symbol_references", "container_name_folded", "TEXT");

        // Indexes / インデックス
        Execute("CREATE INDEX IF NOT EXISTS idx_files_lang     ON files(lang)");
        Execute("CREATE INDEX IF NOT EXISTS idx_files_modified ON files(modified)");
        // idx_files_path is not needed: the UNIQUE constraint on path already creates an implicit index
        // idx_files_path は不要: path の UNIQUE 制約が暗黙的にインデックスを作成済み
        Execute("CREATE INDEX IF NOT EXISTS idx_chunks_file    ON chunks(file_id)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbols_name   ON symbols(name)");
        // Case-insensitive exact-match index for `symbols --exact` (and MCP `symbols` exact=true).
        // Without this, `name = @q COLLATE NOCASE` falls back to a full symbols scan per query name,
        // which on multi-name exact lookups becomes O(names × symbols).
        // `symbols --exact` 用の大文字小文字無視 index。無いと multi-name exact でフルスキャンが N 回走る。
        Execute("CREATE INDEX IF NOT EXISTS idx_symbols_name_nocase ON symbols(name COLLATE NOCASE)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbols_file   ON symbols(file_id)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbols_start  ON symbols(start_line)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_name      ON symbol_references(symbol_name)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_file      ON symbol_references(file_id)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_container ON symbol_references(container_name)");
        // Compound indexes for common query patterns / よくあるクエリパターン用の複合インデックス
        Execute("CREATE INDEX IF NOT EXISTS idx_symbols_file_kind      ON symbols(file_id, kind)");
        Execute("CREATE INDEX IF NOT EXISTS idx_files_lang_modified     ON files(lang, modified)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_container_kind ON symbol_references(container_name, reference_kind)");
        // Indexes for new query patterns: --kind filter, visibility ranking, hotspot/unused analysis
        // 新しいクエリパターン用: --kind フィルタ、可視性ランキング、ホットスポット/未使用分析
        Execute("CREATE INDEX IF NOT EXISTS idx_symbols_kind            ON symbols(kind)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbols_visibility      ON symbols(visibility)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_name_kind   ON symbol_references(symbol_name, reference_kind)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_name_file   ON symbol_references(symbol_name, file_id)");
        // Case-insensitive exact-match indexes for `references --exact` / `callers --exact` / `callees --exact` (#83).
        // Mirror idx_symbols_name_nocase so `= @q COLLATE NOCASE` stays O(log n) per name across graph commands.
        // `references / callers / callees --exact` 用の NOCASE index。idx_symbols_name_nocase と対になる。
        Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_name_nocase      ON symbol_references(symbol_name COLLATE NOCASE)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_container_nocase ON symbol_references(container_name COLLATE NOCASE)");
        // #86: Indexes on the Unicode-folded columns. Used when FoldReadyFlag is set on the
        // DB (= the write path filled every folded column). Legacy / partial DBs keep using
        // the NOCASE indexes above. Both sets coexist so mixed-state DBs cannot regress.
        // #86: 折り畳み列のインデックス。FoldReadyFlag が立っている DB でだけ使う。
        Execute("CREATE INDEX IF NOT EXISTS idx_symbols_name_folded                ON symbols(name_folded)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_symbol_name_folded     ON symbol_references(symbol_name_folded)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_container_name_folded  ON symbol_references(container_name_folded)");

        // Full-text search / 全文検索
        Execute(@"
            CREATE VIRTUAL TABLE IF NOT EXISTS fts_chunks USING fts5(
                content,
                content='chunks',
                content_rowid='id'
            )");

        // FTS5 content-synced triggers — keep fts_chunks in sync with chunks table.
        // Without these, CASCADE DELETEs on chunks leave orphan entries in fts_chunks.
        // FTS5 content-synced トリガー — fts_chunksをchunksテーブルと同期する。
        // これがないとchunksのCASCADE DELETEでfts_chunksに孤立エントリが残る。
        Execute(@"
            CREATE TRIGGER IF NOT EXISTS fts_chunks_ai AFTER INSERT ON chunks BEGIN
                INSERT INTO fts_chunks(rowid, content) VALUES (new.id, new.content);
            END");
        Execute(@"
            CREATE TRIGGER IF NOT EXISTS fts_chunks_ad AFTER DELETE ON chunks BEGIN
                INSERT INTO fts_chunks(fts_chunks, rowid, content) VALUES('delete', old.id, old.content);
            END");
        Execute(@"
            CREATE TRIGGER IF NOT EXISTS fts_chunks_au AFTER UPDATE ON chunks BEGIN
                INSERT INTO fts_chunks(fts_chunks, rowid, content) VALUES('delete', old.id, old.content);
                INSERT INTO fts_chunks(rowid, content) VALUES (new.id, new.content);
            END");
    }

    /// <summary>
    /// Delete all data for a full rebuild.
    /// 全データを削除して完全再構築する。
    /// </summary>
    public void DropAll()
    {
        Execute("DROP TRIGGER IF EXISTS fts_chunks_ai");
        Execute("DROP TRIGGER IF EXISTS fts_chunks_ad");
        Execute("DROP TRIGGER IF EXISTS fts_chunks_au");
        Execute("DROP TABLE IF EXISTS fts_chunks");
        Execute("DROP TABLE IF EXISTS file_issues");
        Execute("DROP TABLE IF EXISTS symbol_references");
        Execute("DROP TABLE IF EXISTS symbols");
        Execute("DROP TABLE IF EXISTS chunks");
        Execute("DROP TABLE IF EXISTS files");
    }

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Attempt opportunistic schema migration for read-only query paths.
    /// Failures (e.g. read-only filesystem) are silently ignored — the DbReader
    /// fallback logic handles missing columns gracefully.
    /// 読み取り専用クエリパス向けの機会的スキーマ移行を試みる。
    /// 失敗（読み取り専用FS等）は無視する — DbReaderのフォールバックが欠損列を安全に処理する。
    /// </summary>
    public void TryMigrateForRead()
    {
        // Skip migration entirely on read-only connections. Even CREATE TABLE IF NOT EXISTS
        // fails with SQLITE_CANTOPEN on sandboxes that cannot create -journal side files —
        // previously only SQLITE_READONLY was caught, so the normal --db /path flow threw
        // on restricted mounts even after the constructor had already degraded to read-only.
        // read-only 接続ではマイグレーション DDL 自体を走らせない。CANTOPEN が漏れて落ちるため。
        if (_isReadOnly) return;

        try
        {
            // Ensure the references table exists for older DBs missing it
            // 古いDBに参照テーブルが無い場合に作成する
            Execute(@"
                CREATE TABLE IF NOT EXISTS symbol_references (
                    id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    file_id         INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                    symbol_name     TEXT,
                    reference_kind  TEXT,
                    line            INTEGER,
                    column_number   INTEGER,
                    context         TEXT,
                    container_kind  TEXT,
                    container_name  TEXT
                )");
            Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_name      ON symbol_references(symbol_name)");
            Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_file      ON symbol_references(file_id)");
            Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_container ON symbol_references(container_name)");
            Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_name_nocase      ON symbol_references(symbol_name COLLATE NOCASE)");
            Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_container_nocase ON symbol_references(container_name COLLATE NOCASE)");

            EnsureColumn("files", "checksum", "TEXT");
            EnsureColumn("files", "modified", "DATETIME");
            EnsureColumn("files", "indexed_at", "DATETIME");
            EnsureColumn("symbols", "start_line", "INTEGER");
            EnsureColumn("symbols", "end_line", "INTEGER");
            EnsureColumn("symbols", "body_start_line", "INTEGER");
            EnsureColumn("symbols", "body_end_line", "INTEGER");
            EnsureColumn("symbols", "signature", "TEXT");
            EnsureColumn("symbols", "container_kind", "TEXT");
            EnsureColumn("symbols", "container_name", "TEXT");
            EnsureColumn("symbols", "container_qualified_name", "TEXT");
            EnsureColumn("symbols", "family_key", "TEXT");
            EnsureColumn("symbols", "visibility", "TEXT");
            EnsureColumn("symbols", "return_type", "TEXT");
            Execute("CREATE INDEX IF NOT EXISTS idx_symbols_name_nocase ON symbols(name COLLATE NOCASE)");
            // #86: fold columns must be ensured BEFORE the folded indexes so CREATE INDEX does
            // not fail on legacy DBs where the column did not exist yet.
            // #86: folded 列を追加してから folded index を作らないと legacy DB でクラッシュする。
            EnsureColumn("symbols", "name_folded", "TEXT");
            EnsureColumn("symbol_references", "symbol_name_folded", "TEXT");
            EnsureColumn("symbol_references", "container_name_folded", "TEXT");
            Execute("CREATE INDEX IF NOT EXISTS idx_symbols_name_folded                ON symbols(name_folded)");
            Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_symbol_name_folded     ON symbol_references(symbol_name_folded)");
            Execute("CREATE INDEX IF NOT EXISTS idx_symbol_refs_container_name_folded  ON symbol_references(container_name_folded)");

            // Ensure file_issues table for older DBs / 古いDBに file_issues テーブルが無い場合に作成
            Execute(@"
                CREATE TABLE IF NOT EXISTS file_issues (
                    id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    file_id         INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                    kind            TEXT NOT NULL,
                    line            INTEGER NOT NULL DEFAULT 0,
                    message         TEXT NOT NULL
                )");

            // #86 codex third-pass review: metadata table for fold-algorithm version guard.
            Execute(@"
                CREATE TABLE IF NOT EXISTS codeindex_meta (
                    key    TEXT PRIMARY KEY NOT NULL,
                    value  TEXT
                )");
        }
        catch (SqliteException ex) when (IsReadOnlyOpenError(ex))
        {
            // Read-only DB / filesystem / sandbox — silently degrade. Catches SQLITE_READONLY
            // (8), SQLITE_IOERR (10), and SQLITE_CANTOPEN (14): some restricted environments
            // report CANTOPEN when SQLite tries to create -journal side files for the DDL.
            // DbReader.LoadColumns() / table-detection will drive the degraded read path.
            // 読み取り専用 DB・FS・サンドボックスでの DDL 失敗を全部縮退として扱う。
        }
    }

    private void EnsureColumn(string tableName, string columnName, string definition)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";

        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        try
        {
            Execute($"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 &&
                                         ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // Another process or an earlier partial migration may have added the
            // column after PRAGMA inspection. Treat it as already migrated.
            // 別プロセスや直前の部分移行で列が追加済みの可能性があるため、移行済みとして扱う。
        }
    }

    private string ExecuteScalar(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
