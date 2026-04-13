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

        using var reader = cmd.ExecuteTrackedReader();
        if (reader.TrackedRead())
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

        using var cmd3 = _conn.CreateCommand();
        cmd3.CommandText = "DELETE FROM symbol_references WHERE file_id = @fid";
        cmd3.Parameters.AddWithValue("@fid", fileId);
        cmd3.ExecuteNonQuery();
    }

    /// <summary>
    /// Insert chunks in batches (FTS index is populated automatically by triggers).
    /// Reuses a prepared statement per batch to avoid per-row command overhead.
    /// チャンクをバッチ挿入する（FTSインデックスはトリガーにより自動で反映される）。
    /// バッチごとにプリペアドステートメントを再利用し、行ごとのコマンド生成コストを回避する。
    /// </summary>
    public void InsertChunks(IReadOnlyList<ChunkRecord> chunks)
    {
        if (chunks.Count == 0) return;

        for (int i = 0; i < chunks.Count; i += BatchSize)
        {
            int end = Math.Min(i + BatchSize, chunks.Count);
            // Only create a batch transaction when not already inside an outer transaction
            // 外部トランザクション内でない場合のみバッチトランザクションを作成
            using var transaction = !IsInTransaction() ? BeginTransaction() : null;

            // Prepare after transaction starts so the command inherits the connection's transaction state
            // トランザクション開始後に準備し、接続のトランザクション状態を引き継ぐ
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
    /// Reuses a prepared statement per batch to avoid per-row command overhead.
    /// シンボルをバッチ挿入する。
    /// バッチごとにプリペアドステートメントを再利用し、行ごとのコマンド生成コストを回避する。
    /// </summary>
    public void InsertSymbols(IReadOnlyList<SymbolRecord> symbols)
    {
        if (symbols.Count == 0) return;

        for (int i = 0; i < symbols.Count; i += BatchSize)
        {
            int end = Math.Min(i + BatchSize, symbols.Count);
            // Only create a batch transaction when not already inside an outer transaction
            // 外部トランザクション内でない場合のみバッチトランザクションを作成
            using var transaction = !IsInTransaction() ? BeginTransaction() : null;

            // Prepare after transaction starts so the command inherits the connection's transaction state
            // トランザクション開始後に準備し、接続のトランザクション状態を引き継ぐ
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO symbols (
                    file_id, kind, name, line, start_line, end_line,
                    body_start_line, body_end_line, signature,
                    container_kind, container_name, visibility, return_type,
                    name_folded
                )
                VALUES (
                    @fid, @kind, @name, @line, @startLine, @endLine,
                    @bodyStartLine, @bodyEndLine, @signature,
                    @containerKind, @containerName, @visibility, @returnType,
                    @nameFolded
                )";
            var pFid = cmd.Parameters.Add("@fid", SqliteType.Integer);
            var pKind = cmd.Parameters.Add("@kind", SqliteType.Text);
            var pName = cmd.Parameters.Add("@name", SqliteType.Text);
            var pLine = cmd.Parameters.Add("@line", SqliteType.Integer);
            var pStartLine = cmd.Parameters.Add("@startLine", SqliteType.Integer);
            var pEndLine = cmd.Parameters.Add("@endLine", SqliteType.Integer);
            var pBodyStartLine = cmd.Parameters.Add("@bodyStartLine", SqliteType.Integer);
            var pBodyEndLine = cmd.Parameters.Add("@bodyEndLine", SqliteType.Integer);
            var pSignature = cmd.Parameters.Add("@signature", SqliteType.Text);
            var pContainerKind = cmd.Parameters.Add("@containerKind", SqliteType.Text);
            var pContainerName = cmd.Parameters.Add("@containerName", SqliteType.Text);
            var pVisibility = cmd.Parameters.Add("@visibility", SqliteType.Text);
            var pReturnType = cmd.Parameters.Add("@returnType", SqliteType.Text);
            var pNameFolded = cmd.Parameters.Add("@nameFolded", SqliteType.Text);
            cmd.Prepare();

            for (int j = i; j < end; j++)
            {
                var symbol = symbols[j];
                var startLine = symbol.StartLine > 0 ? symbol.StartLine : symbol.Line;
                var endLine = symbol.EndLine > 0 ? symbol.EndLine : startLine;
                pFid.Value = symbol.FileId;
                pKind.Value = symbol.Kind;
                pName.Value = symbol.Name;
                pLine.Value = symbol.Line;
                pStartLine.Value = startLine;
                pEndLine.Value = endLine;
                pBodyStartLine.Value = (object?)symbol.BodyStartLine ?? DBNull.Value;
                pBodyEndLine.Value = (object?)symbol.BodyEndLine ?? DBNull.Value;
                pSignature.Value = (object?)symbol.Signature ?? DBNull.Value;
                pContainerKind.Value = (object?)symbol.ContainerKind ?? DBNull.Value;
                pContainerName.Value = (object?)symbol.ContainerName ?? DBNull.Value;
                pVisibility.Value = (object?)symbol.Visibility ?? DBNull.Value;
                pReturnType.Value = (object?)symbol.ReturnType ?? DBNull.Value;
                pNameFolded.Value = (object?)NameFold.Fold(symbol.Name) ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            transaction?.Commit();
        }
    }

    /// <summary>
    /// Insert indexed references in batches.
    /// インデックス済み参照をバッチ挿入する。
    /// </summary>
    public void InsertReferences(IReadOnlyList<ReferenceRecord> references)
    {
        if (references.Count == 0) return;

        for (int i = 0; i < references.Count; i += BatchSize)
        {
            int end = Math.Min(i + BatchSize, references.Count);
            using var transaction = !IsInTransaction() ? BeginTransaction() : null;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO symbol_references (
                    file_id, symbol_name, reference_kind, line, column_number,
                    context, container_kind, container_name,
                    symbol_name_folded, container_name_folded
                )
                VALUES (
                    @fid, @symbolName, @referenceKind, @line, @columnNumber,
                    @context, @containerKind, @containerName,
                    @symbolNameFolded, @containerNameFolded
                )";
            var pFid = cmd.Parameters.Add("@fid", SqliteType.Integer);
            var pSymbolName = cmd.Parameters.Add("@symbolName", SqliteType.Text);
            var pReferenceKind = cmd.Parameters.Add("@referenceKind", SqliteType.Text);
            var pLine = cmd.Parameters.Add("@line", SqliteType.Integer);
            var pColumnNumber = cmd.Parameters.Add("@columnNumber", SqliteType.Integer);
            var pContext = cmd.Parameters.Add("@context", SqliteType.Text);
            var pContainerKind = cmd.Parameters.Add("@containerKind", SqliteType.Text);
            var pContainerName = cmd.Parameters.Add("@containerName", SqliteType.Text);
            var pSymbolNameFolded = cmd.Parameters.Add("@symbolNameFolded", SqliteType.Text);
            var pContainerNameFolded = cmd.Parameters.Add("@containerNameFolded", SqliteType.Text);
            cmd.Prepare();

            for (int j = i; j < end; j++)
            {
                var reference = references[j];
                pFid.Value = reference.FileId;
                pSymbolName.Value = reference.SymbolName;
                pReferenceKind.Value = reference.ReferenceKind;
                pLine.Value = reference.Line;
                pColumnNumber.Value = reference.Column;
                pContext.Value = reference.Context;
                pContainerKind.Value = (object?)reference.ContainerKind ?? DBNull.Value;
                pContainerName.Value = (object?)reference.ContainerName ?? DBNull.Value;
                pSymbolNameFolded.Value = (object?)NameFold.Fold(reference.SymbolName) ?? DBNull.Value;
                pContainerNameFolded.Value = (object?)NameFold.Fold(reference.ContainerName) ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            transaction?.Commit();
        }
    }

    /// <summary>
    /// Insert file validation issues.
    /// ファイル検証問題を挿入する。
    /// </summary>
    public void InsertIssues(long fileId, IReadOnlyList<CodeIndex.Models.FileIssue> issues)
    {
        // Always delete existing issues — if the file is now clean, old issues must be removed
        // 常に既存問題を削除 — ファイルが修正済みなら古い問題を残さない
        using var delCmd = _conn.CreateCommand();
        delCmd.CommandText = "DELETE FROM file_issues WHERE file_id = @fid";
        delCmd.Parameters.AddWithValue("@fid", fileId);
        delCmd.ExecuteNonQuery();

        if (issues.Count == 0) return;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO file_issues (file_id, kind, line, message) VALUES (@fid, @kind, @line, @message)";
        var pFid = cmd.Parameters.Add("@fid", SqliteType.Integer);
        var pKind = cmd.Parameters.Add("@kind", SqliteType.Text);
        var pLine = cmd.Parameters.Add("@line", SqliteType.Integer);
        var pMessage = cmd.Parameters.Add("@message", SqliteType.Text);

        foreach (var issue in issues)
        {
            pFid.Value = fileId;
            pKind.Value = issue.Kind;
            pLine.Value = issue.Line;
            pMessage.Value = issue.Message;
            cmd.ExecuteNonQuery();
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
            using var reader = cmd.ExecuteTrackedReader();
            while (reader.TrackedRead())
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
    /// Delete symbol_references for files whose language is no longer graph-supported.
    /// Prevents stale call edges from surviving after a language is removed from graph support.
    /// グラフ非対応になった言語のファイルから symbol_references を削除する。
    /// グラフ対応が外された言語の古いコールエッジが残存するのを防止する。
    /// </summary>
    /// <param name="supportedLanguages">Currently supported languages / 現在対応している言語</param>
    /// <returns>Number of stale reference rows deleted / 削除された古い参照行数</returns>
    public int PurgeUnsupportedReferences(IReadOnlyCollection<string> supportedLanguages)
    {
        if (supportedLanguages.Count == 0)
            return 0;

        using var cmd = _conn.CreateCommand();
        // Build an IN clause for supported languages / 対応言語の IN 句を構築
        var inParams = new List<string>();
        for (int i = 0; i < supportedLanguages.Count; i++)
        {
            var paramName = $"@lang{i}";
            inParams.Add(paramName);
            cmd.Parameters.AddWithValue(paramName, supportedLanguages.ElementAt(i));
        }

        cmd.CommandText = $@"
            DELETE FROM symbol_references
            WHERE file_id IN (
                SELECT f.id FROM files f
                WHERE f.lang IS NOT NULL
                  AND f.lang NOT IN ({string.Join(", ", inParams)})
            )";
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Get total counts for the summary output.
    /// サマリー出力用の合計件数を取得する。
    /// </summary>
    public (long files, long chunks, long symbols, long references) GetCounts()
    {
        long files = ExecuteScalar("SELECT COUNT(*) FROM files");
        long chunks = ExecuteScalar("SELECT COUNT(*) FROM chunks");
        long symbols = ExecuteScalar("SELECT COUNT(*) FROM symbols");
        long references = ExecuteScalar("SELECT COUNT(*) FROM symbol_references");
        return (files, chunks, symbols, references);
    }

    /// <summary>
    /// Optimize FTS5 index to merge internal b-tree segments for better query performance.
    /// FTS5インデックスを最適化して内部b-treeセグメントを統合し、クエリ性能を改善する。
    /// </summary>
    public void OptimizeFts()
    {
        Execute("INSERT INTO fts_chunks(fts_chunks) VALUES('optimize')");
    }

    // End-of-successful-index trust markers. The ready bits live in PRAGMA user_version so
    // that a reader can tell which subset of the index has been fully populated:
    //   bit 0 (GraphReadyFlag)  — symbol_references fully backfilled
    //   bit 1 (IssuesReadyFlag) — file_issues produced by ValidateContent
    //   bit 2 (FoldReadyFlag)   — name_folded columns populated for Unicode --exact (#86)
    // CLI and MCP full-scan indexing set graph + fold; CLI additionally sets issues (MCP
    // now persists file_issues too after bdbb2bd, so both can stamp it). The index runner
    // ClearReadyFlags() first so partial / aborted runs demote trust until a successful
    // end-of-run commit. Fold is only stamped after a full scan because a partial update
    // leaves legacy rows without folded values.
    // CLI / MCP 共に full-scan で graph + fold を立てる。fold は部分更新では立てない。
    public void MarkGraphReady()    => SetReadyBit(DbContext.GraphReadyFlag);
    public void MarkIssuesReady()   => SetReadyBit(DbContext.IssuesReadyFlag);

    /// <summary>
    /// Stamp FoldReadyFlag AND write the current <see cref="NameFold.Version"/> plus the
    /// runtime-sensitive <see cref="NameFold.Fingerprint"/> into `codeindex_meta`.
    /// Readers require the bit, a version match, and a fingerprint match before trusting
    /// folded columns, so both intentional fold changes and runtime ICU / invariant-casing
    /// drift degrade safely to NOCASE until `--rebuild`. Issue #97.
    /// FoldReady bit + fold_key_version + fold_key_fingerprint を書く。runtime drift を含む
    /// silent mismatch を防ぎ、ズレた場合は `--rebuild` まで NOCASE fallback に降格する。
    /// </summary>
    public void MarkFoldReady()
    {
        SetReadyBit(DbContext.FoldReadyFlag);
        SetMeta("fold_key_version", NameFold.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMeta("fold_key_fingerprint", NameFold.Fingerprint());
    }

    /// <summary>
    /// Upsert a metadata key/value into `codeindex_meta`.
    /// codeindex_meta への key/value の upsert。
    /// </summary>
    public void SetMeta(string key, string? value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO codeindex_meta (key, value) VALUES (@key, @value)
                            ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", (object?)value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    public void ClearReadyFlags()   => Execute("PRAGMA user_version = 0");

    /// <summary>
    /// True only when every existing row in symbols / symbol_references has a populated folded
    /// value for each source name that is itself non-NULL. Callers use this before stamping
    /// `FoldReadyFlag` on a full scan because the default incremental path skips unchanged files
    /// — their pre-#86 rows still carry NULL folded columns, so a naive stamp would flip readers
    /// onto the folded equality path and silently miss those legacy rows. Codex #86 review.
    /// full scan 成功時でも、incremental で skip された legacy 行が NULL のまま残っていれば
    /// fold-ready にしてはならない。stamp 前にこの実検証を通す。
    /// </summary>
    public bool AllFoldedColumnsBackfilled()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                (SELECT COUNT(*) FROM symbols WHERE name_folded IS NULL)
              + (SELECT COUNT(*) FROM symbol_references WHERE symbol_name IS NOT NULL AND symbol_name_folded IS NULL)
              + (SELECT COUNT(*) FROM symbol_references WHERE container_name IS NOT NULL AND container_name_folded IS NULL)";
        var raw = cmd.ExecuteScalar();
        long missing = raw is long l ? l : (raw is int i ? i : 0);
        return missing == 0;
    }

    private void SetReadyBit(int flag)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        var raw = cmd.ExecuteScalar();
        int current = raw is long l ? (int)l : (raw is int i ? i : 0);
        Execute($"PRAGMA user_version = {current | flag}");
    }

    private bool IsInTransaction() => _transactionDepth > 0;

    private long ExecuteScalar(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }
}
