using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

public static partial class SymbolExtractor
{
    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) FindFortranRange(string[] lines, int startIndex)
    {
        if (!TryGetFortranBlockStartKind(lines[startIndex], out var blockKind))
            return (startIndex + 1, null, null);

        int? bodyStartLine = null;
        var endLine = startIndex + 1;

        for (int i = startIndex + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('!'))
                continue;

            if (IsFortranModuleProcedureEndLine(trimmed))
                continue;

            if (IsFortranBlockEndLine(trimmed, blockKind))
            {
                if (bodyStartLine == null)
                    return (i + 1, null, null);

                return (i + 1, bodyStartLine, i + 1);
            }

            if (bodyStartLine == null)
                bodyStartLine = i + 1;

            endLine = i + 1;
        }

        return bodyStartLine == null
            ? (startIndex + 1, null, null)
            : (endLine, bodyStartLine, endLine);
    }

    private static FortranContinuationMatchCandidate? TryBuildFortranContinuationMatchLine(string[] lines, int startIndex)
    {
        var firstLine = lines[startIndex];
        var firstTrimmed = firstLine.TrimStart();
        if (!StartsWithFortranContinuationCandidate(firstTrimmed))
            return null;

        var firstCode = StripFortranComment(firstLine).TrimEnd();
        if (!firstCode.Contains('&'))
            return null;

        var builder = new StringBuilder(firstCode.Length + 32);
        var lastConsumedLineIndex = startIndex;
        var currentLine = firstCode;

        while (true)
        {
            var continuationIndex = currentLine.LastIndexOf('&');
            if (continuationIndex < 0)
            {
                if (currentLine.Length > 0)
                {
                    if (builder.Length > 0)
                        builder.Append(' ');
                    builder.Append(currentLine.Trim());
                }

                break;
            }

            if (builder.Length > 0)
                builder.Append(' ');
            builder.Append(currentLine[..continuationIndex].TrimEnd());

            if (lastConsumedLineIndex + 1 >= lines.Length)
                break;

            var nextLine = StripFortranComment(lines[lastConsumedLineIndex + 1]).TrimStart();
            if (nextLine.StartsWith('&'))
                nextLine = nextLine[1..].TrimStart();

            lastConsumedLineIndex++;
            currentLine = nextLine.TrimEnd();
            if (currentLine.Length == 0)
                break;
        }

        var normalized = builder.ToString().Trim();
        return normalized.Length == 0 || lastConsumedLineIndex == startIndex
            ? null
            : new FortranContinuationMatchCandidate(normalized, lastConsumedLineIndex);
    }

    private static bool StartsWithFortranContinuationCandidate(string trimmedLine)
    {
        if (StartsWithFortranWord(trimmedLine, "interface"))
            return true;

        if (StartsWithFortranWord(trimmedLine, "abstract"))
        {
            var afterAbstract = trimmedLine["abstract".Length..].TrimStart();
            return StartsWithFortranWord(afterAbstract, "interface");
        }

        return StartsWithFortranContinuationPrefix(trimmedLine);
    }

    private static bool StartsWithFortranContinuationPrefix(string trimmedLine)
    {
        return StartsWithFortranWord(trimmedLine, "module")
            || StartsWithFortranWord(trimmedLine, "subroutine")
            || StartsWithFortranWord(trimmedLine, "function")
            || StartsWithFortranWord(trimmedLine, "procedure")
            || StartsWithFortranWord(trimmedLine, "pure")
            || StartsWithFortranWord(trimmedLine, "elemental")
            || StartsWithFortranWord(trimmedLine, "recursive")
            || StartsWithFortranWord(trimmedLine, "impure")
            || StartsWithFortranWord(trimmedLine, "integer")
            || StartsWithFortranWord(trimmedLine, "real")
            || StartsWithFortranWord(trimmedLine, "logical")
            || StartsWithFortranWord(trimmedLine, "complex")
            || StartsWithFortranWord(trimmedLine, "character")
            || StartsWithFortranWord(trimmedLine, "double")
            || StartsWithFortranWord(trimmedLine, "type")
            || StartsWithFortranWord(trimmedLine, "class");
    }

    private static string StripFortranComment(string line)
    {
        var commentIndex = line.IndexOf('!');
        return commentIndex >= 0 ? line[..commentIndex] : line;
    }

    private static bool TryGetFortranBlockStartKind(string line, out string kind)
    {
        kind = string.Empty;
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0)
            return false;

        if (StartsWithFortranWord(trimmed, "module"))
        {
            var remainder = trimmed["module".Length..].TrimStart();
            if (StartsWithFortranWord(remainder, "procedure") || StartsWithFortranWord(remainder, "subroutine") || StartsWithFortranWord(remainder, "function"))
                return false;

            kind = "module";
            return true;
        }

        if (StartsWithFortranWord(trimmed, "interface"))
        {
            kind = "interface";
            return true;
        }

        if (StartsWithFortranWord(trimmed, "submodule"))
        {
            kind = "submodule";
            return true;
        }

        if (StartsWithFortranWord(trimmed, "program"))
        {
            kind = "program";
            return true;
        }

        return false;
    }

    private static bool IsFortranBlockEndLine(string trimmedLine, string blockKind)
    {
        if (!StartsWithFortranWord(trimmedLine, "end"))
            return false;

        var remainder = trimmedLine["end".Length..].TrimStart();
        if (!StartsWithFortranWord(remainder, blockKind))
            return false;

        var afterKind = remainder[blockKind.Length..].TrimStart();
        if (blockKind == "module" && StartsWithFortranWord(afterKind, "procedure"))
            return false;

        return afterKind.Length == 0 || afterKind.StartsWith('!') || afterKind.StartsWith('(') || afterKind.StartsWith(':') || char.IsLetterOrDigit(afterKind[0]) || afterKind[0] == '_';
    }

    private static bool IsFortranModuleProcedureEndLine(string trimmedLine)
    {
        if (!StartsWithFortranWord(trimmedLine, "end"))
            return false;

        var remainder = trimmedLine["end".Length..].TrimStart();
        if (!StartsWithFortranWord(remainder, "module"))
            return false;

        var afterModule = remainder["module".Length..].TrimStart();
        return StartsWithFortranWord(afterModule, "procedure");
    }

    private static bool StartsWithFortranWord(string input, string word)
    {
        if (!input.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            return false;

        return input.Length == word.Length || !char.IsLetterOrDigit(input[word.Length]) && input[word.Length] != '_';
    }

}
