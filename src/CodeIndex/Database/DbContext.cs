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
                snippet     TEXT,
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
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                kind    TEXT,
                name    TEXT,
                line    INTEGER
            )");

        // Indexes / インデックス
        Execute("CREATE INDEX IF NOT EXISTS idx_files_lang     ON files(lang)");
        Execute("CREATE INDEX IF NOT EXISTS idx_files_modified ON files(modified)");
        Execute("CREATE INDEX IF NOT EXISTS idx_files_path     ON files(path)");
        Execute("CREATE INDEX IF NOT EXISTS idx_chunks_file    ON chunks(file_id)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbols_name   ON symbols(name)");
        Execute("CREATE INDEX IF NOT EXISTS idx_symbols_file   ON symbols(file_id)");

        // Full-text search / 全文検索
        Execute(@"
            CREATE VIRTUAL TABLE IF NOT EXISTS fts_chunks USING fts5(
                content,
                content='chunks',
                content_rowid='id'
            )");
    }

    /// <summary>
    /// Delete all data for a full rebuild.
    /// 全データを削除して完全再構築する。
    /// </summary>
    public void DropAll()
    {
        Execute("DROP TABLE IF EXISTS fts_chunks");
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
