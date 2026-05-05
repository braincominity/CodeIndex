using CodeIndex.Models;

namespace CodeIndex.Indexer;

public static partial class SymbolExtractor
{
    private static void ExtractJavaScriptBareMethods(
        long fileId,
        string[] lines,
        List<SymbolRecord> symbols,
        JavaScriptScopePrivacyFlags[][] privateScopeColumns)
    {
        ExtractJavaScriptTypeScriptBareMethods(fileId, "javascript", lines, symbols, privateScopeColumns);
    }
}
