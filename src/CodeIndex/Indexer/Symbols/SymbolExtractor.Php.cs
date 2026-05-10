using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

public static partial class SymbolExtractor
{
    private static readonly Regex PhpPropertyDeclarationHeadRegex = new(
        @"^\s*(?:(?<visibility>public|private|protected|var)\s+)(?:(?:static|readonly)\s+)*(?:(?<returnType>\??[A-Za-z_\\][\w\\]*(?:\s*[|&]\s*\??[A-Za-z_\\][\w\\]*)*)\s+)?\$(?<name>\w+)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    private static void ExtractPhpAdditionalPropertySymbols(long fileId, string[] lines, List<SymbolRecord> symbols)
    {
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var match = PhpPropertyDeclarationHeadRegex.Match(line);
            if (!match.Success)
                continue;

            var lineNumber = lineIndex + 1;
            var firstNameEnd = match.Groups["name"].Index + match.Groups["name"].Length;
            foreach (var candidate in EnumeratePhpAdditionalPropertyNames(line, firstNameEnd))
            {
                AddSymbolRecord(
                    symbols,
                    cssSeenSymbols: null,
                    lineNumber,
                    new SymbolRecord
                    {
                        FileId = fileId,
                        Kind = "property",
                        Name = candidate.Name,
                        Line = lineNumber,
                        StartLine = lineNumber,
                        StartColumn = candidate.Column,
                        EndLine = lineNumber,
                        Signature = line.Trim(),
                        Visibility = match.Groups["visibility"].Value,
                        ReturnType = match.Groups["returnType"].Success
                            ? match.Groups["returnType"].Value
                            : null,
                    },
                    line);
            }
        }
    }

    private static IEnumerable<(string Name, int Column)> EnumeratePhpAdditionalPropertyNames(string line, int startColumn)
    {
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var quote = '\0';

        for (var index = Math.Max(0, startColumn); index < line.Length; index++)
        {
            var ch = line[index];
            if (quote != '\0')
            {
                if (ch == '\\' && index + 1 < line.Length)
                {
                    index++;
                    continue;
                }

                if (ch == quote)
                    quote = '\0';
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch == '/' && index + 1 < line.Length && line[index + 1] == '/')
                yield break;
            if (ch == '#')
                yield break;
            if (ch == ';' && parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                yield break;

            if (ch == '(')
                parenDepth++;
            else if (ch == ')' && parenDepth > 0)
                parenDepth--;
            else if (ch == '[')
                bracketDepth++;
            else if (ch == ']' && bracketDepth > 0)
                bracketDepth--;
            else if (ch == '{')
                braceDepth++;
            else if (ch == '}' && braceDepth > 0)
                braceDepth--;

            if (ch != ',' || parenDepth != 0 || bracketDepth != 0 || braceDepth != 0)
                continue;

            var nameStart = index + 1;
            while (nameStart < line.Length && char.IsWhiteSpace(line[nameStart]))
                nameStart++;
            if (nameStart >= line.Length || line[nameStart] != '$')
                continue;

            var nameColumn = nameStart + 1;
            var nameEnd = nameColumn;
            while (nameEnd < line.Length && IsPhpIdentifierPart(line[nameEnd]))
                nameEnd++;

            if (nameEnd > nameColumn)
                yield return (line[nameColumn..nameEnd], nameColumn);
        }
    }

    private static bool IsPhpIdentifierPart(char ch)
        => ch == '_' || char.IsLetterOrDigit(ch);

}
