using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class GoReferenceExtractor
{
    private static readonly System.Text.RegularExpressions.Regex GoroutineCallRegex = new(
        @"\bgo\s+(?<name>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*\(",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex ChannelSendRegex = new(
        @"(?<!<)(?<name>[A-Za-z_]\w*)\s*<-",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex ChannelReceiveRegex = new(
        @"(?<!<)<-\s*(?<name>[A-Za-z_]\w*)",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool[] BuildImportBlockLineMap(IReadOnlyList<string> originalLines)
        => LanguageReferenceExtractionSupport.BuildGoImportBlockLineMap(originalLines);

    public static void EmitConcurrencyReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (System.Text.RegularExpressions.Match match in GoroutineCallRegex.Matches(preparedLine))
        {
            var group = match.Groups["name"];
            var rawName = group.Value;
            var dot = rawName.LastIndexOf('.');
            var name = dot >= 0 && dot + 1 < rawName.Length ? rawName[(dot + 1)..] : rawName;
            var nameIndex = dot >= 0 && dot + 1 < rawName.Length ? group.Index + dot + 1 : group.Index;
            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                name,
                nameIndex,
                "goroutine_spawn",
                context,
                lineNumber,
                resolveContainerForColumn(nameIndex));
        }

        foreach (System.Text.RegularExpressions.Match match in ChannelSendRegex.Matches(preparedLine))
        {
            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                match.Groups["name"].Value,
                match.Groups["name"].Index,
                "channel_send",
                context,
                lineNumber,
                resolveContainerForColumn(match.Groups["name"].Index));
        }

        foreach (System.Text.RegularExpressions.Match match in ChannelReceiveRegex.Matches(preparedLine))
        {
            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                match.Groups["name"].Value,
                match.Groups["name"].Index,
                "channel_receive",
                context,
                lineNumber,
                resolveContainerForColumn(match.Groups["name"].Index));
        }
    }

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
