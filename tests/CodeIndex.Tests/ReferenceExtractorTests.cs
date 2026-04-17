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
    public void Extract_CsharpExpressionBodiedMembers_AttributeToIndividualMember()
    {
        // issue #233: expression-bodied methods and properties must attribute their
        // calls to the individual member, not collapse to the enclosing class.
        // issue #233: 式本体メソッドと式本体プロパティは、外側クラスにまとめられず
        // 個別メンバーに呼び出しが帰属する必要がある。
        const string content = """
            namespace App;

            public class Calc
            {
                public int Compute() => 42;
                public int Wrap1() => Compute();
                public int Wrap2() => this.Compute();
                public int Wrap3 => Compute();
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var computeRefs = references.Where(r => r.SymbolName == "Compute").ToList();
        Assert.Equal(3, computeRefs.Count);
        Assert.Contains(computeRefs, r => r.ContainerKind == "function" && r.ContainerName == "Wrap1");
        Assert.Contains(computeRefs, r => r.ContainerKind == "function" && r.ContainerName == "Wrap2");
        Assert.Contains(computeRefs, r => r.ContainerKind == "property" && r.ContainerName == "Wrap3");
        Assert.DoesNotContain(computeRefs, r => r.ContainerKind == "class");
    }

    [Fact]
    public void Extract_CsharpExpressionBodiedMultiLine_AttributesToMember()
    {
        // Multi-line expression body (declaration on one line, `=> expr;` on the next)
        // must still attribute calls on the expression line to the enclosing member.
        // 宣言行の次の行に `=> expr;` が来る multi-line 式本体でも、式行の呼び出しが
        // 外側メンバーに帰属すること。
        const string content = """
            public class Calc
            {
                public int Compute() => 42;
                public int MultiLine()
                    => Compute();
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var computeRef = Assert.Single(references.Where(r => r.SymbolName == "Compute"));
        Assert.Equal("function", computeRef.ContainerKind);
        Assert.Equal("MultiLine", computeRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpMultiLineExpressionBodiedProperty_AttributesToProperty()
    {
        // issue #233 second review follow-up: multi-line expression-bodied property
        // (`public int Wrap` + newline + `    => Compute();`) must attribute the
        // Compute() call to the property, not the enclosing class.
        // issue #233 の再レビュー指摘: 宣言行の次行に `=> expr;` が来る multi-line 式本体
        // プロパティも、Compute() 呼び出しが外側クラスではなく property に帰属すること。
        const string content = """
            public class Calc
            {
                public int Compute() => 42;
                public int Wrap
                    => Compute();
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var computeRef = Assert.Single(references.Where(r => r.SymbolName == "Compute"));
        Assert.Equal("property", computeRef.ContainerKind);
        Assert.Equal("Wrap", computeRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpBlockBodiedPropertyAccessor_AttributesToProperty()
    {
        // issue #233 review follow-up: Allman-style block-bodied properties (with `{`
        // on the next line) must have accessor-internal calls attributed to the property,
        // not the enclosing class.
        // issue #233 のレビュー指摘: Allman スタイル（次行に `{`）の block-bodied property は、
        // accessor 内部の呼び出しが外側クラスではなく property に帰属する必要がある。
        const string content = """
            public class Calc
            {
                public int Compute() => 42;

                public int Wrap
                {
                    get { return Compute(); }
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var computeRef = Assert.Single(references.Where(r => r.SymbolName == "Compute"));
        Assert.Equal("property", computeRef.ContainerKind);
        Assert.Equal("Wrap", computeRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpBraceSameLineAccessorNextLineProperty_AttributesToProperty()
    {
        // issue #233 fifth review follow-up: the common Microsoft-style block-bodied
        // property — `{` on the same line as the declaration and the accessor on the
        // following line — must have accessor-internal calls attributed to the property,
        // not the enclosing class.
        // issue #233 第5次レビュー指摘: `{` が宣言行末にあり、accessor が次行にある
        // 標準的な block-bodied property でも、accessor 内部の呼び出しが外側クラスでは
        // なく property に帰属する必要がある。
        const string content = """
            public class Calc
            {
                public int Compute() => 42;

                public int Wrap {
                    get { return Compute(); }
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var computeRef = Assert.Single(references.Where(r => r.SymbolName == "Compute"));
        Assert.Equal("property", computeRef.ContainerKind);
        Assert.Equal("Wrap", computeRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpAllmanBlockBodiedProperty_WithBlockComment_AttributesToProperty()
    {
        // issue #233 fourth review follow-up: a multi-line /* ... */ block comment
        // between the property header line and its `{` must not prevent the property
        // from being recognized, so accessor-internal calls still attribute to the
        // property rather than the enclosing class.
        // issue #233 の 4 回目レビュー指摘: property のヘッダ行と `{` の間に複数行の
        // /* ... */ ブロックコメントが入っていても、property として認識され、
        // accessor 内部の呼び出しは外側クラスではなく property に帰属する必要がある。
        const string content = """
            public class Calc
            {
                public int Compute() => 42;

                public int Wrap
                /* some multi-line
                   block comment */
                {
                    get { return Compute(); }
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var computeRef = Assert.Single(references.Where(r => r.SymbolName == "Compute"));
        Assert.Equal("property", computeRef.ContainerKind);
        Assert.Equal("Wrap", computeRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpMultiLineExpressionBodiedProperty_WithBlockComment_AttributesToProperty()
    {
        // issue #233 fourth review follow-up: a multi-line /* ... */ block comment
        // between the property header line and its `=>` continuation must not prevent
        // the property from being recognized, so Compute() still attributes to the
        // property rather than the enclosing class.
        // issue #233 の 4 回目レビュー指摘: property のヘッダ行と `=>` 継続行の間に
        // 複数行の /* ... */ ブロックコメントが入っていても property として認識され、
        // Compute() 呼び出しは外側クラスではなく property に帰属する必要がある。
        const string content = """
            public class Calc
            {
                public int Compute() => 42;

                public int Wrap
                /* multi-line
                   comment */
                    => Compute();
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var computeRef = Assert.Single(references.Where(r => r.SymbolName == "Compute"));
        Assert.Equal("property", computeRef.ContainerKind);
        Assert.Equal("Wrap", computeRef.ContainerName);
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
    public void Extract_CsharpRawStringFixture_DoesNotBecomeReference()
    {
        const string content = """"
            public class FixtureHost
            {
                public void UsesRawFixture()
                {
                    const string fixture = """
                        Execute();
                        new Widget();
                        main();
                        """;

                    Run();
                }
            }
            """";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var reference = Assert.Single(references);
        Assert.Equal("Run", reference.SymbolName);
        Assert.Equal("call", reference.ReferenceKind);
        Assert.Equal("UsesRawFixture", reference.ContainerName);
    }

    [Fact]
    public void Extract_CsharpInterpolatedRawString_KeepsInterpolationCallReferences()
    {
        const string content = """"
            public class FixtureHost
            {
                public string Run() => "ok";

                public string UsesRawFixture()
                {
                    return $"""
                        value = {Run()}
                        literal = function main()
                        """;
                }
            }
            """";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var reference = Assert.Single(references);
        Assert.Equal("Run", reference.SymbolName);
        Assert.Equal("call", reference.ReferenceKind);
        Assert.Equal("UsesRawFixture", reference.ContainerName);
    }

    [Fact]
    public void Extract_CsharpInterpolatedRawString_WithNestedInterpolatedString_KeepsInterpolationCallReferences()
    {
        const string content = """"
            public class FixtureHost
            {
                public string Run() => "ok";

                public string UsesRawFixture()
                {
                    return $"""
                        value = {$"{Run()}"}
                        literal = function main()
                        """;
                }
            }
            """";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var runReference = Assert.Single(references.Where(reference => reference.SymbolName == "Run"));
        Assert.Equal("call", runReference.ReferenceKind);
        Assert.Equal("UsesRawFixture", runReference.ContainerName);
    }

    [Fact]
    public void Extract_CsharpInterpolatedRawString_WithNestedRawString_DoesNotLeakPhantomReferences()
    {
        const string content = """"
            public class FixtureHost
            {
                public int Run() => 1;
                public string Id(string value) => value;

                public int UsesRawFixture()
                {
                    return $"""
                        value = {Id("""
                            Execute();
                            public class Phantom
                            {
                                public void Go() { }
                            }
                            """) + Run()}
                        """.Length;
                }
            }
            """";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, reference => reference.SymbolName == "Id" && reference.ContainerName == "UsesRawFixture");
        Assert.Contains(references, reference => reference.SymbolName == "Run" && reference.ContainerName == "UsesRawFixture");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "Execute");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "Go");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "Phantom");
    }

    [Fact]
    public void Extract_CsharpInterpolatedString_WithEscapedBraces_DoesNotLeakPhantomReference()
    {
        const string content = """
            public class FixtureHost
            {
                public string Render()
                {
                    return $"{{Run()}}";
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "Run");
    }

    [Fact]
    public void Extract_CsharpInterpolatedVerbatimString_WithEscapedBraces_DoesNotLeakPhantomReference()
    {
        const string content = """
            public class FixtureHost
            {
                public string Render()
                {
                    return $@"{{Run()}}";
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "Run");
    }

    [Fact]
    public void Extract_CsharpMultiLineVerbatimString_DoesNotLeakPhantomCallReferences()
    {
        // Regression for issue #288: call-looking identifiers inside a multi-line
        // @"..." verbatim string body must not be captured as references.
        // issue #288 回帰: 複数行 @"..." 逐語文字列の本体にある呼び出しらしい識別子は
        // 参照として抽出してはならない。
        const string content = """
            public class FixtureHost
            {
                public void M()
                {
                    var legacy = @"
                        SELECT * FROM t
                        WHERE x = BadCall()
                    ";
                    RealCall();
                }

                private void RealCall() { }
                private void BadCall() { }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "BadCall");
        Assert.Contains(references, reference => reference.SymbolName == "RealCall" && reference.ContainerName == "M");
    }

    [Fact]
    public void Extract_CsharpMultiLineRawString_IssueRepro_DoesNotLeakPhantomCallReferences()
    {
        // Regression for issue #288 exact repro: C# 11 raw strings, interpolated raw
        // strings, and multi-line verbatim strings must not leak call-looking
        // identifiers from their content into the reference graph.
        // issue #288 の repro に対する回帰: C# 11 の raw string、補間付き raw string、
        // 複数行 verbatim string の本体から呼び出しらしい識別子を参照グラフへ漏らさない。
        const string content = """"
            namespace App;

            public class Demo
            {
                public void M()
                {
                    var sql = """
                        SELECT * FROM users
                        WHERE id = EvilCall(42)
                        AND name = AnotherCall('bob')
                        """;

                    var id = 42;
                    var msg = $"""
                        Query result for id={id}
                        Hidden: PhantomCall({id})
                        """;

                    var legacy = @"
                        SELECT * FROM t
                        WHERE x = BadCall()
                    ";

                    RealCall();
                }

                private void RealCall() { }
                private int EvilCall(int x) => x;
                private string AnotherCall(string s) => s;
                private int PhantomCall(int x) => x;
                private void BadCall() { }
            }
            """";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "EvilCall");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "AnotherCall");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "PhantomCall");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "BadCall");
        Assert.Contains(references, reference => reference.SymbolName == "RealCall" && reference.ContainerName == "M");
    }

    [Fact]
    public void Extract_CsharpKeywords_NotExtractedAsReferences()
    {
        // LINQ and C# contextual keywords should be ignored
        // LINQ や C# の文脈キーワードは参照として抽出されないこと
        const string content = """
            public class Query
            {
                public void Run()
                {
                    var x = from(items);
                    if (x is string s) { }
                    var y = default(int);
                    base.ToString();
                    value.GetType();
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, r => r.SymbolName == "from");
        Assert.DoesNotContain(references, r => r.SymbolName == "is");
        Assert.DoesNotContain(references, r => r.SymbolName == "default");
        // ToString and GetType are real calls, should be extracted
        Assert.Contains(references, r => r.SymbolName == "ToString");
        Assert.Contains(references, r => r.SymbolName == "GetType");
    }

    [Fact]
    public void Extract_PythonLegitimateCalls_AreNotDroppedByOtherLanguageKeywordLists()
    {
        const string content = """
            def caller():
                run()
                build()
                install()
                clean()
                help()
                print()
                require()
                notexcluded()
                apply()
                task()
            """;

        var symbols = SymbolExtractor.Extract(1, "python", content);
        var references = ReferenceExtractor.Extract(1, "python", content, symbols);

        var names = references.Select(reference => reference.SymbolName).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("run", names);
        Assert.Contains("build", names);
        Assert.Contains("install", names);
        Assert.Contains("clean", names);
        Assert.Contains("help", names);
        Assert.Contains("print", names);
        Assert.Contains("require", names);
        Assert.Contains("notexcluded", names);
        Assert.Contains("apply", names);
        Assert.Contains("task", names);
        Assert.Equal(10, references.Count(reference => reference.ReferenceKind == "call"));
    }

    [Fact]
    public void Extract_PythonRaiseSyntax_IsIgnored()
    {
        const string content = """
            def fail():
                raise(ValueError())
            """;

        var symbols = SymbolExtractor.Extract(1, "python", content);
        var references = ReferenceExtractor.Extract(1, "python", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "raise");
        Assert.Contains(references, reference => reference.SymbolName == "ValueError" && reference.ContainerName == "fail");
    }

    [Fact]
    public void Extract_PythonYieldSyntax_IsIgnored()
    {
        const string content = """
            def stream(xs):
                yield(item())
                yield from(source())
            """;

        var symbols = SymbolExtractor.Extract(1, "python", content);
        var references = ReferenceExtractor.Extract(1, "python", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "yield");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "from");
        Assert.Contains(references, reference => reference.SymbolName == "item" && reference.ContainerName == "stream");
        Assert.Contains(references, reference => reference.SymbolName == "source" && reference.ContainerName == "stream");
    }

    [Fact]
    public void Extract_JavaScriptRequireCall_IsNotDropped()
    {
        const string content = """
            function load() {
                require("fs");
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, reference => reference.SymbolName == "require" && reference.ContainerName == "load");
    }

    [Fact]
    public void Extract_JavaScriptSyntaxConstructs_AreIgnored()
    {
        const string content = """
            class Base {
                run() {}
            }

            class Derived extends Base {
                constructor() {
                    import("fs");
                    super();
                    require("path");
                }

                *stream(item) {
                    yield(item);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "import");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "super");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "yield");
        Assert.Contains(references, reference => reference.SymbolName == "require" && reference.ContainerName == "constructor");
    }

    [Fact]
    public void Extract_RubyRequireCall_IsNotDropped()
    {
        const string content = """
            def load
              require("json")
            end
            """;

        var symbols = SymbolExtractor.Extract(1, "ruby", content);
        var references = ReferenceExtractor.Extract(1, "ruby", content, symbols);

        Assert.Contains(references, reference => reference.SymbolName == "require" && reference.ContainerName == "load");
    }

    [Fact]
    public void Extract_RubyRaiseSyntax_IsIgnored()
    {
        const string content = """
            def fail
              raise("boom")
              ValueError()
            end
            """;

        var symbols = SymbolExtractor.Extract(1, "ruby", content);
        var references = ReferenceExtractor.Extract(1, "ruby", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "raise");
        Assert.Contains(references, reference => reference.SymbolName == "ValueError" && reference.ContainerName == "fail");
    }

    [Fact]
    public void Extract_RubyContextualKeywords_AreIgnored()
    {
        const string content = """
            module Shared
            end

            class Worker
              include(Shared)

              def run(x)
                super(x)
                yield(item())
              end
            end
            """;

        var symbols = SymbolExtractor.Extract(1, "ruby", content);
        var references = ReferenceExtractor.Extract(1, "ruby", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "include");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "super");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "yield");
        Assert.Contains(references, reference => reference.SymbolName == "item" && reference.ContainerName == "run");
    }

    [Fact]
    public void Extract_CsharpRunCall_IsNotDroppedByMakefileKeywordList()
    {
        const string content = """
            public class Worker
            {
                public void Execute(Task task)
                {
                    task.Run();
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, reference => reference.SymbolName == "Run" && reference.ContainerName == "Execute");
    }

    [Fact]
    public void Extract_CsharpReturnTargetAttribute_CallIsNotDropped()
    {
        const string content = """
            using System.Runtime.InteropServices;

            public class Foo
            {
                [MarshalAs(UnmanagedType.Bool)]
                public bool Field1;

                [return: MarshalAs(UnmanagedType.Bool)]
                public bool Method1() => true;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var marshalAsCalls = references
            .Where(reference => reference.SymbolName == "MarshalAs" && reference.ReferenceKind == "call")
            .OrderBy(reference => reference.Line)
            .ToList();

        Assert.Equal(2, marshalAsCalls.Count);
        Assert.Equal([5, 8], marshalAsCalls.Select(reference => reference.Line).ToArray());
        Assert.All(marshalAsCalls, reference => Assert.Equal("Foo", reference.ContainerName));
    }

    [Fact]
    public void Extract_ConstructorCalls_AreInstantiateOnly()
    {
        const string content = """
            namespace N
            {
                public class Foo { }
                public class Bar { }
            }

            public class Worker
            {
                public void Execute()
                {
                    var foo = new N.Foo();
                    var bar = new global::N.Bar();
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, reference => reference.SymbolName == "Foo" && reference.ReferenceKind == "instantiate");
        Assert.Contains(references, reference => reference.SymbolName == "Bar" && reference.ReferenceKind == "instantiate");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "Foo" && reference.ReferenceKind == "call");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "Bar" && reference.ReferenceKind == "call");
    }

    [Fact]
    public void Extract_PhpIncludeRequireConstructs_AreIgnored()
    {
        const string content = """
            <?php
            function load() {
                require("lib.php");
                REQUIRE("lib_upper.php");
                require_once("lib_once.php");
                include("more.php");
                Include_Once("more_once_mixed.php");
                include_once("more_once.php");
                custom();
            }
            ?>
            """;

        var symbols = SymbolExtractor.Extract(1, "php", content);
        var references = ReferenceExtractor.Extract(1, "php", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "require");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "REQUIRE");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "require_once");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "include");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "Include_Once");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "include_once");
        Assert.Contains(references, reference => reference.SymbolName == "custom" && reference.ContainerName == "load");
    }

    [Fact]
    public void Extract_PhpCaseInsensitiveSharedKeywords_AreIgnored()
    {
        const string content = """
            <?php
            function load($flag, $items) {
                IF($flag) { custom(); }
                WHILE($flag) { break; }
                FOREACH($items as $item) { custom(); }
            }
            ?>
            """;

        var symbols = SymbolExtractor.Extract(1, "php", content);
        var references = ReferenceExtractor.Extract(1, "php", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "IF");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "WHILE");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "FOREACH");
        Assert.Equal(2, references.Count(reference => reference.SymbolName == "custom" && reference.ReferenceKind == "call"));
    }

    [Fact]
    public void Extract_PhpCaseInsensitiveNew_IsInstantiate()
    {
        const string content = """
            <?php
            function load() {
                new \Foo();
                NEW namespace\Bar();
            }
            ?>
            """;

        var symbols = SymbolExtractor.Extract(1, "php", content);
        var references = ReferenceExtractor.Extract(1, "php", content, symbols);

        Assert.Contains(references, reference => reference.SymbolName == "Foo" && reference.ReferenceKind == "instantiate");
        Assert.Contains(references, reference => reference.SymbolName == "Bar" && reference.ReferenceKind == "instantiate");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "Foo" && reference.ReferenceKind == "call");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "Bar" && reference.ReferenceKind == "call");
    }

    [Fact]
    public void Extract_PhpLanguageConstructCalls_AreIgnored()
    {
        const string content = """
            <?php
            function load($value) {
                echo("hello");
                EXIT();
                Eval("return 1;");
                empty($value);
                custom();
            }
            ?>
            """;

        var symbols = SymbolExtractor.Extract(1, "php", content);
        var references = ReferenceExtractor.Extract(1, "php", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "echo");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "EXIT");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "Eval");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "empty");
        Assert.Contains(references, reference => reference.SymbolName == "custom" && reference.ContainerName == "load");
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
    public void Extract_CsharpEventSubscription_DetectsAsSubscribe()
    {
        const string content = """
            public class Form
            {
                public void Init()
                {
                    Click += OnClick;
                    Loaded -= OnLoaded;
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "Click" && r.ReferenceKind == "subscribe");
        Assert.Contains(references, r => r.SymbolName == "Loaded" && r.ReferenceKind == "subscribe");
    }

    [Fact]
    public void Extract_CsharpArithmeticCompoundAssignment_NotExtractedAsSubscribe()
    {
        const string content = """
            public class Counter
            {
                public void Increment()
                {
                    count += 1;
                    flags -= mask;
                    total += GetAmount();
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        // Arithmetic compound assignments should NOT produce subscribe references
        Assert.DoesNotContain(references, r => r.SymbolName == "count" && r.ReferenceKind == "subscribe");
        Assert.DoesNotContain(references, r => r.SymbolName == "flags" && r.ReferenceKind == "subscribe");
        // But total += GetAmount() has an identifier RHS, so total may match — that's acceptable
        // (it's still preferable to the previous behavior of matching everything)
    }

    [Fact]
    public void Extract_ElixirCall_DetectsReferences()
    {
        const string content = """
            defmodule MyApp do
              def run() do
                IO.puts("hello")
                GenServer.start_link(MyWorker, [])
              end
            end
            """;

        var symbols = SymbolExtractor.Extract(1, "elixir", content);
        var references = ReferenceExtractor.Extract(1, "elixir", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "puts");
        Assert.Contains(references, r => r.SymbolName == "start_link");
    }

    [Fact]
    public void Extract_CsharpUsingDeclaration_NotExtractedAsReference()
    {
        // 'using var x = ...' should not generate a reference for 'using'
        // 'using var x = ...' で 'using' が参照として生成されないこと
        const string content = """
            public class Db
            {
                public void Query()
                {
                    using var cmd = CreateCommand();
                    using (var reader = cmd.ExecuteReader()) { }
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, r => r.SymbolName == "using");
        // Real calls should still be captured / 実際の呼び出しは抽出されるべき
        Assert.Contains(references, r => r.SymbolName == "CreateCommand");
        Assert.Contains(references, r => r.SymbolName == "ExecuteReader");
    }

    [Fact]
    public void Extract_LuaCall_DetectsReferences()
    {
        const string content = """
            function main()
                io.write("world")
                table.insert(items, value)
                -- this is a comment with call()
            end
            """;

        var symbols = SymbolExtractor.Extract(1, "lua", content);
        var references = ReferenceExtractor.Extract(1, "lua", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "write");
        Assert.Contains(references, r => r.SymbolName == "insert");
        // Comments should not produce references / コメントは参照を生成しないこと
        Assert.DoesNotContain(references, r => r.SymbolName == "call");
    }

    [Fact]
    public void SupportsLanguage_FSharp_ReturnsTrue()
    {
        // F# primarily uses space-separated syntax, but parenthesized calls
        // (someFunc(x), new ClassName()) are common enough to provide value.
        // F#は主にスペース区切りだが、括弧付き呼び出し（someFunc(x)、new ClassName()）は
        // 十分一般的で、参照抽出の価値がある。
        Assert.True(ReferenceExtractor.SupportsLanguage("fsharp"));
    }

    [Fact]
    public void Extract_SQL_DetectsCallSites()
    {
        const string content = "SELECT COALESCE(name, 'unknown'), LOWER(email)\nFROM users\nWHERE LENGTH(name) > 0\n  AND created_at > NOW()";

        var symbols = SymbolExtractor.Extract(1, "sql", content);
        var references = ReferenceExtractor.Extract(1, "sql", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "COALESCE" && r.ReferenceKind == "call");
        Assert.Contains(references, r => r.SymbolName == "LOWER" && r.ReferenceKind == "call");
        Assert.Contains(references, r => r.SymbolName == "LENGTH" && r.ReferenceKind == "call");
        Assert.Contains(references, r => r.SymbolName == "NOW" && r.ReferenceKind == "call");
        // SQL keywords should not be extracted / SQL キーワードは抽出されないこと
        Assert.DoesNotContain(references, r => r.SymbolName == "SELECT");
        Assert.DoesNotContain(references, r => r.SymbolName == "WHERE");
    }

    [Fact]
    public void Extract_FSharp_DetectsParenthesizedCalls()
    {
        const string content = """
            let validate user =
                printfn("Validating %A" user)
                let result = checkAge(user.Age)
                result
            """;

        var symbols = SymbolExtractor.Extract(1, "fsharp", content);
        var references = ReferenceExtractor.Extract(1, "fsharp", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "printfn" && r.ReferenceKind == "call");
        Assert.Contains(references, r => r.SymbolName == "checkAge" && r.ReferenceKind == "call");
        // F# keywords should not be extracted / F# キーワードは抽出されないこと
        Assert.DoesNotContain(references, r => r.SymbolName == "let");
        Assert.DoesNotContain(references, r => r.SymbolName == "match");
    }

    [Fact]
    public void Extract_R_DetectsCallSites()
    {
        const string content = """
            process_data <- function(df) {
                result <- filter(df, x > 0)
                mean(result$value)
                # This is a comment with fake_call()
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "r", content);
        var references = ReferenceExtractor.Extract(1, "r", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "filter" && r.ReferenceKind == "call");
        Assert.Contains(references, r => r.SymbolName == "mean" && r.ReferenceKind == "call");
        // Comments should not produce references / コメントは参照を生成しないこと
        Assert.DoesNotContain(references, r => r.SymbolName == "fake_call");
    }

    [Fact]
    public void Extract_PowerShell_DetectsCallSites()
    {
        const string content = """
            function Deploy-App {
                param([string]$Path)
                $items = Get-ChildItem($Path)
                Write-Host("Deploying...")
                # comment with FakeCall()
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "powershell", content);
        var references = ReferenceExtractor.Extract(1, "powershell", content, symbols);

        // PowerShell cmdlets like Get-ChildItem are split by hyphen — the call regex captures "ChildItem"
        // PowerShell コマンドレットはハイフンで分割される — CallRegex は "ChildItem" を捕捉
        Assert.Contains(references, r => r.SymbolName == "ChildItem" && r.ReferenceKind == "call");
        Assert.Contains(references, r => r.SymbolName == "Host" && r.ReferenceKind == "call");
        // Comments should not produce references / コメントは参照を生成しないこと
        Assert.DoesNotContain(references, r => r.SymbolName == "FakeCall");
    }

    [Fact]
    public void Extract_Haskell_DetectsParenthesizedCalls()
    {
        // Note: Haskell space-separated calls (e.g. `map f xs`) are not captured — only parenthesized calls.
        // Haskell のスペース区切り呼び出し（例: `map f xs`）は抽出できない。括弧付き呼び出しのみ。
        const string content = """
            process :: [Int] -> Int
            process xs = sum(filter(even)(xs))
            compute y = transform(y) + calculate(y)
            -- this is a comment with fakeCall()
            """;

        var symbols = SymbolExtractor.Extract(1, "haskell", content);
        var references = ReferenceExtractor.Extract(1, "haskell", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "sum" && r.ReferenceKind == "call");
        Assert.Contains(references, r => r.SymbolName == "filter" && r.ReferenceKind == "call");
        Assert.Contains(references, r => r.SymbolName == "transform" && r.ReferenceKind == "call");
        Assert.Contains(references, r => r.SymbolName == "calculate" && r.ReferenceKind == "call");
        // Comments should not produce references / コメントは参照を生成しないこと
        Assert.DoesNotContain(references, r => r.SymbolName == "fakeCall");
    }

    [Fact]
    public void Extract_PythonTripleQuotedString_DoesNotLeakPhantomCallReferences()
    {
        // Regression for issue #291: call-looking identifiers inside a Python
        // triple-quoted string (""" or ''') must not be captured as references.
        // issue #291 回帰: Python の三重引用符文字列（""" や '''）の内側にある
        // 呼び出しらしい識別子は参照として抽出してはならない。
        const string content = """"
            def caller():
                fixture_double = """
                    run_task()
                    other_fake()
                """
                fixture_single = '''
                    more_fake()
                '''
                raw_fixture = r"""
                    raw_fake()
                """
                real_call()

            def real_call():
                pass
            """";

        var symbols = SymbolExtractor.Extract(1, "python", content);
        var references = ReferenceExtractor.Extract(1, "python", content, symbols);

        Assert.DoesNotContain(references, r => r.SymbolName == "run_task");
        Assert.DoesNotContain(references, r => r.SymbolName == "other_fake");
        Assert.DoesNotContain(references, r => r.SymbolName == "more_fake");
        Assert.DoesNotContain(references, r => r.SymbolName == "raw_fake");
        Assert.Contains(references, r => r.SymbolName == "real_call" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_RustRawString_DoesNotLeakPhantomCallReferences()
    {
        // Regression for issue #291: call-looking identifiers inside a Rust raw
        // string (r#"..."#, r##"..."##, …) must not be captured as references.
        // issue #291 回帰: Rust raw string（r#"..."#, r##"..."##, …）の内側にある
        // 呼び出しらしい識別子は参照として抽出してはならない。
        const string content = "fn caller() {\n"
            + "    let basic = r#\"\n"
            + "        fake_basic()\n"
            + "    \"#;\n"
            + "    let nested = r##\"\n"
            + "        contains \"# marker\n"
            + "        fake_nested()\n"
            + "    \"##;\n"
            + "    real_call();\n"
            + "}\n"
            + "fn real_call() {}\n";

        var symbols = SymbolExtractor.Extract(1, "rust", content);
        var references = ReferenceExtractor.Extract(1, "rust", content, symbols);

        Assert.DoesNotContain(references, r => r.SymbolName == "fake_basic");
        Assert.DoesNotContain(references, r => r.SymbolName == "fake_nested");
        Assert.Contains(references, r => r.SymbolName == "real_call" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsTemplateLiteral_DoesNotLeakPhantomCallsButKeepsInterpolationCalls()
    {
        // Regression for issue #291: multi-line JS/TS template literal bodies must
        // not produce phantom references, but ${...} interpolation holes must keep
        // real call edges.
        // issue #291 回帰: 複数行 JS/TS テンプレートリテラルの本体は phantom 参照を
        // 生成しないが、${...} 補間ホール内の本物の呼び出しは参照として残ること。
        const string content = """
            function caller() {
                const src = `
                multi-line fixture:
                    fakeInBody()
                and ${runTask()} mid
                then ${"fakeInString()"} nope
                done ${nested(`${deepReal()}`)} end
                `;
                realCall();
            }

            function runTask() {}
            function realCall() {}
            function nested(_) {}
            function deepReal() {}
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.DoesNotContain(references, r => r.SymbolName == "fakeInBody");
        Assert.DoesNotContain(references, r => r.SymbolName == "fakeInString");
        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "nested" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "deepReal" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_PythonFStringHoleKeepsRealCallReference()
    {
        // Regression for issue #291 follow-up: `f"""..."""` interpolation holes must
        // preserve call identifiers so the real call edge is not silently dropped.
        // `{{` / `}}` remain escaped literal braces and must not open a hole.
        // issue #291 続編: `f"""..."""` の補間ホール内は call 識別子を残し、
        // real call エッジを黙って落とさないこと。`{{` / `}}` は escape で、
        // ホールを開かないこと。
        const string content = """"
            def caller():
                msg = f"""
                literal {{ not a hole
                value: {real_call()} trailing
                done
                """
                tail()

            def real_call():
                pass

            def tail():
                pass
            """";

        var symbols = SymbolExtractor.Extract(1, "python", content);
        var references = ReferenceExtractor.Extract(1, "python", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "real_call" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "tail" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsTemplateLiteral_RegexLiteralDoesNotBreakMaskerScan()
    {
        // Regression for issue #291 follow-up: a JS regex literal whose body contains
        // backticks or `}` must not confuse the template-literal / hole scanners.
        // The outer `/`/` regex must not open a phantom template (so `caller` and
        // `realCall` stay visible), and `${/}/.test(value) ? runTask() : fallback()}`
        // must not end the interpolation hole at the `}` that lives inside the regex,
        // which would otherwise drop `runTask` / `fallback` from the reference graph.
        // issue #291 続編: backtick や `}` を含む regex literal が template / hole の
        // scanner を誤作動させず、`/`/` で phantom テンプレートが開いたり regex 中の
        // `}` で hole が閉じたりしないこと。
        const string content = """
            function caller(value) {
                const tickMatch = /`/.test(value);
                const branch = `result: ${/}/.test(value) ? runTask() : fallback()}`;
                realCall();
                return tickMatch || branch;
            }

            function runTask() {}
            function fallback() {}
            function realCall() {}
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "caller");
        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "fallback" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsRegexAfterReturnKeyword_IsTreatedAsRegexNotDivision()
    {
        // Regression for issue #291 follow-up: `return /regex/` must be recognised as
        // a regex literal even though the preceding token ends in an identifier-part
        // character (the `n` of `return`). A prev-char-only heuristic would incorrectly
        // treat the `/` as division and leave the remainder (including any backticks
        // or `}` inside the regex body) unscanned, which could drop `realAfter` from
        // the reference graph.
        // issue #291 続編: `return /regex/` は直前トークン末尾が識別子文字 (`n`) でも
        // regex として扱うこと。prev 文字だけで判定すると division 扱いになり、
        // regex 内の backtick や `}` がそのまま後続走査に入り、`realAfter` 等の
        // 参照エッジを落とし得る。
        const string content = """
            function caller(value) {
                const ok = (() => { return /`/.test(value) ? 1 : 0; })();
                realAfter();
                return ok;
            }

            function realAfter() {}
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "caller");
        Assert.Contains(references, r => r.SymbolName == "realAfter" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsTemplateHoleReturnRegex_PreservesFollowingCalls()
    {
        // Regression for issue #291 follow-up: inside a template hole, a `return`
        // followed by a regex whose body contains `}` must be skipped as a regex
        // literal — otherwise the `}` inside the regex prematurely closes the hole
        // and later `runTask` / `fallback` / `realCall` references can be dropped.
        // issue #291 続編: template hole 内で `return /}/` のような regex を正しく
        // regex としてスキップし、regex 内の `}` で hole を早く閉じないこと。
        const string content = """
            function caller(value) {
                const branch = `${(() => { return /}/.test(value) ? runTask() : fallback(); })()}`;
                realCall();
                return branch;
            }

            function runTask() {}
            function fallback() {}
            function realCall() {}
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "fallback" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_RustRawStringInsideBlockComment_DoesNotMaskFollowingCode()
    {
        // Regression for issue #291 follow-up: `r#"` inside a `/* ... */` Rust block
        // comment must not be treated as a real raw-string opener — otherwise the
        // masker stays in raw-string mode for the rest of the file and every
        // subsequent `fn` / `struct` / `impl` call site is wiped out. Nested block
        // comments and block comments that span multiple lines must behave the
        // same way.
        // issue #291 続編: Rust の `/* ... */` ブロックコメント内に現れる `r#"` を
        // 本物の raw-string 開始扱いしないこと。さもないと以降の `fn` / `struct` /
        // `impl` が全部マスクされて参照グラフから落ちる。ネストしたブロックコメント
        // や複数行に渡るコメントでも同様であること。
        const string content = """
            /* raw marker r#" stays in comment only */
            fn caller() {
                real_fn();
            }

            /* outer /* inner r#" still inert */ outer again */
            fn trailing_caller() {
                trailing_real();
            }

            fn real_fn() {}
            fn trailing_real() {}
            """;

        var symbols = SymbolExtractor.Extract(1, "rust", content);
        var references = ReferenceExtractor.Extract(1, "rust", content, symbols);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "caller");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "trailing_caller");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "real_fn");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "trailing_real");
        Assert.Contains(references, r => r.SymbolName == "real_fn" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "trailing_real" && r.ContainerName == "trailing_caller");
    }

    [Fact]
    public void Extract_JsTemplateHoleMultilineDivisionContinuation_PreservesCallAttribution()
    {
        // Regression for issue #291 follow-up: when a template-literal `${...}` hole
        // wraps a multi-line expression that continues with a leading `/` on the next
        // line (division, not regex), the masker must not lose lexer state at the
        // line boundary. Otherwise the `/` gets reclassified as a regex literal and
        // swallows the hole-closing `}` and the backtick, collapsing the caller's
        // body range and dropping `runTask` / `realCall` from the reference graph.
        // issue #291 続編: `${...}` ホール内で行をまたぐ division `/` が、次行頭に
        // 置かれた場合に lexState をまたげず regex 扱いされて hole と backtick を
        // 食いつぶし、body 範囲が潰れて `runTask` / `realCall` が落ちないこと。
        const string content = """
            function caller(value) {
                const branch = `${value
                    / 2 + runTask()}`;
                realCall();
                return branch;
            }

            function runTask() {}
            function realCall() {}
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "caller");
        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsTemplateHoleInnerObjectClose_TreatsFollowingSlashAsDivision()
    {
        // Regression for issue #291 follow-up: when a template-literal hole contains an
        // object literal or block close `}` followed by `/`, the masker must classify
        // the `/` as division (not regex). Otherwise the regex scanner swallows the
        // hole-closing `}` and backtick, collapsing the caller body and dropping
        // subsequent real-code references.
        // issue #291 続編: テンプレート hole 内で object literal / block 閉じの `}` の
        // 次に来る `/` は division として扱う必要がある。regex 扱いしてしまうと閉じ `}`
        // と backtick を巻き込み、caller 本体が潰れて実コードの参照が落ちる。
        const string content = """
            function caller() {
                const branch = `${({ value: 1 }
                    / 2) + runTask()}`;
                realCall();
                return branch;
            }

            function runTask() {}
            function realCall() {}
            """;

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "caller");
        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_PythonFStringHole_NestedTripleQuotedStringDoesNotCloseHole()
    {
        // Regression for issue #291 follow-up: Python f-string holes can legally contain
        // triple-quoted string literals that span multiple lines. Any `}` inside that
        // nested string must not close the outer hole; otherwise the masker masks real
        // code after the f-string and drops references.
        // issue #291 続編: Python の f-string hole 内には複数行にわたる三重引用符文字列を
        // 入れられる。その内部の `}` は外側の hole を閉じてはならない。さもないと
        // f-string 以降の実コードがマスクされ、参照が落ちる。
        const string content = "def caller():\n"
            + "    msg = f\"\"\"\n"
            + "    {len('''\n"
            + "}\n"
            + "''') + real_call()}\n"
            + "    \"\"\"\n"
            + "    tail()\n"
            + "\n"
            + "def real_call():\n"
            + "    pass\n"
            + "\n"
            + "def tail():\n"
            + "    pass\n";

        var symbols = SymbolExtractor.Extract(1, "python", content);
        var references = ReferenceExtractor.Extract(1, "python", content, symbols);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "caller");
        Assert.Contains(references, r => r.SymbolName == "real_call" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "tail" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsIfStatementParenRegexLiteral_DoesNotMaskFollowingCode()
    {
        // Regression for issue #291 follow-up: after an `if (value)` paren close,
        // `/.../` must parse as a regex literal, not division. Otherwise the masker
        // mistakes the trailing `/` for division, swallows the backtick inside the
        // regex body as a phantom template opener, and erases the real call after.
        // issue #291 続編: `if (value)` の `)` 直後の `/.../` は regex literal と
        // して扱うこと。division 扱いすると regex 本文の backtick を template 開始
        // と誤認して後続コードを潰し、実呼び出しが参照として残らない。
        const string content = "function caller(value) {\n"
            + "    if (value) /`/.test(value);\n"
            + "    realCall(value);\n"
            + "}\n"
            + "function realCall(x) { return x; }\n";

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsTemplateHoleIfParenRegexLiteral_PreservesFollowingCall()
    {
        // Regression for issue #291 follow-up: inside a template literal hole, the
        // same statement-head paren + regex literal pattern must also classify `/`
        // as regex. Otherwise the inner call `runTask()` after the regex is dropped
        // because the hole's backtick-containing regex is misread as a template.
        // issue #291 続編: テンプレートホール内でも同じく statement-head paren +
        // regex の組み合わせで `/` を regex として扱うこと。そうしないと regex 内
        // の backtick を template 開始と取り違え、続く `runTask()` を消してしまう。
        const string content = "function caller(value) {\n"
            + "    const s = `${(() => { if (value) /`/.test(value); runTask(); return 1; })()}`;\n"
            + "    realCall();\n"
            + "}\n"
            + "function runTask() {}\n"
            + "function realCall() {}\n";

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_PythonNestedFStringInnerHoleStringLiteralWithBrace_PreservesInnerCall()
    {
        // Regression for issue #291 follow-up: inside the inner hole of a nested
        // single-line f-string, a string literal containing `}` (e.g. `'}'`) must
        // not close the inner hole prematurely. The inner-hole scanner must skip
        // over string literals the same way the outer hole scanner does.
        // issue #291 続編: ネスト単行 f-string の inner hole 内で、`}` を含む単行
        // 文字列リテラルにより inner hole が早閉じしないこと。inner hole も outer
        // hole と同じく文字列リテラルをスキップする必要がある。
        const string content = "def caller():\n"
            + "    msg = f\"\"\"\n"
            + "    {format(f\"{prefix('}') + real_call()}\")}\n"
            + "    \"\"\"\n"
            + "    tail()\n"
            + "\n"
            + "def prefix(_):\n"
            + "    return \"\"\n"
            + "\n"
            + "def real_call():\n"
            + "    pass\n"
            + "\n"
            + "def tail():\n"
            + "    pass\n";

        var symbols = SymbolExtractor.Extract(1, "python", content);
        var references = ReferenceExtractor.Extract(1, "python", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "real_call" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "tail" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_PythonFStringHole_NestedSingleLineFStringPreservesInnerCall()
    {
        // Regression for issue #291 follow-up: a nested single-line f-string inside an
        // outer f-string hole, e.g. `f"{format(f"{real_call()}")}"`, must preserve the
        // inner call edge. The masker must actively blank the inner quotes so that
        // ReferenceExtractor's single-line StringLiteralRegex does not strip the hole
        // expression along with the string literal.
        // issue #291 続編: 外側 f-string ホール内のネスト単行 f-string
        // (`f"{format(f"{real_call()}")}"`) で、内側ホールの call edge を残すこと。
        // PrepareLine が単行文字列全体を消し去らないよう、masker 側で quote を
        // 空白化する必要がある。
        const string content = "def caller():\n"
            + "    msg = f\"\"\"\n"
            + "    {format(f\"{real_call()}\")}\n"
            + "    \"\"\"\n"
            + "    tail()\n"
            + "\n"
            + "def real_call():\n"
            + "    pass\n"
            + "\n"
            + "def tail():\n"
            + "    pass\n";

        var symbols = SymbolExtractor.Extract(1, "python", content);
        var references = ReferenceExtractor.Extract(1, "python", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "real_call" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "tail" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsBlockCloseThenRegexLiteral_DoesNotMaskFollowingCode()
    {
        // Regression for issue #291 follow-up: after a top-level block `{}` close,
        // `}` must remain regex-legal so that a following `/.../ .test(...)` is parsed
        // as a regex literal rather than division. Otherwise the masker mistakes the
        // trailing `/` for the start of a division operand, swallows the backtick
        // following it, and erases the actual method invocation.
        // issue #291 続編: トップレベルのブロック `{}` 直後の `}` を regex-legal に
        // 残し、`/.../` が regex literal として解釈されること。division 扱いされると
        // バッククォートを template literal 開始と誤認して後続コードを潰す。
        const string content = "function main(value) {\n"
            + "    if (value) { }\n"
            + "    /`/.test(value);\n"
            + "    realCall(value);\n"
            + "}\n"
            + "function realCall(x) { return x; }\n";

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "main");
    }

    [Fact]
    public void Extract_JsTemplateHoleArrowBodyBlockCloseThenRegex_PreservesFollowingCall()
    {
        // Regression for issue #291 follow-up: inside a template literal hole, an
        // arrow function body's inner `{}` must not flip subsequent `/.../` into
        // division. The closing `}` of an arrow-body block is a statement-list close,
        // so the following `/regex/` is a regex literal — treating it as division
        // causes the scanner to absorb the backtick after `.test(value)` and erase
        // the real code that follows.
        // issue #291 続編: テンプレートリテラルホール内で arrow function 本体の
        // ブロック `{}` 直後の `/.../` を regex literal として扱うこと。arrow body の
        // `}` はステートメント閉じなので、続く `/` は regex の開始であり division
        // ではない。division 扱いすると後続コードを潰してしまう。
        const string content = "function main(value) {\n"
            + "    const s = `pre ${(() => { if (value) { } /`/.test(value); realInner(value); return 1; })()} post`;\n"
            + "    return s;\n"
            + "}\n"
            + "function realInner(x) { return x; }\n";

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "realInner" && r.ContainerName == "main");
    }

    [Fact]
    public void Extract_PythonFStringHole_StringLiteralWithBraceDoesNotCloseHole()
    {
        // Regression for issue #291 follow-up: inside an f-string `{expr}` hole,
        // a nested Python string literal containing `}` must not terminate the hole
        // (or leave the outer triple-quoted f-string scanner in the wrong state).
        // The expression should still be preserved, so `real_call` remains a real
        // reference edge and `tail` (outside the string entirely) stays visible.
        // issue #291 続編: f-string ホール内のネストした文字列リテラル中の `}` で
        // ホールが閉じないこと。式本体は残り、`real_call` は参照に、`tail` は
        // ホール外として見えること。
        const string content = """"
            def caller():
                msg = f"""
                {prefix("}") + real_call()}
                """
                tail()

            def prefix(_):
                return ""

            def real_call():
                pass

            def tail():
                pass
            """";

        var symbols = SymbolExtractor.Extract(1, "python", content);
        var references = ReferenceExtractor.Extract(1, "python", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "prefix" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "real_call" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "tail" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsTemplateHolePostfixIncrementDivision_PreservesFollowingCall()
    {
        // Regression for issue #291 follow-up: postfix `++` inside a template hole
        // produces a numeric value, so the next `/` must be division, not a regex
        // literal. Otherwise `/ 2 + runTask()` is swallowed as a regex body and
        // the `runTask` edge disappears.
        // issue #291 続編: テンプレートホール内の postfix `++` は数値を生むため、
        // 直後の `/` は division として扱う必要がある。regex と誤判定すると
        // `/ 2 + runTask()` が regex 本体として消費され、`runTask` 参照が失われる。
        const string content = "function caller(counter) {\n"
            + "    const branch = `${counter++ / 2 + runTask()}`;\n"
            + "    realCall();\n"
            + "    return branch;\n"
            + "}\n"
            + "function runTask() { return 1; }\n"
            + "function realCall() {}\n";

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsTemplateHolePostfixDecrementDivision_PreservesFollowingCall()
    {
        // Same regression for postfix `--`: numeric-typed operand, so `/` after it
        // must be division. Covers the second 2-char-token path in AdvanceJsToken.
        // postfix `--` も同様に数値を生み、続く `/` は division 扱い。
        // `AdvanceJsToken` の 2 文字 token 経路のもう 1 本を確認する。
        const string content = "function caller(counter) {\n"
            + "    const branch = `${counter-- / 2 + runTask()}`;\n"
            + "    realCall();\n"
            + "    return branch;\n"
            + "}\n"
            + "function runTask() { return 1; }\n"
            + "function realCall() {}\n";

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_PythonOuterFStringHoleNestedTripleFString_PreservesInnerCall()
    {
        // Regression for issue #291 follow-up: a nested triple-quoted f-string
        // `f"""...{real_call()}..."""` placed inside an outer f-string hole must
        // preserve the inner hole expression. Previously the nested triple's
        // f-string flag was discarded and its body was blanked wholesale,
        // erasing `real_call` from the reference graph.
        // issue #291 続編: 外側 f-string ホール内のネスト三重引用符 f-string
        // `f"""...{real_call()}..."""` で内側 hole の式本体を残すこと。従来は
        // ネスト三重の f 接頭辞を捨てて本体を全面空白化していたため、内側の
        // `real_call` が参照グラフから消えていた。
        const string content = "def caller():\n"
            + "    msg = f\"\"\"\n"
            + "    {format(f\"\"\"{real_call()}\"\"\")}\n"
            + "    \"\"\"\n"
            + "    tail()\n"
            + "\n"
            + "def format(_):\n"
            + "    return \"\"\n"
            + "\n"
            + "def real_call():\n"
            + "    pass\n"
            + "\n"
            + "def tail():\n"
            + "    pass\n";

        var symbols = SymbolExtractor.Extract(1, "python", content);
        var references = ReferenceExtractor.Extract(1, "python", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "real_call" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "format" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "tail" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsTemplateHoleClassBodyThenRegex_PreservesFollowingCall()
    {
        // Regression for issue #291 follow-up: `class Local {}` inside a template
        // hole must open a statement block, not an expression brace. Otherwise
        // the matching `}` classifies the following `/regex/` as division, which
        // stops the regex skipper from consuming its body and lets a backtick
        // inside the regex look like a phantom template opener — erasing the
        // call edge to `runTask` that follows the regex.
        // issue #291 続編: テンプレートホール内の `class Local {}` は
        // expression brace ではなく statement block として開かせる必要がある。
        // そうしないと閉じ `}` 以降の `/regex/` が division と誤判定され、
        // regex 本文中の backtick が phantom template として読まれて、その後の
        // `runTask` 参照が消えてしまう。
        const string content = "function caller(value) {\n"
            + "    const s = `${(() => { class Local {} /`/.test(value); runTask(); return 1; })()}`;\n"
            + "    realCall();\n"
            + "}\n"
            + "function runTask() {}\n"
            + "function realCall() {}\n";

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsTemplateHoleOptionalCatchBlockThenRegex_PreservesFollowingCall()
    {
        // Regression for issue #291 follow-up: ES2019 optional-binding `catch {}`
        // inside a template hole must open a statement block, not an expression
        // brace. Otherwise the matching `}` flips `/regex/` into division,
        // swallows the regex body, and a backtick inside the regex is read as a
        // phantom template opener — erasing the `runTask` edge that follows.
        // issue #291 続編: ES2019 の optional binding 付き `catch {}` が
        // テンプレートホール内で statement block として開くこと。expression
        // brace として扱うと直後の `/regex/` が division になり、regex 本文
        // 中の backtick が phantom template opener と解釈され、その後の
        // `runTask` 参照が失われる。
        const string content = "function caller(value) {\n"
            + "    const s = `${(() => { try { risky(); } catch { } /`/.test(value); runTask(); return 1; })()}`;\n"
            + "    realCall();\n"
            + "}\n"
            + "function risky() {}\n"
            + "function runTask() {}\n"
            + "function realCall() {}\n";

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsTemplateHoleSwitchCaseBlockThenRegex_PreservesFollowingCall()
    {
        // Regression for issue #291 follow-up: `case N: { }` / `default: { }`
        // inside a template hole must open a statement block, not an expression
        // brace. The case-label colon is recognized via a one-shot hint set on
        // `:` at paren depth 0 after a `case` / `default` keyword.
        // issue #291 続編: テンプレートホール内の `case N: { }` / `default: { }`
        // が statement block として開くこと。case ラベル終端の `:` は
        // `case` / `default` 後かつ paren 深さ 0 のときだけ hint を立てる。
        const string content = "function caller(value) {\n"
            + "    const s = `${(() => { switch (value) { case 1: { } /`/.test(value); runTask(); return 1; } })()}`;\n"
            + "    realCall();\n"
            + "}\n"
            + "function runTask() {}\n"
            + "function realCall() {}\n";

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }

    [Fact]
    public void Extract_JsTemplateHoleSwitchDefaultBlockThenRegex_PreservesFollowingCall()
    {
        // Same regression for `default: { }` inside a template hole.
        // テンプレートホール内の `default: { }` についても同じ回帰。
        const string content = "function caller(value) {\n"
            + "    const s = `${(() => { switch (value) { default: { } /`/.test(value); runTask(); return 1; } })()}`;\n"
            + "    realCall();\n"
            + "}\n"
            + "function runTask() {}\n"
            + "function realCall() {}\n";

        var symbols = SymbolExtractor.Extract(1, "javascript", content);
        var references = ReferenceExtractor.Extract(1, "javascript", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "runTask" && r.ContainerName == "caller");
        Assert.Contains(references, r => r.SymbolName == "realCall" && r.ContainerName == "caller");
    }
}
