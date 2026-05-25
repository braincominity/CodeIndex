namespace CodeIndex.Indexer;

internal static partial class SqlReferenceExtractor
{
    internal readonly record struct DefinitionLeafSpan(string LeafName, int StartIndex, int EndIndexExclusive);

    internal readonly record struct IdentifierScanState(
        bool InBlockComment,
        string? DollarQuoteDelimiter,
        bool InSingleQuotedString);

    internal sealed class State
    {
        public HashSet<string> EstablishedTempObjectNames { get; } = new(StringComparer.Ordinal);
        public string StatementPrefix { get; set; } = string.Empty;
        public IdentifierScanState IdentifierScanState { get; set; }
    }
}
