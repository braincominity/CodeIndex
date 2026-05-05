using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class ElixirReferenceExtractor
{
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
