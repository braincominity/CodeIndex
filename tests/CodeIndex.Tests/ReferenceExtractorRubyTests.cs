using CodeIndex.Indexer;

namespace CodeIndex.Tests;

public class ReferenceExtractorRubyTests
{
    [Fact]
    public void Extract_Ruby_EmitsCustomDslAndMetaprogrammingSymbolReferences()
    {
        const string content = """
            class Order
              custom_callback :notify
              define_method(:nice_name) { name }
              send(:cleanup) if respond_to?(:cleanup)
              public_send(:deliver)
            end
            """;

        var symbols = SymbolExtractor.Extract(1, "ruby", content);
        var references = ReferenceExtractor.Extract(1, "ruby", content, symbols);

        Assert.Contains(references, reference =>
            reference.SymbolName == "notify"
            && reference.ReferenceKind == "reference"
            && reference.ContainerName == "Order");
        Assert.Contains(references, reference =>
            reference.SymbolName == "nice_name"
            && reference.ReferenceKind == "reference"
            && reference.ContainerName == "Order");
        Assert.Contains(references, reference =>
            reference.SymbolName == "cleanup"
            && reference.ReferenceKind == "reference"
            && reference.ContainerName == "Order");
        Assert.Contains(references, reference =>
            reference.SymbolName == "deliver"
            && reference.ReferenceKind == "reference"
            && reference.ContainerName == "Order");
    }
}
