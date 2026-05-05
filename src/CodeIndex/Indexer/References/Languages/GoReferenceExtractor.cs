using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class GoReferenceExtractor
{
    public static bool[] BuildImportBlockLineMap(IReadOnlyList<string> originalLines)
        => LanguageReferenceExtractionSupport.BuildGoImportBlockLineMap(originalLines);

    public static void EmitTypePositionReferences(
        string preparedLine,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        bool isImportBlockLine)
    {
        LanguageReferenceExtractionSupport.EmitTypePositionReferences(
            "go",
            preparedLine,
            originalLine,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn,
            container: null,
            isGoImportBlockLine: isImportBlockLine);
    }
}
