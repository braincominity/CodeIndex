namespace CodeIndex.Models;

/// <summary>
/// Represents a chunk of a file.
/// ファイルのチャンク（分割された部分）を表す。
/// </summary>
public class ChunkRecord
{
    public long Id { get; set; }
    public long FileId { get; set; }

    /// <summary>Zero-based chunk index / 0始まりのチャンクインデックス</summary>
    public int ChunkIndex { get; set; }

    /// <summary>Start line (1-based) / 開始行（1始まり）</summary>
    public int StartLine { get; set; }

    /// <summary>End line (1-based, inclusive) / 終了行（1始まり、含む）</summary>
    public int EndLine { get; set; }

    /// <summary>Chunk content / チャンクの内容</summary>
    public string Content { get; set; } = string.Empty;
}
