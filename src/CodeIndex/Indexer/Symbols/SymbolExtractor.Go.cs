using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

public static partial class SymbolExtractor
{
    private static bool TryHandleGoBlockLine(
        long fileId,
        string line,
        int lineIndex,
        List<SymbolRecord> symbols,
        ref bool inImportBlock)
    {
        var trimmed = line.TrimStart();

        if (inImportBlock)
        {
            if (trimmed.Length == 0
                || trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                return true;
            }

            if (trimmed.StartsWith(")", StringComparison.Ordinal))
            {
                inImportBlock = false;
                return true;
            }

            var closingParenIndex = trimmed.IndexOf(')');
            if (closingParenIndex >= 0)
            {
                var blockImportText = trimmed[..closingParenIndex].TrimEnd();
                if (blockImportText.Length > 0)
                    TryAddGoImportSymbol(fileId, line, lineIndex, symbols, blockImportText);

                inImportBlock = false;
                return true;
            }

            return TryAddGoImportSymbol(fileId, line, lineIndex, symbols, trimmed);
        }

        if (trimmed.StartsWith("import", StringComparison.Ordinal))
        {
            var afterImport = trimmed["import".Length..].TrimStart();
            if (afterImport.StartsWith("(", StringComparison.Ordinal))
            {
                var blockRemainder = afterImport[1..].TrimStart();
                if (blockRemainder.Length > 0)
                {
                    var closingParenIndex = blockRemainder.IndexOf(')');
                    if (closingParenIndex >= 0)
                    {
                        var blockImportText = blockRemainder[..closingParenIndex].TrimEnd();
                        if (blockImportText.Length > 0)
                            TryAddGoImportSymbol(fileId, line, lineIndex, symbols, blockImportText);

                        inImportBlock = false;
                        return true;
                    }

                    TryAddGoImportSymbol(fileId, line, lineIndex, symbols, blockRemainder);
                }

                inImportBlock = true;
                return true;
            }

            return TryAddGoImportSymbol(fileId, line, lineIndex, symbols, afterImport);
        }

        return false;
    }

    private static bool TryAddGoTypeSymbol(
        long fileId,
        string rawLine,
        int lineIndex,
        List<SymbolRecord> symbols,
        string typeText,
        ref int goTypeBodyDepth)
    {
        var normalizedTypeText = typeText.StartsWith("type", StringComparison.Ordinal)
            ? typeText["type".Length..].TrimStart()
            : typeText;
        var match = GoTypeBlockSpecRegex.Match(normalizedTypeText);
        if (!match.Success)
            return true;

        var name = match.Groups["name"].Value.Trim();
        var kind = Regex.IsMatch(normalizedTypeText, @"\bstruct\b")
            ? "struct"
            : Regex.IsMatch(normalizedTypeText, @"\binterface\b")
                ? "interface"
                : "class";
        if (HasGoSymbol(symbols, fileId, lineIndex + 1, kind, name))
            return true;
        var startColumn = rawLine.IndexOf(name, StringComparison.Ordinal);
        if (startColumn < 0)
            startColumn = rawLine.Length - rawLine.TrimStart().Length;

        AddSymbolRecord(
            symbols,
            cssSeenSymbols: null,
            lineIndex + 1,
            new SymbolRecord
            {
                FileId = fileId,
                Kind = kind,
                Name = name,
                Line = lineIndex + 1,
                StartLine = lineIndex + 1,
                StartColumn = startColumn,
                EndLine = lineIndex + 1,
                Signature = name,
            },
            rawLine);

        if (kind is "struct" or "interface")
            goTypeBodyDepth = CountGoBraceDelta(typeText);

        return true;
    }

    private static bool TryAddGoValueSymbol(
        long fileId,
        string rawLine,
        int lineIndex,
        List<SymbolRecord> symbols,
        string valueText)
    {
        var match = GoValueBlockSpecRegex.Match(valueText);
        if (!match.Success)
            return true;

        foreach (var name in match.Groups["names"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (HasGoSymbol(symbols, fileId, lineIndex + 1, "property", name))
                continue;

            var startColumn = rawLine.IndexOf(name, StringComparison.Ordinal);
            if (startColumn < 0)
                startColumn = rawLine.Length - rawLine.TrimStart().Length;

            AddSymbolRecord(
                symbols,
                cssSeenSymbols: null,
                lineIndex + 1,
                new SymbolRecord
                {
                    FileId = fileId,
                    Kind = "property",
                    Name = name,
                    Line = lineIndex + 1,
                    StartLine = lineIndex + 1,
                    StartColumn = startColumn,
                    EndLine = lineIndex + 1,
                    Signature = name,
                },
                rawLine);
        }

        return true;
    }

    private static int CountGoBraceDelta(string text)
    {
        var delta = 0;
        foreach (var ch in text)
        {
            if (ch == '{')
                delta++;
            else if (ch == '}')
                delta--;
        }

        return delta;
    }

    private static bool TryAddGoImportSymbol(
        long fileId,
        string rawLine,
        int lineIndex,
        List<SymbolRecord> symbols,
        string importText)
    {
        var match = GoImportSpecRegex.Match(importText);
        if (!match.Success)
            return true;

        var name = match.Groups["name"].Value.Trim();
        var startColumn = rawLine.IndexOf(name, StringComparison.Ordinal);
        if (startColumn < 0)
            startColumn = rawLine.IndexOf(importText, StringComparison.Ordinal);
        if (startColumn < 0)
            startColumn = rawLine.Length - rawLine.TrimStart().Length;

        AddSymbolRecord(
            symbols,
            cssSeenSymbols: null,
            lineIndex + 1,
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "import",
                Name = name,
                Line = lineIndex + 1,
                StartLine = lineIndex + 1,
                StartColumn = startColumn,
                EndLine = lineIndex + 1,
                Signature = name,
            },
            rawLine);
        return true;
    }

    private static void ExtractGoInterfaceMethods(long fileId, string[] lines, List<SymbolRecord> symbols)
    {
        var awaitingInterfaceBody = false;
        var inInterfaceBody = false;
        var interfaceBodyDepth = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            var trimmed = rawLine.TrimStart();

            if (!inInterfaceBody)
            {
                if (!awaitingInterfaceBody)
                {
                    if (!GoInterfaceHeaderRegex.IsMatch(trimmed))
                        continue;
                }
                else if (trimmed.Length == 0
                    || trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    continue;
                }

                var openBraceIndex = rawLine.IndexOf('{');
                if (openBraceIndex < 0)
                {
                    awaitingInterfaceBody = true;
                    continue;
                }

                inInterfaceBody = true;
                awaitingInterfaceBody = false;
                interfaceBodyDepth = CountGoBraceDelta(rawLine);

                var sameLineBody = rawLine[(openBraceIndex + 1)..].TrimStart();
                if (sameLineBody.Length > 0
                    && !sameLineBody.StartsWith("//", StringComparison.Ordinal)
                    && !sameLineBody.StartsWith("/*", StringComparison.Ordinal)
                    && !sameLineBody.StartsWith("}", StringComparison.Ordinal))
                {
                    TryAddGoInterfaceBodySymbols(fileId, rawLine, i, symbols, sameLineBody);
                }

                if (interfaceBodyDepth <= 0)
                {
                    inInterfaceBody = false;
                    interfaceBodyDepth = 0;
                }

                continue;
            }

            if (trimmed.Length == 0
                || trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                interfaceBodyDepth += CountGoBraceDelta(rawLine);
                if (interfaceBodyDepth <= 0)
                {
                    inInterfaceBody = false;
                    interfaceBodyDepth = 0;
                }

                continue;
            }

            var candidate = trimmed;
            var trailingBraceIndex = candidate.IndexOf('}');
            if (trailingBraceIndex >= 0)
                candidate = candidate[..trailingBraceIndex].TrimEnd();

            if (candidate.Length > 0 && !candidate.StartsWith("}", StringComparison.Ordinal))
                TryAddGoInterfaceBodySymbols(fileId, rawLine, i, symbols, candidate);

            interfaceBodyDepth += CountGoBraceDelta(rawLine);
            if (interfaceBodyDepth <= 0)
            {
                inInterfaceBody = false;
                interfaceBodyDepth = 0;
            }
        }
    }

    private static void TryAddGoInterfaceBodySymbols(
        long fileId,
        string rawLine,
        int lineIndex,
        List<SymbolRecord> symbols,
        string bodyText)
    {
        foreach (var segment in bodyText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = segment;
            var trailingBraceIndex = candidate.IndexOf('}');
            if (trailingBraceIndex >= 0)
                candidate = candidate[..trailingBraceIndex].TrimEnd();

            var lineCommentIndex = candidate.IndexOf("//", StringComparison.Ordinal);
            if (lineCommentIndex >= 0)
                candidate = candidate[..lineCommentIndex].TrimEnd();

            var blockCommentIndex = candidate.IndexOf("/*", StringComparison.Ordinal);
            if (blockCommentIndex >= 0)
                candidate = candidate[..blockCommentIndex].TrimEnd();

            if (candidate.Length == 0
                || candidate.StartsWith("//", StringComparison.Ordinal)
                || candidate.StartsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryAddGoInterfaceMethodSymbol(fileId, rawLine, lineIndex, symbols, candidate))
                continue;

            TryAddGoInterfaceEmbeddedTypeSymbol(fileId, rawLine, lineIndex, symbols, candidate);
        }
    }

    private static bool TryAddGoInterfaceMethodSymbol(
        long fileId,
        string rawLine,
        int lineIndex,
        List<SymbolRecord> symbols,
        string candidate)
    {
        var match = GoInterfaceMethodRegex.Match(candidate);
        if (!match.Success)
            return false;

        var name = match.Groups["name"].Value.Trim();
        if (HasGoSymbol(symbols, fileId, lineIndex + 1, "function", name))
            return true;

        var startColumn = rawLine.IndexOf(name, StringComparison.Ordinal);
        if (startColumn < 0)
            startColumn = rawLine.Length - rawLine.TrimStart().Length;

        AddSymbolRecord(
            symbols,
            cssSeenSymbols: null,
            lineIndex + 1,
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = name,
                Line = lineIndex + 1,
                StartLine = lineIndex + 1,
                StartColumn = startColumn,
                EndLine = lineIndex + 1,
                Signature = rawLine.Trim(),
            },
            rawLine);
        return true;
    }

    private static bool TryAddGoInterfaceEmbeddedTypeSymbol(
        long fileId,
        string rawLine,
        int lineIndex,
        List<SymbolRecord> symbols,
        string candidate)
    {
        var match = GoInterfaceEmbeddedTypeRegex.Match(candidate);
        if (!match.Success)
            return false;

        var name = match.Groups["name"].Value.Trim();
        if (name.Length == 0)
            return false;

        if (!name.Contains('.') && GoInterfaceEmbeddedTypeBlacklist.Contains(name))
        {
            return false;
        }

        if (HasGoSymbol(symbols, fileId, lineIndex + 1, "import", name))
            return true;

        var startColumn = rawLine.IndexOf(name, StringComparison.Ordinal);
        if (startColumn < 0)
            startColumn = rawLine.Length - rawLine.TrimStart().Length;

        AddSymbolRecord(
            symbols,
            cssSeenSymbols: null,
            lineIndex + 1,
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "import",
                Name = name,
                Line = lineIndex + 1,
                StartLine = lineIndex + 1,
                StartColumn = startColumn,
                EndLine = lineIndex + 1,
                Signature = rawLine.Trim(),
            },
            rawLine);
        return true;
    }

    private static bool HasGoSymbol(List<SymbolRecord> symbols, long fileId, int lineNumber, string kind, string name)
    {
        return symbols.Any(symbol =>
            symbol.FileId == fileId
            && symbol.Line == lineNumber
            && symbol.Kind == kind
            && symbol.Name == name);
    }

    private static void ExtractGoGroupedDeclarations(long fileId, string[] lines, List<SymbolRecord> symbols)
    {
        string? blockKind = null;
        ExtractGoInterfaceMethods(fileId, lines, symbols);
        var typeBodyDepth = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (typeBodyDepth > 0)
            {
                typeBodyDepth += CountGoBraceDelta(line);
                if (typeBodyDepth < 0)
                    typeBodyDepth = 0;
                continue;
            }

            if (trimmed.Length == 0
                || trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed.StartsWith(")", StringComparison.Ordinal))
            {
                blockKind = null;
                typeBodyDepth = 0;
                continue;
            }

            if (blockKind is not null)
            {
                switch (blockKind)
                {
                    case "type":
                        TryAddGoTypeSymbol(fileId, line, i, symbols, trimmed, ref typeBodyDepth);
                        break;
                    case "const":
                    case "var":
                        TryAddGoValueSymbol(fileId, line, i, symbols, trimmed);
                        break;
                }

                continue;
            }

            if (trimmed.StartsWith("type", StringComparison.Ordinal)
                && trimmed["type".Length..].TrimStart().StartsWith("(", StringComparison.Ordinal))
            {
                blockKind = "type";
                continue;
            }

            if (trimmed.StartsWith("const", StringComparison.Ordinal)
                && trimmed["const".Length..].TrimStart().StartsWith("(", StringComparison.Ordinal))
            {
                blockKind = "const";
                continue;
            }

            if (trimmed.StartsWith("var", StringComparison.Ordinal)
                && trimmed["var".Length..].TrimStart().StartsWith("(", StringComparison.Ordinal))
            {
                blockKind = "var";
                continue;
            }

            if (trimmed.StartsWith("type", StringComparison.Ordinal))
            {
                TryAddGoTypeSymbol(fileId, line, i, symbols, trimmed, ref typeBodyDepth);
                continue;
            }
        }
    }

}
