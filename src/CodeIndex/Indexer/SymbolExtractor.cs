using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

/// <summary>
/// Extracts symbols (functions, classes, imports) using regex patterns.
/// 正規表現を使ってシンボル（関数、クラス、インポート）を抽出する。
/// </summary>
public static class SymbolExtractor
{
    // Cached regex patterns per language (compiled once, reused across all files)
    // 言語ごとのコンパイル済み正規表現パターンをキャッシュ（全ファイルで再利用）
    private static readonly Dictionary<string, List<(string kind, Regex regex)>> PatternCache = new()
    {
        ["python"] =
        [
            ("function", new Regex(@"^\s*(?:async\s+)?def\s+(?<name>\w+)\s*\(", RegexOptions.Compiled)),
            ("class",    new Regex(@"^\s*class\s+(?<name>\w+)", RegexOptions.Compiled)),
        ],
        ["javascript"] =
        [
            ("function", new Regex(@"^\s*(?:export\s+)?(?:async\s+)?function\s+(?<name>\w+)\s*\(", RegexOptions.Compiled)),
            ("class",    new Regex(@"^\s*(?:export\s+)?class\s+(?<name>\w+)", RegexOptions.Compiled)),
            ("import",   new Regex(@"^\s*import\s+(?<name>.+?)\s+from\s+", RegexOptions.Compiled)),
        ],
        ["typescript"] =
        [
            ("function", new Regex(@"^\s*(?:export\s+)?(?:async\s+)?function\s+(?<name>\w+)\s*\(", RegexOptions.Compiled)),
            ("class",    new Regex(@"^\s*(?:export\s+)?class\s+(?<name>\w+)", RegexOptions.Compiled)),
            ("import",   new Regex(@"^\s*import\s+(?<name>.+?)\s+from\s+", RegexOptions.Compiled)),
        ],
        ["csharp"] =
        [
            ("class",    new Regex(@"^\s*(?:public|private|protected|internal)\s+(?:static\s+)?(?:partial\s+)?class\s+(?<name>\w+)", RegexOptions.Compiled)),
            ("class",    new Regex(@"^\s*(?:public|private|protected|internal)\s+(?:static\s+)?(?:partial\s+)?(?:interface|enum|record|struct)\s+(?<name>\w+)", RegexOptions.Compiled)),
            ("function", new Regex(@"^\s*(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?(?:override\s+)?(?:\w+(?:<[^>]+>)?)\s+(?<name>\w+)\s*\(", RegexOptions.Compiled)),
            // Constructor: access modifier followed by class-like name and open paren (no return type)
            // コンストラクタ: アクセス修飾子の後にクラス名風の名前と開き括弧（戻り値なし）
            ("function", new Regex(@"^\s*(?:public|private|protected|internal)\s+(?:static\s+)?(?<name>\w+)\s*\(", RegexOptions.Compiled)),
        ],
        ["go"] =
        [
            ("function", new Regex(@"^func\s+(?:\([^)]+\)\s+)?(?<name>\w+)\s*\(", RegexOptions.Compiled)),
        ],
        ["rust"] =
        [
            ("function", new Regex(@"^\s*(?:pub\s+)?(?:async\s+)?fn\s+(?<name>\w+)", RegexOptions.Compiled)),
            ("class",    new Regex(@"^\s*(?:pub\s+)?struct\s+(?<name>\w+)", RegexOptions.Compiled)),
            ("class",    new Regex(@"^\s*impl\s+(?<name>\w+)", RegexOptions.Compiled)),
        ],
        ["java"] =
        [
            ("class",    new Regex(@"^\s*(?:public|private|protected)?\s*(?:abstract\s+)?class\s+(?<name>\w+)", RegexOptions.Compiled)),
            ("function", new Regex(@"^\s*(?:public|private|protected)?\s*(?:static\s+)?(?:fun|void|\w+)\s+(?<name>\w+)\s*\(", RegexOptions.Compiled)),
        ],
        ["kotlin"] =
        [
            ("class",    new Regex(@"^\s*(?:public|private|protected)?\s*(?:abstract\s+)?class\s+(?<name>\w+)", RegexOptions.Compiled)),
            ("function", new Regex(@"^\s*(?:public|private|protected)?\s*(?:static\s+)?(?:fun|void|\w+)\s+(?<name>\w+)\s*\(", RegexOptions.Compiled)),
        ],
    };

    /// <summary>
    /// Extract symbols from the given source content.
    /// 指定されたソース内容からシンボルを抽出する。
    /// </summary>
    /// <param name="fileId">The file ID in the database / データベース上のファイルID</param>
    /// <param name="lang">Detected language / 検出された言語</param>
    /// <param name="content">Full file content / ファイル全体の内容</param>
    /// <returns>List of extracted symbols / 抽出されたシンボルのリスト</returns>
    public static List<SymbolRecord> Extract(long fileId, string? lang, string content)
    {
        if (lang == null || !PatternCache.TryGetValue(lang, out var patterns))
            return [];

        var symbols = new List<SymbolRecord>();
        var lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (var (kind, regex) in patterns)
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    var name = match.Groups["name"].Success
                        ? match.Groups["name"].Value
                        : match.Value.Trim();

                    symbols.Add(new SymbolRecord
                    {
                        FileId = fileId,
                        Kind = kind,
                        Name = name,
                        Line = i + 1, // 1-based / 1始まり
                    });
                }
            }
        }

        return symbols;
    }
}
