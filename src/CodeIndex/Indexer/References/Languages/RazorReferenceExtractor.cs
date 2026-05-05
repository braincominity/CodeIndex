using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class RazorReferenceExtractor
{
    public static string[] MaskCommentLines(IReadOnlyList<string> originalLines)
        => LanguageReferenceExtractionSupport.MaskRazorCommentLines(originalLines);

    public static void EmitReferences(
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        IReadOnlySet<string>? definitionNames)
    {
        LanguageReferenceExtractionSupport.EmitRazorReferences(
            originalLine,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn,
            definitionNames);
    }
}
