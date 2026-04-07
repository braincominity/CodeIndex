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

    /// <summary>
    /// Split file content into chunks.
    /// ファイル内容をチャンクに分割する。
    /// </summary>
    /// <param name="fileId">The file ID in the database / データベース上のファイルID</param>
    /// <param name="content">Full file content / ファイル全体の内容</param>
    /// <returns>List of chunk records / チャンクレコードのリスト</returns>
    public static List<ChunkRecord> Split(long fileId, string content)
    {
        // Normalize line endings to LF before splitting / 分割前に改行をLFに正規化
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");
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
