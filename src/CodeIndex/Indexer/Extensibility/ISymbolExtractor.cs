using CodeIndex.Models;

namespace CodeIndex.Indexer.Extensibility;

public interface ISymbolExtractor
{
    string Language { get; }

    IReadOnlyCollection<string> FileExtensions => [];

    IReadOnlyList<SymbolRecord> Extract(long fileId, string source, ExtractionContext context);
}
