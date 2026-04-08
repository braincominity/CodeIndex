using Microsoft.Data.Sqlite;
using CodeIndex.Models;

namespace CodeIndex.Database;

/// <summary>
/// Handles INSERT/UPSERT operations to the database with batch commits.
/// バッチコミットによるINSERT/UPSERT処理を担当する。
/// </summary>
public class DbWriter
{
    private readonly SqliteConnection _conn;
    private const int BatchSize = 500;
    private int _transactionDepth;

    public DbWriter(SqliteConnection connection)
    {
        _conn = connection;
    }

    /// <summary>
    /// Begin a transaction or savepoint for grouping multiple operations atomically.
    /// SQLite does not support nested BEGIN TRANSACTION, so nested calls use SAVEPOINT.
    /// 複数操作をアトミックにまとめるためのトランザクションまたはセーブポイントを開始する。
    /// SQLiteはネストされたBEGIN TRANSACTIONをサポートしないため、ネスト時はSAVEPOINTを使用する。
    /// </summary>
    public TransactionScope BeginTransaction()
    {
        if (_transactionDepth == 0)
        {
            _transactionDepth++;
            var txn = _conn.BeginTransaction();
            return new TransactionScope(txn, this);
        }
        else
        {
            // Nested: use SAVEPOINT instead of BEGIN TRANSACTION
            // ネスト: BEGIN TRANSACTIONの代わりにSAVEPOINTを使用
            var name = $"sp_{_transactionDepth}";
            _transactionDepth++;
            Execute($"SAVEPOINT {name}");
            return new TransactionScope(name, _conn, this);
        }
    }

    /// <summary>
    /// RAII wrapper for transactions and savepoints.
    /// Ensures _transactionDepth is decremented and uncommitted changes are rolled back on Dispose.
    /// トランザクションとセーブポイントのRAIIラッパー。
    /// Dispose時に_transactionDepthを確実に減算し、未コミットの変更をロールバックする。
    /// </summary>
    public sealed class TransactionScope : IDisposable
    {
        private readonly SqliteTransaction? _transaction;
        private readonly string? _savepointName;
        private readonly SqliteConnection? _conn;
        private readonly DbWriter _writer;
        private bool _committed;
        private bool _disposed;

        // Real transaction / 実トランザクション
        internal TransactionScope(SqliteTransaction transaction, DbWriter writer)
        {
            _transaction = transaction;
            _writer = writer;
        }

        // Savepoint / セーブポイント
        internal TransactionScope(string savepointName, SqliteConnection conn, DbWriter writer)
        {
            _savepointName = savepointName;
            _conn = conn;
            _writer = writer;
        }

        public void Commit()
        {
            _committed = true;
            if (_transaction != null)
                _transaction.Commit();
            else
                ExecuteSql($"RELEASE SAVEPOINT {_savepointName}");
        }

        public void Rollback()
        {
            _committed = true;
            if (_transaction != null)
                _transaction.Rollback();
            else
                ExecuteSql($"ROLLBACK TO SAVEPOINT {_savepointName}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Rollback uncommitted changes / 未コミットの変更をロールバック
            if (!_committed)
            {
                try
                {
                    if (_transaction != null)
                        _transaction.Rollback();
                    else
                        ExecuteSql($"ROLLBACK TO SAVEPOINT {_savepointName}");
                }
                catch { /* Best effort during dispose / Dispose中はベストエフォート */ }
            }

            _transaction?.Dispose();
            if (_writer._transactionDepth > 0) _writer._transactionDepth--;
        }

        private void ExecuteSql(string sql)
        {
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private void Execute(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Check if a file needs re-indexing by comparing modified time and checksum.
    /// 更新日時とチェックサムを比較してファイルの再インデックスが必要か判定する。
    /// Returns the existing file ID if unchanged, or null if indexing is needed.
    /// If the timestamp differs but the checksum matches, updates the timestamp
    /// in the DB and returns the ID (content unchanged, e.g. after git checkout).
    /// 変更なしなら既存ファイルIDを返し、インデックスが必要ならnullを返す。
    /// タイムスタンプが異なってもチェックサムが一致すればDB側を更新しIDを返す。
    /// </summary>
    public long? GetUnchangedFileId(string relativePath, DateTime modified, string? checksum = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, modified, checksum FROM files WHERE path = @path";
        cmd.Parameters.AddWithValue("@path", relativePath);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var id = reader.GetInt64(0);
            var existingModified = reader.GetDateTime(1);

            // Fast path: timestamp unchanged / 高速パス: タイムスタンプ一致
            if (existingModified == modified)
                return id;

            // Slow path: timestamp changed but content may be the same (e.g. git checkout)
            // 低速パス: タイムスタンプは変わったが内容は同じ可能性（例: git checkout）
            if (checksum != null && !reader.IsDBNull(2))
            {
                var existingChecksum = reader.GetString(2);
                if (existingChecksum == checksum)
                {
                    // Update timestamp so next run takes the fast path
                    // 次回実行で高速パスを通るようタイムスタンプを更新
                    using var updateCmd = _conn.CreateCommand();
                    updateCmd.CommandText = "UPDATE files SET modified = @modified WHERE id = @id";
                    updateCmd.Parameters.AddWithValue("@modified", modified);
                    updateCmd.Parameters.AddWithValue("@id", id);
                    updateCmd.ExecuteNonQuery();
                    return id;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Clean up existing file data (FTS, chunks, symbols) before re-indexing.
    /// 再インデックス前に既存ファイルデータ（FTS、チャンク、シンボル）を削除する。
    /// </summary>
    public void CleanExistingFileData(string relativePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM files WHERE path = @path";
        cmd.Parameters.AddWithValue("@path", relativePath);
        var result = cmd.ExecuteScalar();
        if (result != null)
            DeleteFileData((long)result);
    }

    /// <summary>
    /// Upsert a file record and return its ID.
    /// Automatically cleans up existing FTS/chunk/symbol data first to prevent
    /// FTS orphan entries caused by INSERT OR REPLACE CASCADE deletes.
    /// ファイルレコードをUPSERTしてIDを返す。
    /// INSERT OR REPLACE の CASCADE 削除による FTS 孤立を防ぐため、
    /// 既存の FTS/チャンク/シンボルデータを先に自動クリーンアップする。
    /// </summary>
    public long UpsertFile(FileRecord file)
    {
        // Auto-cleanup existing data to prevent FTS orphans from INSERT OR REPLACE
        // INSERT OR REPLACE による FTS 孤立防止のため既存データを自動クリーンアップ
        CleanExistingFileData(file.Path);

        using var cmd = _conn.CreateCommand();
        // Use RETURNING to atomically insert and retrieve the ID
        // RETURNINGを使って挿入とID取得をアトミックに行う
        cmd.CommandText = @"
            INSERT OR REPLACE INTO files (path, lang, size, lines, checksum, modified, indexed_at)
            VALUES (@path, @lang, @size, @lines, @checksum, @modified, CURRENT_TIMESTAMP)
            RETURNING id";
        cmd.Parameters.AddWithValue("@path", file.Path);
        cmd.Parameters.AddWithValue("@lang", (object?)file.Lang ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@size", file.Size);
        cmd.Parameters.AddWithValue("@lines", file.Lines);
        cmd.Parameters.AddWithValue("@checksum", (object?)file.Checksum ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@modified", file.Modified);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Delete old chunks and symbols for a file before re-indexing.
    /// 再インデックス前にファイルの古いチャンクとシンボルを削除する。
    /// </summary>
    public void DeleteFileData(long fileId)
    {
        // FTS cleanup is handled automatically by fts_chunks_ad trigger on chunk deletion
        // FTSクリーンアップはチャンク削除時にfts_chunks_adトリガーで自動処理される
        using var cmd1 = _conn.CreateCommand();
        cmd1.CommandText = "DELETE FROM chunks WHERE file_id = @fid";
        cmd1.Parameters.AddWithValue("@fid", fileId);
        cmd1.ExecuteNonQuery();

        using var cmd2 = _conn.CreateCommand();
        cmd2.CommandText = "DELETE FROM symbols WHERE file_id = @fid";
        cmd2.Parameters.AddWithValue("@fid", fileId);
        cmd2.ExecuteNonQuery();
    }

    /// <summary>
    /// Insert chunks in batches and populate FTS index.
    /// チャンクをバッチ挿入し、FTSインデックスに反映する。
    /// </summary>
    public void InsertChunks(IReadOnlyList<ChunkRecord> chunks)
    {
        for (int i = 0; i < chunks.Count; i += BatchSize)
        {
            int end = Math.Min(i + BatchSize, chunks.Count);
            // Only create a batch transaction when not already inside an outer transaction
            // 外部トランザクション内でない場合のみバッチトランザクションを作成
            var ownTxn = !IsInTransaction();
            using var transaction = ownTxn ? _conn.BeginTransaction() : null;

            for (int j = i; j < end; j++)
            {
                var chunk = chunks[j];
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO chunks (file_id, chunk_index, start_line, end_line, content)
                    VALUES (@fid, @idx, @start, @end, @content)
                    RETURNING id";
                cmd.Parameters.AddWithValue("@fid", chunk.FileId);
                cmd.Parameters.AddWithValue("@idx", chunk.ChunkIndex);
                cmd.Parameters.AddWithValue("@start", chunk.StartLine);
                cmd.Parameters.AddWithValue("@end", chunk.EndLine);
                cmd.Parameters.AddWithValue("@content", chunk.Content);
                // FTS index is populated automatically by fts_chunks_ai trigger
                // FTSインデックスはfts_chunks_aiトリガーにより自動で反映される
                cmd.ExecuteNonQuery();
            }

            transaction?.Commit();
        }
    }

    /// <summary>
    /// Insert symbols in batches.
    /// シンボルをバッチ挿入する。
    /// </summary>
    public void InsertSymbols(IReadOnlyList<SymbolRecord> symbols)
    {
        for (int i = 0; i < symbols.Count; i += BatchSize)
        {
            int end = Math.Min(i + BatchSize, symbols.Count);
            // Only create a batch transaction when not already inside an outer transaction
            // 外部トランザクション内でない場合のみバッチトランザクションを作成
            var ownTxn = !IsInTransaction();
            using var transaction = ownTxn ? _conn.BeginTransaction() : null;

            for (int j = i; j < end; j++)
            {
                var symbol = symbols[j];
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO symbols (file_id, kind, name, line)
                    VALUES (@fid, @kind, @name, @line)";
                cmd.Parameters.AddWithValue("@fid", symbol.FileId);
                cmd.Parameters.AddWithValue("@kind", symbol.Kind);
                cmd.Parameters.AddWithValue("@name", symbol.Name);
                cmd.Parameters.AddWithValue("@line", symbol.Line);
                cmd.ExecuteNonQuery();
            }

            transaction?.Commit();
        }
    }

    /// <summary>
    /// Delete a file and its associated data by relative path. Returns true if found.
    /// 相対パスでファイルと関連データを削除する。見つかればtrueを返す。
    /// </summary>
    public bool DeleteFileByPath(string relativePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM files WHERE path = @path";
        cmd.Parameters.AddWithValue("@path", relativePath);
        var result = cmd.ExecuteScalar();
        if (result == null) return false;

        var fileId = (long)result;
        DeleteFileData(fileId);

        using var cmd2 = _conn.CreateCommand();
        cmd2.CommandText = "DELETE FROM files WHERE id = @id";
        cmd2.Parameters.AddWithValue("@id", fileId);
        cmd2.ExecuteNonQuery();
        return true;
    }

    /// <summary>
    /// Remove files from DB that no longer exist on disk (e.g. after branch switch).
    /// ディスク上に存在しなくなったファイルをDBから削除する（ブランチ切り替え対応）。
    /// </summary>
    /// <param name="projectRoot">Absolute path to project root / プロジェクトルートの絶対パス</param>
    /// <returns>Number of stale files removed / 削除された古いファイル数</returns>
    public int PurgeStaleFiles(string projectRoot)
    {
        // Collect all paths currently in DB / DB内の全パスを収集
        var dbPaths = new List<(long id, string path)>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, path FROM files";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                dbPaths.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        // Identify stale files (no longer on disk) / ディスク上に存在しないファイルを特定
        var staleIds = new List<long>();
        foreach (var (id, relativePath) in dbPaths)
        {
            var absolutePath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
                staleIds.Add(id);
        }

        if (staleIds.Count == 0)
            return 0;

        // Delete all stale files in a single transaction for atomicity and performance
        // アトミック性とパフォーマンスのため、全古いファイルを1トランザクションで削除
        using var txn = BeginTransaction();
        foreach (var id in staleIds)
        {
            DeleteFileData(id);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM files WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        txn.Commit();

        return staleIds.Count;
    }

    /// <summary>
    /// Get total counts for the summary output.
    /// サマリー出力用の合計件数を取得する。
    /// </summary>
    public (long files, long chunks, long symbols) GetCounts()
    {
        long files = ExecuteScalar("SELECT COUNT(*) FROM files");
        long chunks = ExecuteScalar("SELECT COUNT(*) FROM chunks");
        long symbols = ExecuteScalar("SELECT COUNT(*) FROM symbols");
        return (files, chunks, symbols);
    }

    private bool IsInTransaction() => _transactionDepth > 0;

    private long ExecuteScalar(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }
}
