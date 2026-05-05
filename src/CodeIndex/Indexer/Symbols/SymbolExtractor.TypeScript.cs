using CodeIndex.Models;

namespace CodeIndex.Indexer;

public static partial class SymbolExtractor
{
    private static void ExtractTypeScriptBareMethods(
        long fileId,
        string[] lines,
        List<SymbolRecord> symbols,
        JavaScriptScopePrivacyFlags[][] privateScopeColumns)
    {
        ExtractJavaScriptTypeScriptBareMethods(fileId, "typescript", lines, symbols, privateScopeColumns);
    }
}
