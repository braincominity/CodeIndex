using CodeIndex.Models;
using System.Text.RegularExpressions;

namespace CodeIndex.Indexer;

internal static class ElixirReferenceExtractor
{
    private static readonly Regex PipeCallRegex = new(
        @"\|>\s*(?:(?:[A-Z_]\w*(?:\.[A-Z_]\w*)*)\.)?(?<name>[a-z_]\w*[!?]?)\s*(?=\(|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> IgnoredPipeCallNames = new(StringComparer.Ordinal)
    {
        "and",
        "catch",
        "do",
        "else",
        "end",
        "fn",
        "in",
        "not",
        "or",
        "rescue",
        "when",
    };

    public static void EmitTypePositionReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        LanguageReferenceExtractionSupport.EmitTypePositionReferences(
            "elixir",
            preparedLine,
            preparedLine,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            _ => container,
            container);
    }

    public static void EmitAdditionalCallReferences(
        string preparedLine,
        Action<string, int> addCallLikeReference,
        IReadOnlySet<string>? definitionNames)
    {
        foreach (Match match in PipeCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (!IgnoredPipeCallNames.Contains(name))
                addCallLikeReference(name, match.Groups["name"].Index);
        }

        LanguageReferenceExtractionSupport.EmitAdditionalCallReferences(
            "elixir",
            preparedLine,
            preparedLine,
            addCallLikeReference,
            [],
            [],
            0,
            string.Empty,
            0,
            _ => null,
            definitionNames);
    }
}
