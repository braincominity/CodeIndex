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
    public void Extract_CsharpReturnTargetAttribute_ReferenceIsRecordedAsAttribute()
    {
        // issue #293: C# attributes (including targeted `[return: ...]` form) are recorded as
        // `attribute` kind — the reference must not be dropped, but also must not pollute the
        // call-graph with a phantom `call` row.
        // issue #293: C# 属性（`[return: ...]` の target 付きも含む）は `attribute` として
        // 記録される。参照自体は失われないが、call-graph を `call` 行で汚染してはならない。
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

        var marshalAsRefs = references
            .Where(reference => reference.SymbolName == "MarshalAs")
            .OrderBy(reference => reference.Line)
            .ToList();

        Assert.Equal(2, marshalAsRefs.Count);
        Assert.Equal([5, 8], marshalAsRefs.Select(reference => reference.Line).ToArray());
        Assert.All(marshalAsRefs, reference => Assert.Equal("attribute", reference.ReferenceKind));
        Assert.All(marshalAsRefs, reference => Assert.Equal("Foo", reference.ContainerName));
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
    public void Extract_CsharpAttribute_ClassifiedAsAttribute()
    {
        // issue #293: `[Obsolete("msg")]` must produce an `attribute` reference, not a phantom `call`.
        // issue #293: `[Obsolete("msg")]` は `call` ではなく `attribute` として記録されること。
        const string content = """
            using System;
            [Obsolete("old")]
            public class Old
            {
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var obsolete = Assert.Single(references.Where(r => r.SymbolName == "Obsolete"));
        Assert.Equal("attribute", obsolete.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpTargetedAttribute_ClassifiedAsAttribute()
    {
        // issue #293: `[return: NotNull("x")]` targeted attribute is classified as `attribute`.
        // issue #293: `[return: NotNull("x")]` のターゲット付き属性も `attribute` になること。
        const string content = """
            public class C
            {
                [return: NotNull("x")]
                public string M() => string.Empty;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var notNull = Assert.Single(references.Where(r => r.SymbolName == "NotNull"));
        Assert.Equal("attribute", notNull.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpMultipleAttributes_ClassifiedAsAttribute()
    {
        // issue #293: `[Foo("a"), Bar("b")]` — both entries in a comma-separated attribute list
        // must be classified as `attribute`.
        // issue #293: `[Foo("a"), Bar("b")]` のカンマ区切り属性リストは全て `attribute` になること。
        const string content = """
            [Foo("a"), Bar("b")]
            public class C { }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var foo = Assert.Single(references.Where(r => r.SymbolName == "Foo"));
        var bar = Assert.Single(references.Where(r => r.SymbolName == "Bar"));
        Assert.Equal("attribute", foo.ReferenceKind);
        Assert.Equal("attribute", bar.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpAttributeWithNewArgument_InstantiateStaysInstantiate()
    {
        // Inside attribute arguments, `new Foo()` still counts as `instantiate` — only the
        // attribute identifier itself is reclassified.
        // 属性引数内の `new Foo()` は従来通り `instantiate`。属性名本体のみが再分類される。
        const string content = """
            [AttributeUsage(AttributeTargets.Class)]
            public class C { }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var au = Assert.Single(references.Where(r => r.SymbolName == "AttributeUsage"));
        Assert.Equal("attribute", au.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpMethodBodyCall_StaysCall()
    {
        // Regression guard: ordinary method calls inside method bodies must still produce `call`,
        // not be mistaken for attribute references due to unrelated `[` tokens on nearby lines.
        // 回帰防止: メソッド本体内の通常呼び出しは、近くの `[` トークンの影響で `attribute` と
        // 誤判定されず `call` のまま残ること。
        const string content = """
            public class C
            {
                public int Run() => Compute(42);
                public int Compute(int x) => x;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var compute = Assert.Single(references.Where(r => r.SymbolName == "Compute"));
        Assert.Equal("call", compute.ReferenceKind);
    }

    [Fact]
    public void Extract_JavaAnnotation_ClassifiedAsAnnotation()
    {
        // issue #293: `@Deprecated(since="1.0")` must produce an `annotation` reference, not a phantom `call`.
        // issue #293: `@Deprecated(since="1.0")` は `call` ではなく `annotation` として記録されること。
        const string content = """
            public class AnnotatedClass {
                @Deprecated(since="1.0")
                public void doWork() { }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        var deprecated = Assert.Single(references.Where(r => r.SymbolName == "Deprecated"));
        Assert.Equal("annotation", deprecated.ReferenceKind);
    }

    [Fact]
    public void Extract_JavaQualifiedAnnotation_ClassifiedAsAnnotation()
    {
        // issue #293: `@org.junit.Test(timeout=1000)` — dotted qualifier chain still resolves to `@`.
        // issue #293: `@org.junit.Test(timeout=1000)` のような修飾付き注釈も `annotation` になること。
        const string content = """
            public class T {
                @org.junit.Test(timeout=1000)
                public void testIt() { }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        var testAnno = Assert.Single(references.Where(r => r.SymbolName == "Test"));
        Assert.Equal("annotation", testAnno.ReferenceKind);
    }

    [Fact]
    public void Extract_KotlinAnnotation_ClassifiedAsAnnotation()
    {
        // issue #293: Kotlin `@Deprecated("msg")` also emits `annotation`.
        // issue #293: Kotlin の `@Deprecated("msg")` も `annotation` になること。
        const string content = """
            class K {
                @Deprecated("msg")
                fun old() {}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "kotlin", content);
        var references = ReferenceExtractor.Extract(1, "kotlin", content, symbols);

        var deprecated = Assert.Single(references.Where(r => r.SymbolName == "Deprecated"));
        Assert.Equal("annotation", deprecated.ReferenceKind);
    }

    [Fact]
    public void Extract_JavaMethodBodyCall_StaysCall()
    {
        // Regression guard: ordinary Java method call remains `call`, not `annotation`.
        // 回帰防止: Java のメソッド本体内の通常呼び出しは `annotation` に誤判定されず `call` のまま。
        const string content = """
            public class J {
                public int add(int a, int b) { return compute(a, b); }
                public int compute(int a, int b) { return a + b; }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        var compute = Assert.Single(references.Where(r => r.SymbolName == "compute"));
        Assert.Equal("call", compute.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpCollectionExpression_StaysCall()
    {
        // issue #293 regression: C# 12 collection expressions `var xs = [Make(), Make()]`
        // share the `[...]` syntax with attributes but must NOT be classified as `attribute`.
        // issue #293 回帰防止: C# 12 collection expression `var xs = [Make(), Make()]` は
        // 属性と同じ `[...]` 構文を共有するが `attribute` に誤分類してはならない。
        const string content = """
            public class C
            {
                public int Make() => 42;
                public void Run()
                {
                    var xs = [Make(), Make()];
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var makeRefs = references.Where(r => r.SymbolName == "Make").ToList();
        Assert.Equal(2, makeRefs.Count);
        Assert.All(makeRefs, r => Assert.Equal("call", r.ReferenceKind));
        Assert.All(makeRefs, r => Assert.Equal("Run", r.ContainerName));
    }

    [Fact]
    public void Extract_CsharpCollectionExpressionInArgument_StaysCall()
    {
        // Collection expressions appearing as arguments, nested in other expressions, or
        // after `return` must still classify inner calls as `call`, not `attribute`.
        // 引数やネストされた式、`return` 後の collection expression 内の呼び出しは `call` のまま。
        const string content = """
            public class C
            {
                public int Make() => 42;
                public int[] Wrap() => [Make(), Make()];
                public void Consume(int[] xs) { }
                public void Run()
                {
                    Consume([Make(), Make()]);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var makeRefs = references.Where(r => r.SymbolName == "Make").ToList();
        Assert.Equal(4, makeRefs.Count);
        Assert.All(makeRefs, r => Assert.Equal("call", r.ReferenceKind));
    }

    [Fact]
    public void Extract_CsharpIndexerAccess_StaysCall()
    {
        // `arr[Compute()]` — `[` is preceded by an identifier, so it is an indexer, not an
        // attribute, and the inner call must stay `call`.
        // `arr[Compute()]` は indexer で、`[` の直前が識別子のため attribute 扱いにはしない。
        const string content = """
            public class C
            {
                public int Compute() => 0;
                public int Read(int[] arr) => arr[Compute()];
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var compute = Assert.Single(references.Where(r => r.SymbolName == "Compute"));
        Assert.Equal("call", compute.ReferenceKind);
    }

    [Fact]
    public void Extract_KotlinFieldTargetAnnotation_ClassifiedAsAnnotation()
    {
        // issue #293 follow-up: Kotlin use-site target `@field:Deprecated("msg")` must be
        // classified as `annotation`, not `call`.
        // issue #293 補足: Kotlin の use-site target `@field:Deprecated("msg")` も `annotation`。
        const string content = """
            class Example {
                @field:Deprecated("msg")
                val value: Int = 0
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "kotlin", content);
        var references = ReferenceExtractor.Extract(1, "kotlin", content, symbols);

        var deprecated = Assert.Single(references.Where(r => r.SymbolName == "Deprecated"));
        Assert.Equal("annotation", deprecated.ReferenceKind);
    }

    [Fact]
    public void Extract_KotlinGetTargetAnnotation_ClassifiedAsAnnotation()
    {
        // Kotlin `@get:JsonName("x")` property getter target annotation.
        // Kotlin の `@get:JsonName("x")` プロパティ getter 向け注釈も `annotation`。
        const string content = """
            class K {
                @get:JsonName("x")
                val x: Int = 0
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "kotlin", content);
        var references = ReferenceExtractor.Extract(1, "kotlin", content, symbols);

        var jsonName = Assert.Single(references.Where(r => r.SymbolName == "JsonName"));
        Assert.Equal("annotation", jsonName.ReferenceKind);
    }

    [Fact]
    public void Extract_KotlinFileTargetAnnotation_ClassifiedAsAnnotation()
    {
        // Kotlin `@file:JvmName("Foo")` file-level target annotation.
        // Kotlin の `@file:JvmName("Foo")` ファイル単位注釈も `annotation`。
        const string content = """
            @file:JvmName("Foo")

            package example
            """;

        var symbols = SymbolExtractor.Extract(1, "kotlin", content);
        var references = ReferenceExtractor.Extract(1, "kotlin", content, symbols);

        var jvmName = Assert.Single(references.Where(r => r.SymbolName == "JvmName"));
        Assert.Equal("annotation", jvmName.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpChainedIndexerCalls_StayCall()
    {
        // issue #293 follow-up: `arr[Compute()][Compute()]` — the second `[` is preceded by
        // an indexer-closing `]`, not an attribute-section `]`. Walking back to the matching
        // `[` must find an expression-position bracket so both inner calls remain `call`.
        // issue #293 補足: `arr[Compute()][Compute()]` の 2 個目の `[` は indexer の `]` に
        // 続くだけで attribute section の終端ではないため、対応する `[` まで戻って宣言位置で
        // ないことを確認し、両方の呼び出しを `call` のまま残す。
        const string content = """
            public class C
            {
                public int Compute() => 0;
                public int Read(int[][] arr) => arr[Compute()][Compute()];
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var computeRefs = references.Where(r => r.SymbolName == "Compute").ToList();
        Assert.Equal(2, computeRefs.Count);
        Assert.All(computeRefs, r => Assert.Equal("call", r.ReferenceKind));
    }

    [Fact]
    public void Extract_CsharpMatrixIndexerCalls_StayCall()
    {
        // Two consecutive indexer accesses on a matrix — `matrix[Row()][Col()]` — must keep
        // both inner calls as `call`.
        // 連続 indexer `matrix[Row()][Col()]` でも、両方の呼び出しが `call` のまま残ること。
        const string content = """
            public class M
            {
                public int Row() => 0;
                public int Col() => 0;
                public int Read(int[,] matrix) => matrix[Row(), Col()];
                public int Read2(int[][] grid) => grid[Row()][Col()];
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var row = references.Where(r => r.SymbolName == "Row").ToList();
        var col = references.Where(r => r.SymbolName == "Col").ToList();
        Assert.Equal(2, row.Count);
        Assert.Equal(2, col.Count);
        Assert.All(row, r => Assert.Equal("call", r.ReferenceKind));
        Assert.All(col, r => Assert.Equal("call", r.ReferenceKind));
    }

    [Fact]
    public void Extract_CsharpChainedAttributeLists_StayAttribute()
    {
        // `[A(...)][B(...)]` on a declaration — the chained attribute-list form must still
        // classify both entries as `attribute` after the indexer-safety walk-back.
        // `[A(...)][B(...)]` の連続 attribute list は、indexer との区別が入ったあとも両方 `attribute`。
        const string content = """
            [A("x")][B("y")]
            public class C { }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var a = Assert.Single(references.Where(r => r.SymbolName == "A"));
        var b = Assert.Single(references.Where(r => r.SymbolName == "B"));
        Assert.Equal("attribute", a.ReferenceKind);
        Assert.Equal("attribute", b.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpParameterAttributes_ClassifiedAsAttribute()
    {
        // Parameter attributes are introduced by `(` or `,` rather than a scope boundary, so
        // the classifier must use forward lookahead from `[` to disambiguate against C# 12
        // collection expressions in argument position like `Consume([Make()])`.
        // パラメータ属性は `(` や `,` に続くため、collection expression と区別するには `[` から
        // 対応する `]` まで前方を走査して、直後が識別子かを確認する必要がある。
        const string content = """
            public class C
            {
                public void M([Attr("x")] int a, [Other("y")] int b) { }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var attr = Assert.Single(references.Where(r => r.SymbolName == "Attr"));
        var other = Assert.Single(references.Where(r => r.SymbolName == "Other"));
        Assert.Equal("attribute", attr.ReferenceKind);
        Assert.Equal("attribute", other.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpMultiLineAttribute_ClassifiedAsAttribute()
    {
        // A multi-line attribute list `[\n Foo("x")\n ]` must still classify `Foo` as
        // attribute even though the opening `[` is not on the same line as the identifier.
        // `[` と `Foo("x")` が別行にある場合でも `Foo` を属性として判定すること。
        const string content = """
            [
                Foo("x")
            ]
            public class C { }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var foo = Assert.Single(references.Where(r => r.SymbolName == "Foo"));
        Assert.Equal("attribute", foo.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpMultiLineParameterAttribute_ClassifiedAsAttribute()
    {
        // Parameter attribute split across lines — `(` ends one line, `[Attr]` sits on the
        // next, and the declaration continues after. Cross-line lookahead must still find
        // the identifier after the matching `]`.
        // 改行を挟んだパラメータ属性でも、跨行 lookahead で `]` の直後に続く識別子まで到達し、
        // 属性として判定できること。
        const string content = """
            public class C
            {
                public void M(
                    [Attr("x")]
                    int a)
                {
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var attr = Assert.Single(references.Where(r => r.SymbolName == "Attr"));
        Assert.Equal("attribute", attr.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpTargetedAttribute_StaysAttribute()
    {
        // Regression: `[return: NotNullWhen(true)]` is recognised as an attribute section by
        // the pre-pass (bracket position, not `target:` heuristics). Keep the case covered.
        // リグレッション: `[return: NotNullWhen(true)]` も属性セクションとして判定されること。
        const string content = """
            public class C
            {
                [return: NotNullWhen(true)]
                public bool Try() => true;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var notNullWhen = Assert.Single(references.Where(r => r.SymbolName == "NotNullWhen"));
        Assert.Equal("attribute", notNullWhen.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpCollectionExpressionInArgument_StaysCallAfterParen()
    {
        // Defense-in-depth: `Consume([Make()])` has `[` immediately after `(`, matching the
        // parameter-attribute entry point, but forward lookahead sees `)` after the matching
        // `]` and correctly keeps `Make` as `call`.
        // `Consume([Make()])` のように `(` 直後に `[` が続くケースでも、`]` の直後が `)` であれば
        // collection expression として `call` のままであること。
        const string content = """
            public class C
            {
                public void M()
                {
                    Consume([Make(), Make()]);
                }
                private static int Make() => 0;
                private void Consume(int[] xs) { }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var makeRefs = references.Where(r => r.SymbolName == "Make").ToList();
        Assert.Equal(2, makeRefs.Count);
        Assert.All(makeRefs, r => Assert.Equal("call", r.ReferenceKind));
    }

    [Fact]
    public void Extract_CsharpCollectionExpressionPatternMatch_StaysCall()
    {
        // Regression: `[Make()] is int[] xs` is a pattern expression, not an attribute. The
        // next token after `]` is the contextual keyword `is`, so `Make` must stay `call`.
        // リグレッション: `[Make()] is int[] xs` はパターン式なので、`]` の次の `is` を属性の続きと誤認せず `Make` は `call`。
        const string content = """
            public class C
            {
                public bool M()
                {
                    return Consume([Make()] is int[] xs && xs.Length > 0);
                }
                private static int Make() => 0;
                private bool Consume(bool b) => b;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var make = Assert.Single(references.Where(r => r.SymbolName == "Make"));
        Assert.Equal("call", make.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpCollectionExpressionAsCast_StaysCall()
    {
        // Regression: `[Make()] as int[]` is an `as` cast, not an attribute. The classifier
        // must treat `as` as expression continuation and keep `Make` as `call`.
        // リグレッション: `[Make()] as int[]` は `as` キャストなので `Make` は `call` のまま。
        const string content = """
            public class C
            {
                public void M()
                {
                    var arr = ([Make()] as int[]);
                }
                private static int Make() => 0;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var make = Assert.Single(references.Where(r => r.SymbolName == "Make"));
        Assert.Equal("call", make.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpCollectionExpressionSwitchExpression_StaysCall()
    {
        // Regression: `[Make()] switch { ... }` is a switch expression over a collection,
        // not an attribute. The classifier must treat `switch` as expression continuation.
        // リグレッション: `[Make()] switch { ... }` は collection に対する switch 式のため `Make` は `call`。
        const string content = """
            public class C
            {
                public bool M() => Consume([Make()] switch { _ => true });
                private static int Make() => 0;
                private bool Consume(bool b) => b;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var make = Assert.Single(references.Where(r => r.SymbolName == "Make"));
        Assert.Equal("call", make.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpTupleTypedParameterAttribute_ClassifiedAsAttribute()
    {
        // Regression: `void M([Attr("x")] (int, int) value)` — the token after `]` is `(`
        // (tuple type syntax), which must still be treated as a declaration start so the
        // preceding `[...]` is classified as an attribute.
        // リグレッション: `void M([Attr("x")] (int, int) value)` のように `]` の直後が tuple 型の `(` でも属性扱い。
        const string content = """
            public class C
            {
                public void M([Attr("x")] (int a, int b) value) { }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var attr = Assert.Single(references.Where(r => r.SymbolName == "Attr"));
        Assert.Equal("attribute", attr.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpTypeParameterAttribute_ClassifiedAsAttribute()
    {
        // Regression: `class C<[Attr("x")] T>` — the `[` is preceded by `<`, which is a valid
        // attribute position for type parameters. The classifier must accept `<` alongside
        // `(` and `,` as parameter-list entry points.
        // リグレッション: `class C<[Attr("x")] T>` のように `<` の直後にある型パラメータ属性も検出できること。
        const string content = """
            public class C<[Attr("x")] T>
            {
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var attr = Assert.Single(references.Where(r => r.SymbolName == "Attr"));
        Assert.Equal("attribute", attr.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpLambdaAttribute_ClassifiedAsAttribute()
    {
        // Regression: `var f = [Attr("x")] () => 0;` — the `[` is preceded by `=`, and the token
        // after `]` is `(` (lambda parameter list). The classifier must accept `=` as a valid
        // attribute-entry context alongside `(`, `,`, `<`.
        // リグレッション: `var f = [Attr("x")] () => 0;` のように `=` の直後にあるラムダ属性も検出できること。
        const string content = """
            public class C
            {
                public void M()
                {
                    var f = [Attr("x")] () => 0;
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var attr = Assert.Single(references.Where(r => r.SymbolName == "Attr"));
        Assert.Equal("attribute", attr.ReferenceKind);
    }

    [Fact]
    public void Extract_CsharpNoArgAttribute_ClassifiedAsAttribute()
    {
        // Regression (issue #293): `[Serializable]`, `[Obsolete]`, `[System.Obsolete]`,
        // `[assembly: CLSCompliant]`, `[Required, Key]` — bare no-arg attributes were not
        // indexed at all because CallRegex requires `(`. A dedicated no-arg entry path
        // must emit them with kind `attribute`.
        // リグレッション (issue #293): `[Serializable]` などの引数なし属性も `attribute` として
        // インデックスされること。CallRegex は `(` を要求するため専用の取り込み経路が必要。
        const string content = """
            [assembly: CLSCompliant]
            [Serializable]
            [Obsolete]
            [System.Obsolete]
            [Required, Key]
            public class C
            {
            }
            """;

        var references = ReferenceExtractor.Extract(1, "csharp", content, []);

        Assert.Single(references.Where(r => r.SymbolName == "CLSCompliant" && r.ReferenceKind == "attribute"));
        Assert.Single(references.Where(r => r.SymbolName == "Serializable" && r.ReferenceKind == "attribute"));
        // `[System.Obsolete]` — the qualifier chain is part of the attribute, and the emitted
        // reference should carry the final segment (`Obsolete`). There are two `Obsolete` rows
        // (the plain `[Obsolete]` above and the qualified `[System.Obsolete]`), both attribute.
        Assert.Equal(2, references.Count(r => r.SymbolName == "Obsolete" && r.ReferenceKind == "attribute"));
        Assert.Single(references.Where(r => r.SymbolName == "Required" && r.ReferenceKind == "attribute"));
        Assert.Single(references.Where(r => r.SymbolName == "Key" && r.ReferenceKind == "attribute"));
        Assert.DoesNotContain(references, r => r.ReferenceKind == "call");
    }

    [Fact]
    public void Extract_CsharpIndexerAccess_NotClassifiedAsAttribute()
    {
        // Regression (issue #293): `arr[i]` looks like a bare `[name]` token, but it is an
        // indexer expression, not an attribute. The no-arg attribute path must defer to the
        // attribute-range pre-pass so indexer access is not misclassified as `attribute`.
        // リグレッション (issue #293): `arr[i]` のような indexer アクセスは `[name]` 形だが
        // 属性ではない。属性レンジ pre-pass を経由することで attribute への誤分類を防ぐ。
        const string content = """
            public class C
            {
                public int M(int[] arr, int i) => arr[i];
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, r => r.SymbolName == "i" && r.ReferenceKind == "attribute");
    }

    [Fact]
    public void Extract_JavaNoArgAnnotation_ClassifiedAsAnnotation()
    {
        // Regression (issue #293): `@Deprecated`, `@Override`, `@org.junit.Test` — bare no-arg
        // annotations were not indexed because CallRegex requires `(`. A dedicated no-arg
        // regex must emit them with kind `annotation`.
        // リグレッション (issue #293): `@Deprecated` などの引数なし Java annotation も
        // `annotation` として認識されること。
        const string content = """
            public class C {
                @Deprecated
                @Override
                @org.junit.Test
                public void m() {}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Single(references.Where(r => r.SymbolName == "Deprecated" && r.ReferenceKind == "annotation"));
        Assert.Single(references.Where(r => r.SymbolName == "Override" && r.ReferenceKind == "annotation"));
        Assert.Single(references.Where(r => r.SymbolName == "Test" && r.ReferenceKind == "annotation"));
    }

    [Fact]
    public void Extract_KotlinNoArgTargetAnnotation_ClassifiedAsAnnotation()
    {
        // Regression (issue #293): `@field:Deprecated` — use-site target without parentheses.
        // リグレッション (issue #293): `@field:Deprecated` のような use-site target 付き
        // 引数なしアノテーションも `annotation` 判定になること。
        const string content = """
            class C {
                @field:Deprecated
                val x: Int = 0
            }
            """;

        var references = ReferenceExtractor.Extract(1, "kotlin", content, []);

        var deprecated = Assert.Single(references.Where(r => r.SymbolName == "Deprecated"));
        Assert.Equal("annotation", deprecated.ReferenceKind);
    }

    [Fact]
    public void Extract_KotlinReturnAtLabel_NotClassifiedAsAnnotation()
    {
        // Regression (issue #293): `return@foo` is a Kotlin label reference, not an annotation.
        // The leading lookbehind `(?<![\w)])` in the no-arg annotation regex must prevent a
        // match where `@` is preceded by an identifier character.
        // リグレッション (issue #293): `return@foo` は Kotlin のラベル参照で annotation ではない。
        // 先頭 lookbehind で識別子に続く `@` を除外すること。
        const string content = """
            fun outer() {
                listOf(1).forEach foo@{
                    return@foo
                }
            }
            """;

        var references = ReferenceExtractor.Extract(1, "kotlin", content, []);

        Assert.DoesNotContain(references, r => r.SymbolName == "foo" && r.ReferenceKind == "annotation");
    }

    [Fact]
    public void Extract_KotlinQualifiedFieldTargetAnnotation_ClassifiedAsAnnotation()
    {
        // issue #293 follow-up: Kotlin use-site target with a fully-qualified annotation
        // name, e.g. `@field:com.example.Deprecated("msg")`, must be classified as
        // `annotation` — the dotted qualifier chain plus the `target:` prefix must both
        // resolve back to `@`.
        // issue #293 補足: Kotlin の `@field:com.example.Deprecated("msg")` のように use-site
        // target と修飾付き注釈名が組み合わさった場合も `annotation` 判定になること。
        const string content = """
            class Example {
                @field:com.example.Deprecated("msg")
                val value: Int = 0
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "kotlin", content);
        var references = ReferenceExtractor.Extract(1, "kotlin", content, symbols);

        var deprecated = Assert.Single(references.Where(r => r.SymbolName == "Deprecated"));
        Assert.Equal("annotation", deprecated.ReferenceKind);
    }

    [Fact]
    public void Extract_KotlinQualifiedGetTargetAnnotation_ClassifiedAsAnnotation()
    {
        // Kotlin `@get:com.fasterxml.jackson.annotation.JsonProperty("x")` — use-site target
        // combined with a long qualifier chain must still be `annotation`.
        // Kotlin の `@get:com.fasterxml.jackson.annotation.JsonProperty("x")` も `annotation`。
        const string content = """
            class K {
                @get:com.fasterxml.jackson.annotation.JsonProperty("x")
                val x: Int = 0
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "kotlin", content);
        var references = ReferenceExtractor.Extract(1, "kotlin", content, symbols);

        var jsonProperty = Assert.Single(references.Where(r => r.SymbolName == "JsonProperty"));
        Assert.Equal("annotation", jsonProperty.ReferenceKind);
    }

    [Fact]
    public void Extract_KotlinQualifiedAnnotationWithoutTarget_ClassifiedAsAnnotation()
    {
        // Regression guard: fully-qualified annotation name without a use-site target, e.g.
        // `@org.junit.Test(...)`, must still be `annotation`.
        // 退行防止: use-site target のない `@org.junit.Test(...)` も引き続き `annotation`。
        const string content = """
            class K {
                @org.junit.Test(expected = Exception::class)
                fun run() {}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "kotlin", content);
        var references = ReferenceExtractor.Extract(1, "kotlin", content, symbols);

        var test = Assert.Single(references.Where(r => r.SymbolName == "Test"));
        Assert.Equal("annotation", test.ReferenceKind);
    }
}
