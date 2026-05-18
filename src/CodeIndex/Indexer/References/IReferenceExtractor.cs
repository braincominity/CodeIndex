using CodeIndex.Models;

namespace CodeIndex.Indexer;

/// <summary>
/// Extracts indexed references for one normalized language key.
/// 正規化済み言語キー 1 つに対してインデックス用参照を抽出する。
/// </summary>
public interface IReferenceExtractor
{
    /// <summary>Normalized language key handled by this extractor / この抽出器が扱う正規化済み言語キー</summary>
    string Language { get; }

    /// <summary>
    /// Extract references from one source file.
    /// 1 つのソースファイルから参照を抽出する。
    /// </summary>
    List<ReferenceRecord> Extract(ReferenceExtractionContext request);
}
