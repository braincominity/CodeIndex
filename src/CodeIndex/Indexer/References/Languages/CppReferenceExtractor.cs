using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class CppReferenceExtractor
{
    public static void EmitTypePositionReferences(
        string preparedLine,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        LanguageReferenceExtractionSupport.EmitTypePositionReferences(
            "cpp",
            preparedLine,
            originalLine,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn,
            container: null);
    }
}
