using CodeIndex.Models;

namespace CodeIndex.Indexer;

/// <summary>
/// Splits file content into overlapping chunks for indexing.
/// ファイル内容を重複を持つチャンクに分割してインデックス用にする。
/// </summary>
public static class ChunkSplitter
{
    // Lines per chunk / 1チャンクあたりの行数
    private const int ChunkSize = 80;

    // Overlap with previous chunk / 前チャンクとの重複行数
    private const int Overlap = 10;

    // Per-line byte cap (in chars). Lines longer than this trigger the
    // oversize-line skip path: chunks/symbols/references for that file
    // are skipped and ValidateContent emits a `line_too_long` FileIssue
    // so downstream regex-based extraction cannot stall on minified or
    // base64-encoded payloads packed into a single physical line. Closes #1542.
    // 行ごとのバイト上限 (char 数)。これを超える行は oversize-line スキップ
    // 経路に入り、当該ファイルの chunks / symbols / references をスキップし、
    // ValidateContent から `line_too_long` FileIssue を発行する。これにより
    // 1 行に詰め込まれた minified / base64 ペイロードに対して下流の正規表現
    // 抽出が停止しないようにする。Closes #1542.
    public const int MaxLineLength = 64 * 1024;

    /// <summary>
    /// Returns true when <paramref name="content"/> contains any single line
    /// whose length exceeds <see cref="MaxLineLength"/>. Used by ChunkSplitter,
    /// SymbolExtractor, ReferenceExtractor, and ValidateContent to share a
    /// single cap so the indexer never feeds an unbounded line into regex-based
    /// extraction. Assumes `\n` is the only line separator (callers normalize
    /// CRLF first). Closes #1542.
    /// </summary>
    public static bool HasOversizeLine(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return false;
        int lineLen = 0;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                lineLen = 0;
                continue;
            }
            lineLen++;
            if (lineLen > MaxLineLength)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Split file content into chunks.
    /// ファイル内容をチャンクに分割する。
    /// </summary>
    /// <param name="fileId">The file ID in the database / データベース上のファイルID</param>
    /// <param name="content">Full file content / ファイル全体の内容</param>
    /// <returns>List of chunk records / チャンクレコードのリスト</returns>
    public static List<ChunkRecord> Split(long fileId, string content)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        // Defensive CRLF normalization — FileIndexer.BuildRecord already normalizes,
        // but this method is public and may be called directly with raw content.
        // 防御的CRLF正規化 — BuildRecordで正規化済みだが、直接呼び出し時の安全策。
        if (content.Contains('\r'))
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");
        // Defensive line-leading UTF-8 BOM strip — BuildRecord already strips the
        // leading BOM and any BOM that follows `\n`, but this method is public and
        // may be called directly. Mid-line U+FEFF (e.g. inside a string literal)
        // is preserved. Closes #183.
        // 防御的な行頭 UTF-8 BOM 剥離 — BuildRecord で先頭 BOM と `\n` 直後の BOM を
        // 既に剥がしているが、本メソッドはpublicで直接呼ばれうる。行頭以外の
        // U+FEFF (文字列リテラル内等) はそのまま残す。Closes #183.
        content = FileIndexer.StripLineLeadingBom(content);
        // Re-check for empty after BOM/CRLF strip so BOM-only input yields no chunks,
        // matching the no-chunks contract for empty files.
        // BOM/CRLF剥離後に再度空判定し、BOMのみの入力が空ファイルと同じく0チャンクになるようにする。
        if (content.Length == 0)
            return [];
        // Skip oversize-line files (e.g. 1 MB minified `.min.js`, base64 blobs):
        // returning no chunks prevents a single multi-MB Content column from
        // being persisted, and parallel guards in SymbolExtractor / ReferenceExtractor
        // keep the regex-based extractors from stalling on the same input.
        // ValidateContent emits a `line_too_long` FileIssue so the skip is
        // observable through the existing issues channel. Closes #1542.
        // oversize-line ファイル (例: 1 MB minified .min.js、base64 ペイロード) を
        // スキップする。チャンクを返さないことで複数 MB の Content カラムが
        // 永続化されるのを防ぎ、SymbolExtractor / ReferenceExtractor 側の同等
        // ガードと合わせて、正規表現抽出が同じ入力で停止しないようにする。
        // スキップは ValidateContent からの `line_too_long` FileIssue として
        // 既存の issues 経路で観測できる。Closes #1542.
        if (HasOversizeLine(content))
            return [];
        // Remove trailing newline to avoid phantom empty line / 末尾改行による空行を除去
        var lines = content.EndsWith('\n')
            ? content[..^1].Split('\n')
            : content.Split('\n');
        var chunks = new List<ChunkRecord>();
        int step = ChunkSize - Overlap;
        int chunkIndex = 0;

        for (int start = 0; start < lines.Length; start += step)
        {
            int end = Math.Min(start + ChunkSize, lines.Length);
            var chunkLines = lines[start..end];
            var chunkContent = string.Join('\n', chunkLines);

            chunks.Add(new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = chunkIndex,
                StartLine = start + 1,       // 1-based / 1始まり
                EndLine = end,               // 1-based inclusive / 1始まり（含む）
                Content = chunkContent,
            });

            chunkIndex++;

            // Stop if we've reached the end / 末尾に到達したら終了
            if (end >= lines.Length)
                break;
        }

        return chunks;
    }
}
