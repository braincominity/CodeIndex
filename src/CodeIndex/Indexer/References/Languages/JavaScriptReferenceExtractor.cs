using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class JavaScriptReferenceExtractor
{
    // JavaScript / TypeScript allow zero-argument constructor calls without parentheses:
    // `new Foo;`, `new Date;`, `new Demo.Provider;`, `new Box<number>;`.
    // Keep this language-gated at the call site so Java / C# / other `new` forms still require
    // either `(` or their own dedicated initializer path.
    // JavaScript / TypeScript では引数なしコンストラクタ呼び出しで括弧を省略できる。
    // 他言語の `new` 形と混線しないよう、呼び出し側で JS/TS に限定する。
    private static readonly Regex ParenlessConstructorRegex = new(
        $@"\bnew\s+(?:{ReferenceExtractor.CSharpIdentifierPattern}\s*\.\s*)*(?<name>{ReferenceExtractor.CSharpIdentifierPattern})(?:\s*<[^>\n]+>)?",
        RegexOptions.Compiled);

    public static void EmitParenlessConstructorReferences(
        string preparedLine,
        IReadOnlyList<string> preparedLines,
        int currentLineIndex,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForCall)
    {
        foreach (Match match in ParenlessConstructorRegex.Matches(preparedLine))
        {
            var rawName = match.Groups["name"].Value;
            var nameIndex = match.Groups["name"].Index;
            var trailingProbe = match.Index + match.Length;
            while (trailingProbe < preparedLine.Length && char.IsWhiteSpace(preparedLine[trailingProbe]))
                trailingProbe++;

            if (trailingProbe >= preparedLine.Length)
            {
                if (NextNonEmptyPreparedLineStartsWithContinuation(preparedLines, currentLineIndex))
                    continue;
            }
            else
            {
                if (preparedLine[trailingProbe] is '(' or '.' or '[')
                    continue;

                if (preparedLine[trailingProbe] == '?'
                    && trailingProbe + 1 < preparedLine.Length
                    && preparedLine[trailingProbe + 1] == '.')
                {
                    continue;
                }
            }

            var initContainer = resolveContainerForCall(nameIndex);
            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                rawName,
                nameIndex,
                "instantiate",
                context,
                lineNumber,
                initContainer);
        }
    }

    private static bool NextNonEmptyPreparedLineStartsWithContinuation(
        IReadOnlyList<string> preparedLines,
        int currentLineIndex)
    {
        for (var next = currentLineIndex + 1; next < preparedLines.Count; next++)
        {
            var trimmed = preparedLines[next].TrimStart();
            if (trimmed.Length == 0)
                continue;

            return trimmed.StartsWith(".", StringComparison.Ordinal)
                || trimmed.StartsWith("?.", StringComparison.Ordinal)
                || trimmed.StartsWith("[", StringComparison.Ordinal)
                || trimmed.StartsWith("(", StringComparison.Ordinal);
        }

        return false;
    }
}
