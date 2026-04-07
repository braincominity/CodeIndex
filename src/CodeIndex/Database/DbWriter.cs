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

    public DbWriter(SqliteConnection connection)
    {
        _conn = connection;
    }

    /// <summary>
    /// Check if a file needs re-indexing by comparing modified time.
    /// 更新日時を比較してファイルの再インデックスが必要か判定する。
    /// Returns the existing file ID if unchanged, or null if indexing is needed.
    /// 変更なしなら既存ファイルIDを返し、インデックスが必要ならnullを返す。
    /// </summary>
    public long? GetUnchangedFileId(string relativePath, DateTime modified)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, modified FROM files WHERE path = @path";
        cmd.Parameters.AddWithValue("@path", relativePath);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var existingModified = reader.GetDateTime(1);
            if (existingModified == modified)
                return reader.GetInt64(0);
        }
        return null;
    }

    /// <summary>
    /// Upsert a file record and return its ID.
    /// ファイルレコードをUPSERTしてIDを返す。
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
            using var transaction = _conn.BeginTransaction();

            for (int j = i; j < end; j++)
            {
                var chunk = chunks[j];
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO chunks (file_id, chunk_index, start_line, end_line, content)
                    VALUES (@fid, @idx, @start, @end, @content)";
                cmd.Parameters.AddWithValue("@fid", chunk.FileId);
                cmd.Parameters.AddWithValue("@idx", chunk.ChunkIndex);
                cmd.Parameters.AddWithValue("@start", chunk.StartLine);
                cmd.Parameters.AddWithValue("@end", chunk.EndLine);
                cmd.Parameters.AddWithValue("@content", chunk.Content);
                cmd.ExecuteNonQuery();

                // Populate FTS index / FTSインデックスに追加
                using var ftsCmd = _conn.CreateCommand();
                ftsCmd.CommandText = @"
                    INSERT INTO fts_chunks (rowid, content)
                    VALUES (last_insert_rowid(), @content)";
                ftsCmd.Parameters.AddWithValue("@content", chunk.Content);
                ftsCmd.ExecuteNonQuery();
            }

            transaction.Commit();
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
            using var transaction = _conn.BeginTransaction();

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

            transaction.Commit();
        }
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

        int removed = 0;
        foreach (var (id, relativePath) in dbPaths)
        {
            var absolutePath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
            {
                DeleteFileData(id);
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "DELETE FROM files WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                removed++;
            }
        }

        return removed;
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

    private long ExecuteScalar(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }
}
