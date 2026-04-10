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

    /// <summary>Definition start line (1-based) / 定義開始行（1始まり）</summary>
    public int StartLine { get; set; }

    /// <summary>Definition end line (1-based) / 定義終了行（1始まり）</summary>
    public int EndLine { get; set; }

    /// <summary>Body start line when known / 本体開始行（判定できる場合）</summary>
    public int? BodyStartLine { get; set; }

    /// <summary>Body end line when known / 本体終了行（判定できる場合）</summary>
    public int? BodyEndLine { get; set; }

    /// <summary>Original declaration or signature line / 元の宣言・シグネチャ行</summary>
    public string? Signature { get; set; }

    /// <summary>Enclosing symbol kind when known / 親シンボル種別</summary>
    public string? ContainerKind { get; set; }

    /// <summary>Enclosing symbol name when known / 親シンボル名</summary>
    public string? ContainerName { get; set; }

    /// <summary>Visibility or export-like modifier when known / 可視性または公開修飾子</summary>
    public string? Visibility { get; set; }

    /// <summary>Return type when known / 戻り値型</summary>
    public string? ReturnType { get; set; }
}
