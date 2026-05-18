using CodeIndex.Models;

namespace CodeIndex.Indexer.Extensibility;

public interface IReferenceExtractor
{
    string Language { get; }

    IReadOnlyCollection<string> FileExtensions => [];

    IReadOnlyList<ReferenceRecord> Extract(long fileId, string source, ExtractionContext context);
}
