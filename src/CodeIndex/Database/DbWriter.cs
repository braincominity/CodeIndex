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
    /// Begin a transaction for grouping multiple operations atomically.
    /// 複数操作をアトミックにまとめるためのトランザクションを開始する。
    /// </summary>
    public SqliteTransaction BeginTransaction()
    {
        _transactionDepth++;
        return _conn.BeginTransaction();
    }

    /// <summary>
    /// Notify that the outer transaction has ended (committed or rolled back).
    /// 外部トランザクション終了を通知する。
    /// </summary>
    public void EndTransaction()
    {
        if (_transactionDepth > 0) _transactionDepth--;
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
    /// Must be called BEFORE UpsertFile to prevent FTS orphan entries caused by
    /// INSERT OR REPLACE triggering CASCADE deletes that bypass FTS cleanup.
    /// INSERT OR REPLACE の CASCADE 削除が FTS をバイパスするため、UpsertFile の前に呼ぶこと。
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
    /// ファイルレコードをUPSERTしてIDを返す。
    /// NOTE: Call CleanExistingFileData() before this method to avoid FTS orphans.
    /// 注意: FTS孤立エントリを防ぐため、このメソッドの前にCleanExistingFileData()を呼ぶこと。
    /// </summary>
    public long UpsertFile(FileRecord file)
    {
        using var cmd = _conn.CreateCommand();
        // Use RETURNING to atomically insert and retrieve the ID
        // RETURNINGを使って挿入とID取得をアトミックに行う
        cmd.CommandText = @"
            INSERT OR REPLACE INTO files (path, lang, size, lines, snippet, checksum, modified, indexed_at)
            VALUES (@path, @lang, @size, @lines, @snippet, @checksum, @modified, CURRENT_TIMESTAMP)
            RETURNING id";
        cmd.Parameters.AddWithValue("@path", file.Path);
        cmd.Parameters.AddWithValue("@lang", (object?)file.Lang ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@size", file.Size);
        cmd.Parameters.AddWithValue("@lines", file.Lines);
        cmd.Parameters.AddWithValue("@snippet", (object?)file.Snippet ?? DBNull.Value);
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
        using var cmd1 = _conn.CreateCommand();
        cmd1.CommandText = "DELETE FROM fts_chunks WHERE rowid IN (SELECT id FROM chunks WHERE file_id = @fid)";
        cmd1.Parameters.AddWithValue("@fid", fileId);
        cmd1.ExecuteNonQuery();

        using var cmd2 = _conn.CreateCommand();
        cmd2.CommandText = "DELETE FROM chunks WHERE file_id = @fid";
        cmd2.Parameters.AddWithValue("@fid", fileId);
        cmd2.ExecuteNonQuery();

        using var cmd3 = _conn.CreateCommand();
        cmd3.CommandText = "DELETE FROM symbols WHERE file_id = @fid";
        cmd3.Parameters.AddWithValue("@fid", fileId);
        cmd3.ExecuteNonQuery();
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
            var transaction = ownTxn ? _conn.BeginTransaction() : null;

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
                var chunkId = (long)cmd.ExecuteScalar()!;

                // Populate FTS index with explicit chunk ID / 明示的なチャンクIDでFTSインデックスに追加
                using var ftsCmd = _conn.CreateCommand();
                ftsCmd.CommandText = @"
                    INSERT INTO fts_chunks (rowid, content)
                    VALUES (@rowid, @content)";
                ftsCmd.Parameters.AddWithValue("@rowid", chunkId);
                ftsCmd.Parameters.AddWithValue("@content", chunk.Content);
                ftsCmd.ExecuteNonQuery();
            }

            transaction?.Commit();
            transaction?.Dispose();
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
            var transaction = ownTxn ? _conn.BeginTransaction() : null;

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
            transaction?.Dispose();
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
        using var transaction = _conn.BeginTransaction();
        foreach (var id in staleIds)
        {
            DeleteFileData(id);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM files WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();

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
