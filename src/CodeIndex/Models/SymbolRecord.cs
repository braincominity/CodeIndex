namespace CodeIndex.Models;

/// <summary>
/// Represents a symbol (function, class, import) extracted from source code.
/// ソースコードから抽出されたシンボル（関数、クラス、インポート）を表す。
/// </summary>
public class SymbolRecord
{
    public long Id { get; set; }
    public long FileId { get; set; }

    /// <summary>Symbol kind: 'function', 'class', or 'import' / シンボル種別</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Symbol name / シンボル名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Line number (1-based) / 行番号（1始まり）</summary>
    public int Line { get; set; }
}
