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
            if (_transaction != null)
                _transaction.Commit();
            else
                ExecuteSql($"RELEASE SAVEPOINT {_savepointName}");
            // Set _committed after success so Dispose() will rollback if Commit/Release throws
            // コミット/リリース成功後にフラグを立て、失敗時はDispose()でロールバックされるようにする
            _committed = true;
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
    /// Uses ON CONFLICT DO UPDATE to preserve the existing file ID (avoids
    /// unnecessary AUTOINCREMENT growth from INSERT OR REPLACE's delete+insert).
    /// Cleans up old chunks/symbols before re-indexing.
    /// ファイルレコードをUPSERTしてIDを返す。
    /// ON CONFLICT DO UPDATEで既存IDを保持する（INSERT OR REPLACEの
    /// delete+insertによる不要なAUTOINCREMENT増加を回避）。
    /// 再インデックス前に古いチャンク/シンボルをクリーンアップする。
    /// </summary>
    public long UpsertFile(FileRecord file)
    {
        // Clean up old chunks/symbols so new ones can be inserted
        // 新しいチャンク/シンボル挿入のため古いデータをクリーンアップ
        CleanExistingFileData(file.Path);

        using var cmd = _conn.CreateCommand();
        // ON CONFLICT DO UPDATE preserves the existing row ID
        // ON CONFLICT DO UPDATEで既存の行IDを保持する
        cmd.CommandText = @"
            INSERT INTO files (path, lang, size, lines, checksum, modified, indexed_at)
            VALUES (@path, @lang, @size, @lines, @checksum, @modified, CURRENT_TIMESTAMP)
            ON CONFLICT(path) DO UPDATE SET
                lang = excluded.lang,
                size = excluded.size,
                lines = excluded.lines,
                checksum = excluded.checksum,
                modified = excluded.modified,
                indexed_at = CURRENT_TIMESTAMP
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
    /// Insert chunks in batches (FTS index is populated automatically by triggers).
    /// Reuses a single prepared statement for all rows to avoid per-row command overhead.
    /// チャンクをバッチ挿入する（FTSインデックスはトリガーにより自動で反映される）。
    /// 行ごとのコマンド生成コストを回避するため、単一のプリペアドステートメントを再利用する。
    /// </summary>
    public void InsertChunks(IReadOnlyList<ChunkRecord> chunks)
    {
        if (chunks.Count == 0) return;

        // Prepare the command once and reuse for all rows
        // コマンドを1回だけ準備し、全行で再利用する
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO chunks (file_id, chunk_index, start_line, end_line, content)
            VALUES (@fid, @idx, @start, @end, @content)";
        var pFid = cmd.Parameters.Add("@fid", SqliteType.Integer);
        var pIdx = cmd.Parameters.Add("@idx", SqliteType.Integer);
        var pStart = cmd.Parameters.Add("@start", SqliteType.Integer);
        var pEnd = cmd.Parameters.Add("@end", SqliteType.Integer);
        var pContent = cmd.Parameters.Add("@content", SqliteType.Text);
        cmd.Prepare();

        for (int i = 0; i < chunks.Count; i += BatchSize)
        {
            int end = Math.Min(i + BatchSize, chunks.Count);
            // Only create a batch transaction when not already inside an outer transaction
            // 外部トランザクション内でない場合のみバッチトランザクションを作成
            using var transaction = !IsInTransaction() ? BeginTransaction() : null;

            for (int j = i; j < end; j++)
            {
                var chunk = chunks[j];
                pFid.Value = chunk.FileId;
                pIdx.Value = chunk.ChunkIndex;
                pStart.Value = chunk.StartLine;
                pEnd.Value = chunk.EndLine;
                pContent.Value = chunk.Content;
                // FTS index is populated automatically by fts_chunks_ai trigger
                // FTSインデックスはfts_chunks_aiトリガーにより自動で反映される
                cmd.ExecuteNonQuery();
            }

            transaction?.Commit();
        }
    }

    /// <summary>
    /// Insert symbols in batches.
    /// Reuses a single prepared statement for all rows to avoid per-row command overhead.
    /// シンボルをバッチ挿入する。
    /// 行ごとのコマンド生成コストを回避するため、単一のプリペアドステートメントを再利用する。
    /// </summary>
    public void InsertSymbols(IReadOnlyList<SymbolRecord> symbols)
    {
        if (symbols.Count == 0) return;

        // Prepare the command once and reuse for all rows
        // コマンドを1回だけ準備し、全行で再利用する
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO symbols (file_id, kind, name, line)
            VALUES (@fid, @kind, @name, @line)";
        var pFid = cmd.Parameters.Add("@fid", SqliteType.Integer);
        var pKind = cmd.Parameters.Add("@kind", SqliteType.Text);
        var pName = cmd.Parameters.Add("@name", SqliteType.Text);
        var pLine = cmd.Parameters.Add("@line", SqliteType.Integer);
        cmd.Prepare();

        for (int i = 0; i < symbols.Count; i += BatchSize)
        {
            int end = Math.Min(i + BatchSize, symbols.Count);
            // Only create a batch transaction when not already inside an outer transaction
            // 外部トランザクション内でない場合のみバッチトランザクションを作成
            using var transaction = !IsInTransaction() ? BeginTransaction() : null;

            for (int j = i; j < end; j++)
            {
                var symbol = symbols[j];
                pFid.Value = symbol.FileId;
                pKind.Value = symbol.Kind;
                pName.Value = symbol.Name;
                pLine.Value = symbol.Line;
                cmd.ExecuteNonQuery();
            }

            transaction?.Commit();
        }
    }

    /// <summary>
    /// Delete a file and its associated data by relative path. Returns true if found.
    /// CASCADE on chunks/symbols + FTS triggers handle all cleanup automatically.
    /// 相対パスでファイルと関連データを削除する。見つかればtrueを返す。
    /// chunks/symbolsのCASCADE + FTSトリガーが全クリーンアップを自動処理する。
    /// </summary>
    public bool DeleteFileByPath(string relativePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM files WHERE path = @path";
        cmd.Parameters.AddWithValue("@path", relativePath);
        return cmd.ExecuteNonQuery() > 0;
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
        // CASCADE on chunks/symbols + FTS triggers handle all cleanup automatically
        // chunks/symbolsのCASCADE + FTSトリガーが全クリーンアップを自動処理する
        using var txn = BeginTransaction();
        using var deleteCmd = _conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM files WHERE id = @id";
        var pId = deleteCmd.Parameters.Add("@id", SqliteType.Integer);
        deleteCmd.Prepare();
        foreach (var id in staleIds)
        {
            pId.Value = id;
            deleteCmd.ExecuteNonQuery();
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

    /// <summary>
    /// Optimize FTS5 index to merge internal b-tree segments for better query performance.
    /// FTS5インデックスを最適化して内部b-treeセグメントを統合し、クエリ性能を改善する。
    /// </summary>
    public void OptimizeFts()
    {
        Execute("INSERT INTO fts_chunks(fts_chunks) VALUES('optimize')");
    }

    private bool IsInTransaction() => _transactionDepth > 0;

    private long ExecuteScalar(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }
}
