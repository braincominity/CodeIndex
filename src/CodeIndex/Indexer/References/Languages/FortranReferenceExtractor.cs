using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class FortranReferenceExtractor
{
    public static void EmitTypePositionReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container)
    {
        LanguageReferenceExtractionSupport.EmitTypePositionReferences(
            "fortran",
            preparedLine,
            preparedLine,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn,
            container);
    }

    public static void EmitAdditionalCallReferences(string preparedLine, Action<string, int> addCallLikeReference)
    {
        LanguageReferenceExtractionSupport.EmitAdditionalCallReferences(
            "fortran",
            preparedLine,
            preparedLine,
            addCallLikeReference,
            [],
            [],
            0,
            string.Empty,
            0,
            _ => null,
            definitionNames: null);
    }
}
