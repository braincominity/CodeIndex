using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class LuaReferenceExtractor
{
    public static string[] MaskLongCommentAndStringLines(IReadOnlyList<string> originalLines)
        => LanguageReferenceExtractionSupport.MaskLuaLongCommentAndStringLines(originalLines);

    public static void EmitTypePositionReferences(
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        LanguageReferenceExtractionSupport.EmitTypePositionReferences(
            "lua",
            originalLine,
            originalLine,
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
            "lua",
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
