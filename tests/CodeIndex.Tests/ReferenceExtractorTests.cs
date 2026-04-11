using CodeIndex.Indexer;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for lightweight reference extraction.
/// 軽量参照抽出のテスト。
/// </summary>
public class ReferenceExtractorTests
{
    [Fact]
    public void Extract_PythonCall_AssignsCallerContainer()
    {
        const string content = """
            def login(user, password):
                return authenticate(user, password)
            """;

        var symbols = SymbolExtractor.Extract(1, "python", content);
        var references = ReferenceExtractor.Extract(1, "python", content, symbols);

        var reference = Assert.Single(references);
        Assert.Equal("authenticate", reference.SymbolName);
        Assert.Equal("call", reference.ReferenceKind);
        Assert.Equal("login", reference.ContainerName);
    }

    [Fact]
    public void Extract_CsharpDefinitionLine_DoesNotBecomeReference()
    {
        const string content = """
            public class App
            {
                public void Run()
                {
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Empty(references);
    }

    [Fact]
    public void Extract_UnsupportedLanguage_ReturnsEmpty()
    {
        const string content = "hello = world";

        var references = ReferenceExtractor.Extract(1, "markdown", content, []);

        Assert.Empty(references);
    }

    [Theory]
    [InlineData("dart")]
    [InlineData("scala")]
    public void SupportsLanguage_DartAndScala_ReturnsTrue(string lang)
    {
        Assert.True(ReferenceExtractor.SupportsLanguage(lang));
    }

    [Fact]
    public void Extract_Dart_DetectsCallSites()
    {
        const string content = "void main() {\n  runApp(MyApp());\n}";
        var symbols = SymbolExtractor.Extract(1, "dart", content);
        var references = ReferenceExtractor.Extract(1, "dart", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "runApp");
        Assert.Contains(references, r => r.SymbolName == "MyApp");
    }

    [Fact]
    public void Extract_Scala_DetectsCallSites()
    {
        const string content = "object Main {\n  def run(): Unit = {\n    println(compute(42))\n  }\n}";
        var symbols = SymbolExtractor.Extract(1, "scala", content);
        var references = ReferenceExtractor.Extract(1, "scala", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "println");
        Assert.Contains(references, r => r.SymbolName == "compute");
    }

    [Fact]
    public void SupportsLanguage_FSharp_ReturnsFalse()
    {
        // F# uses space-separated call syntax (foo x) not parenthesized, so
        // the regex-based extractor cannot reliably detect call sites.
        // F#はスペース区切りの呼び出し構文(foo x)を使うため、正規表現ベースの
        // 抽出では呼び出し箇所を正確に検出できない。
        Assert.False(ReferenceExtractor.SupportsLanguage("fsharp"));
    }
}
