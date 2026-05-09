using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

public static partial class SymbolExtractor
{
    private readonly record struct PythonImportSymbolEntry(string Name, int StartColumn);
    private readonly record struct PythonExportSymbolEntry(string Name, int LineIndex, int StartColumn);
    private static readonly Regex PythonDirectImportRegex = new(@"^import\s+(?<imports>.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonFromImportRegex = new(@"^from\s+(?<module>(?:\.+[\w.]*|[\w.]+))\s+import\s+(?<imports>.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PythonAllAssignmentRegex = new(@"^\s*__all__\s*(?:\+?=)\s*(?<values>.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool HasPythonPropertyDecorator(string[] lines, int defLineIndex)
    {
        for (var i = defLineIndex - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith('@'))
                return false;

            if (IsPythonPropertyDecorator(trimmed))
                return true;
        }

        return false;
    }

    private static bool IsPythonPropertyDecorator(string trimmedDecorator)
    {
        var decorator = trimmedDecorator[1..];
        var commentIndex = decorator.IndexOf('#');
        if (commentIndex >= 0)
            decorator = decorator[..commentIndex];
        var parenIndex = decorator.IndexOf('(');
        if (parenIndex >= 0)
            decorator = decorator[..parenIndex];

        decorator = decorator.Trim();
        return decorator is "property" or "cached_property" or "abstractproperty"
            || decorator.EndsWith(".cached_property", StringComparison.Ordinal)
            || decorator.EndsWith(".abstractproperty", StringComparison.Ordinal)
            || decorator.EndsWith(".setter", StringComparison.Ordinal)
            || decorator.EndsWith(".deleter", StringComparison.Ordinal);
    }

    private static List<PythonImportSymbolEntry>? TryExpandPythonImportSymbols(
        string[] lines,
        int lineIndex,
        int absoluteStartColumn,
        string? pythonModulePrefix)
    {
        var line = lines[lineIndex];
        if (absoluteStartColumn < 0 || absoluteStartColumn >= line.Length)
            return null;

        var statement = line[absoluteStartColumn..].Trim();
        if (statement.Length == 0)
            return null;

        var commentIndex = statement.IndexOf('#');
        if (commentIndex >= 0)
            statement = statement[..commentIndex].TrimEnd();

        if (statement.Length == 0)
            return null;

        var entries = new List<PythonImportSymbolEntry>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        var directImportMatch = PythonDirectImportRegex.Match(statement);
        if (directImportMatch.Success)
        {
            var directImportSpecs = directImportMatch.Groups["imports"].Value;
            AddPythonImportSpecEntries(
                line,
                absoluteStartColumn,
                modulePart: null,
                directImportSpecs,
                entries,
                seenNames,
                treatAsFromImport: false,
                pythonModulePrefix);
            return entries.Count > 0 ? entries : null;
        }

        var fromImportMatch = PythonFromImportRegex.Match(statement);
        if (!fromImportMatch.Success)
            return null;

        var modulePart = fromImportMatch.Groups["module"].Value;
        var fromImportSpecs = fromImportMatch.Groups["imports"].Value;
        AddPythonImportModuleEntry(line, absoluteStartColumn, modulePart, entries, seenNames, pythonModulePrefix);
        if (TryExpandPythonMultilineParenthesizedImportBlock(
                lines,
                lineIndex,
                absoluteStartColumn + fromImportMatch.Groups["imports"].Index,
                modulePart,
                fromImportSpecs,
                entries,
                seenNames,
                pythonModulePrefix))
        {
            return entries.Count > 0 ? entries : null;
        }

        AddPythonImportSpecEntries(
            line,
            absoluteStartColumn + fromImportMatch.Groups["imports"].Index,
            modulePart,
            fromImportSpecs,
            entries,
            seenNames,
            treatAsFromImport: true,
            pythonModulePrefix);
        return entries.Count > 0 ? entries : null;
    }

    private static void ExtractPythonAllExportSymbols(
        long fileId,
        string[] lines,
        List<SymbolRecord> symbols,
        string? pythonModulePrefix)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var exports = TryExpandPythonAllExportSymbols(lines, i);
            if (exports == null)
                continue;

            foreach (var export in exports)
            {
                AddSymbolRecord(
                    symbols,
                    cssSeenSymbols: null,
                    export.LineIndex + 1,
                    new SymbolRecord
                    {
                        FileId = fileId,
                        Kind = "import",
                        Name = export.Name,
                        Line = export.LineIndex + 1,
                        StartLine = export.LineIndex + 1,
                        StartColumn = export.StartColumn,
                        EndLine = export.LineIndex + 1,
                        Signature = lines[export.LineIndex].Trim(),
                    },
                    lines[export.LineIndex]);

                if (!string.IsNullOrEmpty(pythonModulePrefix)
                    && !string.Equals(export.Name, pythonModulePrefix, StringComparison.Ordinal)
                    && !export.Name.StartsWith(pythonModulePrefix + ".", StringComparison.Ordinal))
                {
                    AddSymbolRecord(
                        symbols,
                        cssSeenSymbols: null,
                        export.LineIndex + 1,
                        new SymbolRecord
                        {
                            FileId = fileId,
                            Kind = "import",
                            Name = $"{pythonModulePrefix}.{export.Name}",
                            Line = export.LineIndex + 1,
                            StartLine = export.LineIndex + 1,
                            StartColumn = export.StartColumn,
                            EndLine = export.LineIndex + 1,
                            Signature = lines[export.LineIndex].Trim(),
                        },
                        lines[export.LineIndex]);
                }
            }
        }
    }

    private static List<PythonExportSymbolEntry>? TryExpandPythonAllExportSymbols(string[] lines, int lineIndex)
    {
        var line = lines[lineIndex];
        var match = PythonAllAssignmentRegex.Match(line);
        if (!match.Success)
            return null;

        var valuesStartColumn = match.Groups["values"].Index;
        if (valuesStartColumn < 0 || valuesStartColumn >= line.Length)
            return null;

        var entries = new List<PythonExportSymbolEntry>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var currentLineIndex = lineIndex;
        var currentColumn = valuesStartColumn;
        var depth = 0;
        var inString = false;
        var quoteChar = '\0';
        var stringStartColumn = -1;

        while (currentLineIndex < lines.Length)
        {
            var currentLine = lines[currentLineIndex];
            if (currentColumn >= currentLine.Length)
            {
                if (depth <= 0 && !inString)
                    break;

                currentLineIndex++;
                currentColumn = 0;
                continue;
            }

            var ch = currentLine[currentColumn];
            if (inString)
            {
                if (ch == '\\' && currentColumn + 1 < currentLine.Length)
                {
                    currentColumn += 2;
                    continue;
                }

                if (ch == quoteChar)
                {
                    var name = currentLine[stringStartColumn..currentColumn].Trim();
                    if (name.Length > 0 && seenNames.Add(name))
                    {
                        entries.Add(new PythonExportSymbolEntry(name, currentLineIndex, stringStartColumn));
                    }

                    inString = false;
                    quoteChar = '\0';
                    stringStartColumn = -1;
                    currentColumn++;
                    continue;
                }

                currentColumn++;
                continue;
            }

            if (ch == '#')
                break;

            if (ch == '\'' || ch == '"')
            {
                inString = true;
                quoteChar = ch;
                stringStartColumn = currentColumn + 1;
                currentColumn++;
                continue;
            }

            if (ch is '[' or '(' or '{')
            {
                depth++;
                currentColumn++;
                continue;
            }

            if (ch is ']' or ')' or '}')
            {
                if (depth > 0)
                    depth--;
                currentColumn++;
                if (depth <= 0)
                    break;
                continue;
            }

            currentColumn++;
        }

        return entries.Count > 0 ? entries : null;
    }

    private static bool TryExpandPythonMultilineParenthesizedImportBlock(
        string[] lines,
        int startLineIndex,
        int importsStartColumn,
        string modulePart,
        string importSpecs,
        List<PythonImportSymbolEntry> entries,
        HashSet<string> seenNames,
        string? pythonModulePrefix)
    {
        if (!importSpecs.StartsWith('(') || importSpecs.EndsWith(')'))
            return false;

        var currentLineIndex = startLineIndex;
        var importLineIndent = FindFirstNonWhitespaceColumn(lines[startLineIndex]);
        var fragment = importSpecs[1..];

        while (currentLineIndex < lines.Length)
        {
            var currentLine = lines[currentLineIndex];
            var fragmentStartColumn = currentLineIndex == startLineIndex
                ? Math.Min(importsStartColumn + 1, currentLine.Length)
                : FindFirstNonWhitespaceColumn(currentLine);
            if (fragmentStartColumn >= currentLine.Length)
            {
                currentLineIndex++;
                fragment = string.Empty;
                if (currentLineIndex >= lines.Length)
                    return true;
                continue;
            }

            if (currentLineIndex != startLineIndex && fragmentStartColumn <= importLineIndent)
                return true;

            fragment = currentLineIndex == startLineIndex
                ? fragment
                : currentLine[fragmentStartColumn..];

            var commentIndex = fragment.IndexOf('#');
            if (commentIndex >= 0)
                fragment = fragment[..commentIndex];

            fragment = fragment.TrimEnd();
            if (fragment.Length > 0)
            {
                var closingParenIndex = fragment.IndexOf(')');
                if (closingParenIndex >= 0)
                {
                    fragment = fragment[..closingParenIndex].TrimEnd();
                    if (fragment.Length > 0)
                    {
                        AddPythonImportSpecEntries(
                            currentLine,
                            fragmentStartColumn,
                            modulePart,
                            fragment,
                            entries,
                            seenNames,
                            treatAsFromImport: true,
                            pythonModulePrefix);
                    }

                    return true;
                }

                AddPythonImportSpecEntries(
                    currentLine,
                    fragmentStartColumn,
                    modulePart,
                    fragment,
                    entries,
                    seenNames,
                    treatAsFromImport: true,
                    pythonModulePrefix);
            }

            if (currentLineIndex + 1 >= lines.Length)
                return true;

            currentLineIndex++;
        }

        return true;
    }

    private static void AddPythonImportSpecEntries(
        string line,
        int absoluteStartColumn,
        string? modulePart,
        string importedNames,
        List<PythonImportSymbolEntry> entries,
        HashSet<string> seenNames,
        bool treatAsFromImport,
        string? pythonModulePrefix)
    {
        importedNames = importedNames.Trim();
        if (importedNames.Length == 0)
            return;

        if (importedNames.StartsWith('(') && importedNames.EndsWith(')'))
            importedNames = importedNames[1..^1].Trim();

        var searchStartColumn = absoluteStartColumn;
        var normalizedModulePart = treatAsFromImport
            ? modulePart?.Trim().TrimStart('.').TrimEnd('.')
            : null;
        var relativeQualifiedModulePart = treatAsFromImport
            ? ResolvePythonRelativeFromImportModuleName(modulePart, pythonModulePrefix)
            : null;
        foreach (var rawSpec in importedNames.Split(','))
        {
            var spec = rawSpec.Trim();
            if (spec.Length == 0 || spec == "*")
                continue;

            var aliasIndex = spec.IndexOf(" as ", StringComparison.Ordinal);
            var importedName = aliasIndex >= 0
                ? spec[..aliasIndex].Trim()
                : spec;
            var localName = aliasIndex >= 0
                ? spec[(aliasIndex + " as ".Length)..].Trim()
                : importedName.Split('.')[0].Trim();

            if (importedName.Length > 0)
            {
                AddPythonImportEntry(line, absoluteStartColumn, importedName, entries, seenNames, ref searchStartColumn);
                if (importedName.Contains('.'))
                {
                    AddPythonImportDottedPrefixEntries(
                        line,
                        absoluteStartColumn,
                        importedName,
                        entries,
                        seenNames,
                        ref searchStartColumn);
                }

                if (!treatAsFromImport
                    && !string.IsNullOrEmpty(pythonModulePrefix)
                    && !importedName.Contains('.'))
                {
                    AddPythonImportEntry(
                        line,
                        absoluteStartColumn,
                        $"{pythonModulePrefix}.{importedName}",
                        entries,
                        seenNames,
                        ref searchStartColumn);
                }

                if (treatAsFromImport && !string.IsNullOrEmpty(normalizedModulePart))
                {
                    AddPythonImportEntry(
                        line,
                        absoluteStartColumn,
                        $"{normalizedModulePart}.{importedName}",
                        entries,
                        seenNames,
                        ref searchStartColumn);
                }

                if (!string.IsNullOrEmpty(relativeQualifiedModulePart))
                {
                    AddPythonImportEntry(
                        line,
                        absoluteStartColumn,
                        $"{relativeQualifiedModulePart}.{importedName}",
                        entries,
                        seenNames,
                        ref searchStartColumn);
                }
            }

            if (localName.Length > 0
                && (!string.Equals(localName, importedName, StringComparison.Ordinal) || treatAsFromImport))
            {
                AddPythonImportEntry(line, absoluteStartColumn, localName, entries, seenNames, ref searchStartColumn);
            }

            if (string.IsNullOrEmpty(pythonModulePrefix))
                continue;

            if (aliasIndex >= 0)
            {
                AddPythonImportEntry(
                    line,
                    absoluteStartColumn,
                    $"{pythonModulePrefix}.{localName}",
                    entries,
                    seenNames,
                    ref searchStartColumn);
            }
        }
    }

    private static string? ResolvePythonRelativeFromImportModuleName(string? modulePart, string? pythonModulePrefix)
    {
        if (string.IsNullOrEmpty(modulePart)
            || string.IsNullOrEmpty(pythonModulePrefix)
            || !modulePart.StartsWith(".", StringComparison.Ordinal))
        {
            return null;
        }

        var leadingDots = 0;
        while (leadingDots < modulePart.Length && modulePart[leadingDots] == '.')
            leadingDots++;

        var levelsToDrop = leadingDots - 1;
        var packageParts = pythonModulePrefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (levelsToDrop >= packageParts.Length)
            return null;

        var basePartCount = packageParts.Length - levelsToDrop;
        var resolved = string.Join('.', packageParts.Take(basePartCount));
        var suffix = modulePart[leadingDots..].Trim('.');
        return suffix.Length == 0
            ? resolved
            : $"{resolved}.{suffix}";
    }

    private static void AddPythonImportModuleEntry(
        string line,
        int absoluteStartColumn,
        string modulePart,
        List<PythonImportSymbolEntry> entries,
        HashSet<string> seenNames,
        string? pythonModulePrefix)
    {
        modulePart = modulePart.Trim();
        if (modulePart.Length == 0)
            return;

        var searchStartColumn = absoluteStartColumn;
        var normalizedModule = modulePart.TrimStart('.');
        if (normalizedModule.Length > 0)
        {
            AddPythonImportEntry(line, absoluteStartColumn, normalizedModule, entries, seenNames, ref searchStartColumn);
            if (normalizedModule.Contains('.'))
            {
                AddPythonImportDottedPrefixEntries(
                    line,
                    absoluteStartColumn,
                    normalizedModule,
                    entries,
                    seenNames,
                    ref searchStartColumn);
            }
        }

        var relativeQualifiedModule = ResolvePythonRelativeFromImportModuleName(modulePart, pythonModulePrefix);
        if (!string.IsNullOrEmpty(relativeQualifiedModule))
        {
            AddPythonImportEntry(
                line,
                absoluteStartColumn,
                relativeQualifiedModule,
                entries,
                seenNames,
                ref searchStartColumn);
        }
    }

    private static void AddPythonImportEntry(
        string line,
        int absoluteStartColumn,
        string symbolName,
        List<PythonImportSymbolEntry> entries,
        HashSet<string> seenNames,
        ref int searchStartColumn)
    {
        symbolName = symbolName.Trim();
        if (symbolName.Length == 0 || !seenNames.Add(symbolName))
            return;

        var startColumn = line.IndexOf(symbolName, searchStartColumn, StringComparison.Ordinal);
        if (startColumn < 0)
        {
            startColumn = line.IndexOf(symbolName, absoluteStartColumn, StringComparison.Ordinal);
            if (startColumn < 0)
                startColumn = absoluteStartColumn;
        }
        else
        {
            searchStartColumn = startColumn + Math.Max(1, symbolName.Length);
        }

        entries.Add(new PythonImportSymbolEntry(symbolName, startColumn));
    }

    private static void AddPythonImportDottedPrefixEntries(
        string line,
        int absoluteStartColumn,
        string dottedName,
        List<PythonImportSymbolEntry> entries,
        HashSet<string> seenNames,
        ref int searchStartColumn)
    {
        var prefixStart = 0;
        while (prefixStart >= 0 && prefixStart < dottedName.Length)
        {
            var dotIndex = dottedName.IndexOf('.', prefixStart);
            if (dotIndex < 0)
                break;

            var prefix = dottedName[..dotIndex].Trim();
            if (prefix.Length > 0)
            {
                AddPythonImportEntry(line, absoluteStartColumn, prefix, entries, seenNames, ref searchStartColumn);
            }

            prefixStart = dotIndex + 1;
        }
    }

    private static string? GetPythonModulePrefix(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var normalizedPath = filePath.Replace('\\', '/');
        if (!normalizedPath.EndsWith("/__init__.py", StringComparison.Ordinal)
            && !string.Equals(normalizedPath, "__init__.py", StringComparison.Ordinal))
        {
            return null;
        }

        var modulePath = normalizedPath[..^"/__init__.py".Length];
        if (modulePath.Length == 0)
            return null;

        return modulePath.Replace('/', '.').Trim('.');
    }

    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) FindIndentRange(string[] lines, int startIndex)
    {
        var currentLine = lines[startIndex];
        var currentIndent = CountIndent(currentLine);
        var trimmedCurrent = currentLine.Trim();

        if (trimmedCurrent.Contains(':'))
        {
            var suffix = trimmedCurrent[(trimmedCurrent.IndexOf(':') + 1)..].Trim();
            if (suffix.Length > 0 && !suffix.StartsWith('#'))
                return (startIndex + 1, startIndex + 1, startIndex + 1);
        }

        int? bodyStartLine = null;
        int endLine = startIndex + 1;

        for (int i = startIndex + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
                continue;

            var indent = CountIndent(lines[i]);
            if (bodyStartLine == null)
            {
                if (indent <= currentIndent)
                    return (endLine, null, null);

                bodyStartLine = i + 1;
                endLine = i + 1;
                continue;
            }

            if (indent <= currentIndent)
                return (endLine, bodyStartLine, endLine);

            endLine = i + 1;
        }

        return bodyStartLine == null
            ? (startIndex + 1, null, null)
            : (endLine, bodyStartLine, endLine);
    }

}
