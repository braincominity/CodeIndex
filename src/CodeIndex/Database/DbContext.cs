using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

/// <summary>
/// Manages SQLite connection and schema initialization.
/// SQLite接続とスキーマ初期化を管理する。
/// </summary>
public class DbContext : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteConnection Connection => _connection;

    public DbContext(string dbPath)
    {
        // Use SqliteConnectionStringBuilder to prevent connection string injection
        // via paths containing ';' or other special characters.
        // SqliteConnectionStringBuilderで接続文字列インジェクションを防止する。
        var builder = new SqliteConnectionStringBuilder { DataSource = dbPath };
        _connection = new SqliteConnection(builder.ConnectionString);
        _connection.Open();

        // Enable WAL mode and verify it was applied / WALモードを有効にし適用を確認
        var journalMode = ExecuteScalar("PRAGMA journal_mode=WAL");
        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
            Console.Error.WriteLine($"Warning: WAL mode not enabled (got '{journalMode}')");

        // Set busy timeout to avoid immediate SQLITE_BUSY errors on concurrent access
        // 同時アクセス時の即座のSQLITE_BUSYエラーを回避するためビジータイムアウトを設定
        Execute("PRAGMA busy_timeout=5000");

        Execute("PRAGMA foreign_keys=ON");
        var fkResult = ExecuteScalar("PRAGMA foreign_keys");
        if (fkResult != "1")
            Console.Error.WriteLine("Warning: foreign_keys pragma not enabled");
    }

    /// <summary>
    /// Initialize the database schema (tables, indexes, FTS).
    /// データベーススキーマ（テーブル、インデックス、FTS）を初期化する。
    /// </summary>
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
        EnsureColumn("symbols", "visibility", "TEXT");
        EnsureColumn("symbols", "return_type", "TEXT");

        // Indexes / インデックス
        Execute("CREATE INDEX IF NOT EXISTS idx_files_lang     ON files(lang)");
        Execute("CREATE INDEX IF NOT EXISTS idx_files_modified ON files(modified)");
        // idx_files_path is not needed: the UNIQUE constraint on path already creates an implicit index
        // idx_files_path は不要: path の UNIQUE 制約が暗黙的にインデックスを作成済み
        Execute("CREATE INDEX IF NOT EXISTS idx_chunks_file    ON chunks(file_id)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbols_name   ON symbols(name)");
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
            EnsureColumn("symbols", "visibility", "TEXT");
            EnsureColumn("symbols", "return_type", "TEXT");

            // Ensure file_issues table for older DBs / 古いDBに file_issues テーブルが無い場合に作成
            Execute(@"
                CREATE TABLE IF NOT EXISTS file_issues (
                    id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    file_id         INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                    kind            TEXT NOT NULL,
                    line            INTEGER NOT NULL DEFAULT 0,
                    message         TEXT NOT NULL
                )");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 8 /* SQLITE_READONLY */)
        {
            // Read-only DB or filesystem — silently degrade.
            // DbReader.LoadColumns() will detect what is available.
            // 読み取り専用DBまたはFS — 黙って縮退する。
            // DbReader.LoadColumns() が利用可能な列を検出する。
        }
    }

    private void EnsureColumn(string tableName, string columnName, string definition)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
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
