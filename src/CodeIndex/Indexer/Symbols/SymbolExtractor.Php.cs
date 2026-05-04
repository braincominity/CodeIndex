using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

public static partial class SymbolExtractor
{
    private static void ExtractPhpImportSymbols(List<SymbolRecord> symbols, string line, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var groupUseMatch = PhpGroupUseRegex.Match(line);
        if (groupUseMatch.Success)
        {
            var prefix = groupUseMatch.Groups["prefix"].Value;
            var signature = line.Trim();
            foreach (var rawItem in groupUseMatch.Groups["items"].Value.Split(','))
            {
                var item = rawItem.Trim();
                if (item.Length == 0)
                    continue;

                var importedName = item;
                var alias = string.Empty;
                var aliasIndex = item.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
                if (aliasIndex >= 0)
                {
                    importedName = item[..aliasIndex].Trim();
                    alias = item[(aliasIndex + 4)..].Trim();
                }

                var symbolName = alias.Length > 0 ? alias : prefix + importedName;
                if (symbolName.Length == 0)
                    continue;

                AddSymbolRecord(
                    symbols,
                    cssSeenSymbols: null,
                    lineNumber,
                    new SymbolRecord
                    {
                        Kind = "import",
                        Name = symbolName,
                        Line = lineNumber,
                        StartLine = lineNumber,
                        EndLine = lineNumber,
                        Signature = signature
                    });
            }

            return;
        }

        var useMatch = PhpUseRegex.Match(line);
        if (useMatch.Success)
        {
            var symbolName = useMatch.Groups["alias"].Success
                ? useMatch.Groups["alias"].Value.Trim()
                : useMatch.Groups["name"].Value.Trim();
            if (symbolName.Length > 0)
            {
                AddSymbolRecord(
                    symbols,
                    cssSeenSymbols: null,
                    lineNumber,
                    new SymbolRecord
                    {
                        Kind = "import",
                        Name = symbolName,
                        Line = lineNumber,
                        StartLine = lineNumber,
                        EndLine = lineNumber,
                        Signature = line.Trim()
                    });
            }

            return;
        }

        var requireMatch = PhpRequireIncludeRegex.Match(line);
        if (!requireMatch.Success)
            requireMatch = PhpPrefixedRequireIncludeRegex.Match(line);
        if (!requireMatch.Success)
            return;

        var importedPath = requireMatch.Groups["singleName"].Success
            ? requireMatch.Groups["singleName"].Value.Trim()
            : requireMatch.Groups["doubleName"].Value.Trim();
        if (importedPath.Length == 0)
            return;

        if (requireMatch.Groups["prefix"].Success)
            importedPath = importedPath.TrimStart('/', '\\');

        AddSymbolRecord(
            symbols,
            cssSeenSymbols: null,
            lineNumber,
            new SymbolRecord
            {
                Kind = "import",
                Name = importedPath,
                Line = lineNumber,
                StartLine = lineNumber,
                EndLine = lineNumber,
                Signature = line.Trim()
            });
    }

}
