using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

/// <summary>
/// Extracts symbols (functions, classes, imports) using regex patterns.
/// 正規表現を使ってシンボル（関数、クラス、インポート）を抽出する。
/// </summary>
public static class SymbolExtractor
{
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
        if (lang == null) return [];

        var patterns = GetPatterns(lang);
        if (patterns.Count == 0) return [];

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

    /// <summary>
    /// Get regex patterns for the given language.
    /// 指定言語の正規表現パターンを取得する。
    /// </summary>
    private static List<(string kind, Regex regex)> GetPatterns(string lang)
    {
        return lang switch
        {
            "python" =>
            [
                ("function", new Regex(@"^\s*(?:async\s+)?def\s+(?<name>\w+)\s*\(", RegexOptions.Compiled)),
                ("class",    new Regex(@"^\s*class\s+(?<name>\w+)", RegexOptions.Compiled)),
            ],
            "javascript" or "typescript" =>
            [
                ("function", new Regex(@"^\s*(?:export\s+)?(?:async\s+)?function\s+(?<name>\w+)\s*\(", RegexOptions.Compiled)),
                ("class",    new Regex(@"^\s*(?:export\s+)?class\s+(?<name>\w+)", RegexOptions.Compiled)),
                ("import",   new Regex(@"^\s*import\s+(?<name>.+?)\s+from\s+", RegexOptions.Compiled)),
            ],
            "csharp" =>
            [
                ("class",    new Regex(@"^\s*(?:public|private|protected|internal)\s+(?:static\s+)?(?:partial\s+)?class\s+(?<name>\w+)", RegexOptions.Compiled)),
                ("function", new Regex(@"^\s*(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?(?:override\s+)?(?:\w+(?:<[^>]+>)?)\s+(?<name>\w+)\s*\(", RegexOptions.Compiled)),
            ],
            "go" =>
            [
                ("function", new Regex(@"^func\s+(?:\([^)]+\)\s+)?(?<name>\w+)\s*\(", RegexOptions.Compiled)),
            ],
            "rust" =>
            [
                ("function", new Regex(@"^\s*(?:pub\s+)?(?:async\s+)?fn\s+(?<name>\w+)", RegexOptions.Compiled)),
                ("class",    new Regex(@"^\s*(?:pub\s+)?struct\s+(?<name>\w+)", RegexOptions.Compiled)),
                ("class",    new Regex(@"^\s*impl\s+(?<name>\w+)", RegexOptions.Compiled)),
            ],
            "java" or "kotlin" =>
            [
                ("class",    new Regex(@"^\s*(?:public|private|protected)?\s*(?:abstract\s+)?class\s+(?<name>\w+)", RegexOptions.Compiled)),
                ("function", new Regex(@"^\s*(?:public|private|protected)?\s*(?:static\s+)?(?:fun|void|\w+)\s+(?<name>\w+)\s*\(", RegexOptions.Compiled)),
            ],
            _ => [],
        };
    }
}
