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
    public void Extract_CsharpQualifiedEnumMemberAccess_DoesNotCreateBareEnumMemberReferencesYet()
    {
        const string content = """
            namespace Demo;

            public class Outer
            {
                public enum First { None }
            }

            public enum Nested { A = 1, B = A }

            public class UsesEnum
            {
                public void Use()
                {
                    _ = Nested.A;
                    _ = Outer.First.None;
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "A");
        Assert.DoesNotContain(references, reference => reference.SymbolName == "None");
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
    public void Extract_CsharpNonEnumQualifiedMemberAccess_DoesNotBecomeEnumMemberReference()
    {
        const string content = """
            namespace Demo;

            public enum EnumHolder
            {
                A = 1
            }

            public static class Values
            {
                public static int A = 1;
            }

            public class UsesValues
            {
                public int Read()
                {
                    return Values.A;
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, reference => reference.SymbolName == "A");
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
    public void Extract_CsharpCtorChainThis_RewritesToEnclosingClass()
    {
        const string content = """
            namespace Demo;

            public class A
            {
                public A(int x) { }
                public A() : this(0) { }
                public A(string s) : this(s.Length) { }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var chainRefs = references.Where(r => r.SymbolName == "A").ToList();
        Assert.Equal(2, chainRefs.Count);
        Assert.All(chainRefs, r => Assert.Equal("call", r.ReferenceKind));
        Assert.All(chainRefs, r => Assert.Equal("function", r.ContainerKind));
        Assert.All(chainRefs, r => Assert.Equal("A", r.ContainerName));
        Assert.DoesNotContain(references, r => r.SymbolName == "this");
        Assert.DoesNotContain(references, r => r.SymbolName == "base");
    }

    [Fact]
    public void Extract_CsharpCtorChainBase_RewritesToBaseType()
    {
        const string content = """
            namespace Demo;

            public class A
            {
                public A(int x) { }
            }

            public class B : A
            {
                public B() : base(42) { }
                public B(int x, int y) : base(x + y) { }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var chainRefs = references
            .Where(r => r.SymbolName == "A" && r.ContainerName == "B")
            .ToList();
        Assert.Equal(2, chainRefs.Count);
        Assert.All(chainRefs, r => Assert.Equal("call", r.ReferenceKind));
        Assert.DoesNotContain(references, r => r.SymbolName == "base");
    }

    [Fact]
    public void Extract_CsharpCtorChainCrossLine_AttributesToConstructor()
    {
        const string content = """
            namespace Demo;

            public class A
            {
                public A(int x) { }
                public A(int x, int y)
                    : this(x + y)
                {
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var chainRef = Assert.Single(references, r => r.SymbolName == "A");
        Assert.Equal("call", chainRef.ReferenceKind);
        Assert.Equal("function", chainRef.ContainerKind);
        Assert.Equal("A", chainRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpRecordChain_RewritesToBaseType()
    {
        const string content = """
            namespace Demo;

            public record Parent(int Value);

            public record Child(int Value, int Extra) : Parent(Value);
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, r => r.SymbolName == "base");
        Assert.DoesNotContain(references, r => r.SymbolName == "this");

        // Record primary-ctor base call is attributed to a synthetic function named after
        // the record, so `callers` / `callees` / `impact` can traverse the chain edge.
        // record のプライマリーコンストラクタの base 呼び出しが、record 名の合成 function に紐付く。
        var parentRef = Assert.Single(references, r => r.SymbolName == "Parent");
        Assert.Equal("call", parentRef.ReferenceKind);
        Assert.Equal("function", parentRef.ContainerKind);
        Assert.Equal("Child", parentRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpRecordChain_MultilineBaseCall_RewritesToBaseType()
    {
        // Record declaration wraps the base primary-ctor call onto a continuation line.
        // The synthetic function container must span the whole header range so the Parent
        // reference is attributed to the record rather than landing without a container.
        // record 宣言の base primary-ctor 呼び出しが改行で別行に来るケース。
        // 合成 function コンテナは宣言ヘッダー全体を覆い、`Parent(...)` が record に紐付くこと。
        const string content = """
            namespace Demo;

            public record Parent(int Value);

            public record Child(int Value, int Extra)
                : Parent(Value);
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var parentRef = Assert.Single(references, r => r.SymbolName == "Parent");
        Assert.Equal("call", parentRef.ReferenceKind);
        Assert.Equal("function", parentRef.ContainerKind);
        Assert.Equal("Child", parentRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpRecordChain_BracedBodyAfterBaseCall_AttributesBaseCallOnly()
    {
        // Base primary-ctor call sits at the end of the record header on its own line,
        // followed by a braced body. The synthetic container must cover the base-call line
        // but not leak into body method lines (those keep their real innermost containers).
        // base 呼び出し後に `{}` 本体が続くケース。合成コンテナはヘッダーのみを覆い、
        // body 内のメソッド行は通常の container を維持すること。
        const string content = """
            namespace Demo;

            public record Parent(int Value);

            public record Child(int Value, int Extra)
                : Parent(Value)
            {
                public int DoubleValue() => Value * 2;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var parentRef = Assert.Single(references, r => r.SymbolName == "Parent");
        Assert.Equal("function", parentRef.ContainerKind);
        Assert.Equal("Child", parentRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpRecordChain_SameLineBracedBody_DoesNotStealBodyCallsForSyntheticCtor()
    {
        // Legal same-line braced record where the header, base call, and a body method all live
        // on one line: `record Child(int V) : Parent(V) { public int Sum() => Add(V, 1); }`.
        // The synthetic function-kind container must cover only the header portion of that line
        // (before `{`), not the body. Overriding the whole line would steal `Add(...)` from its
        // real inner container and misattribute it to the synthetic record ctor in callers/impact.
        // 同一行 record 内の `{` より後ろの本体呼び出しが合成 ctor に奪われないことを固定する。
        const string content = """
            namespace Demo;

            public record Parent(int Value);

            public record Child(int Value) : Parent(Value) { public int Sum() => Add(Value, 1); }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var parentRef = Assert.Single(references, r => r.SymbolName == "Parent");
        Assert.Equal("call", parentRef.ReferenceKind);
        Assert.Equal("function", parentRef.ContainerKind);
        Assert.Equal("Child", parentRef.ContainerName);

        // Add(...) sits past the header-terminating `{`, so the synthetic record ctor must NOT
        // claim it. The exact inner container depends on whether SymbolExtractor produces a Sum
        // symbol for this layout, but in any case the call must not be attributed to a
        // `function` kind container named `Child` (that would be the synthetic record ctor).
        // `Add(...)` が synthetic record ctor に奪われていないことだけを固定（inner 側の
        // 具体的な container は extractor の挙動に依存するため深追いしない）。
        var addRef = Assert.Single(references, r => r.SymbolName == "Add");
        Assert.False(
            addRef.ContainerKind == "function" && addRef.ContainerName == "Child",
            $"Add(...) was incorrectly attributed to the synthetic record ctor (container_kind={addRef.ContainerKind}, container_name={addRef.ContainerName}).");
    }

    [Fact]
    public void Extract_CsharpRecord_TupleGenericInterfaceBase_DoesNotSynthesizeRecordCtor()
    {
        // Interface implementations that use tuple generic type args (e.g. `IBox<(int, int)>`)
        // contain `(` at generic depth 1, not at depth 0 as a primary-ctor call would. The record
        // primary-ctor detector must treat this as a non-chain base list and avoid synthesizing a
        // phantom record-ctor container; otherwise unrelated header calls would be attributed to
        // a fake function-kind `Pair` caller. The body call `Multiply(A, B)` inside `Combine`
        // must therefore stay attributed to `Combine`, not to a synthetic record ctor.
        // tuple を含む interface 実装が primary-ctor 呼び出しと誤判定されて合成 ctor が立たないよう固定。
        const string content = """
            namespace Demo;

            public interface IBox<T> { }

            public record Pair(int A, int B) : IBox<(int Left, int Right)>
            {
                public int Combine() => Multiply(A, B);
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        // Multiply must stay attributed to `Combine`, not to a synthetic record ctor.
        var multiplyRef = Assert.Single(references, r => r.SymbolName == "Multiply");
        Assert.Equal("call", multiplyRef.ReferenceKind);
        Assert.False(
            multiplyRef.ContainerKind == "function" && multiplyRef.ContainerName == "Pair",
            $"Multiply(...) was incorrectly attributed to a synthetic record ctor (container_kind={multiplyRef.ContainerKind}, container_name={multiplyRef.ContainerName}).");
    }

    [Fact]
    public void Extract_CsharpRecordChain_SplitLinePrimaryCtorAndBaseCall_AttributesToSyntheticRecordCtor()
    {
        // Legal C# formatter style where the primary-ctor parens and `: Parent(...)` are split
        // across multiple lines: `record Child` / `(` / `  int Value` / `)` / `  : Parent(Value);`.
        // The joined-header record detector must still pick this up so the base call is attributed
        // to the synthetic function-kind container named after the record, not to class/null.
        // record 名と `(` を別行に分け、base 呼び出しもさらに別行に置く合法な改行スタイル。
        // 連結済みヘッダーベースの record 判定で拾い、`Parent(...)` を record の合成 function
        // コンテナに紐付ける必要がある。
        const string content = """
            namespace Demo;

            public record Parent(int Value);

            public record Child
            (
                int Value
            )
                : Parent(Value);
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var parentRef = Assert.Single(references, r => r.SymbolName == "Parent");
        Assert.Equal("call", parentRef.ReferenceKind);
        Assert.Equal("function", parentRef.ContainerKind);
        Assert.Equal("Child", parentRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpClassPrimaryCtor_BaseCall_AttributesToSyntheticCtor()
    {
        // C# 12 introduced primary constructors on plain `class` / `struct` (not just records).
        // `public class Child(int value) : Parent(value)` must be treated the same as the record
        // form: the `Parent(...)` base call is attributed to a synthetic function-kind container
        // named after the child class, so callers/impact can follow the ctor chain.
        // C# 12 の class primary constructor でも record と同じ合成 function コンテナで扱うことを固定。
        const string content = """
            namespace Demo;

            public class Parent(int value)
            {
                public int Value { get; } = value;
            }

            public class Child(int value) : Parent(value)
            {
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var parentRef = Assert.Single(references, r => r.SymbolName == "Parent");
        Assert.Equal("call", parentRef.ReferenceKind);
        Assert.Equal("function", parentRef.ContainerKind);
        Assert.Equal("Child", parentRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpClassPrimaryCtor_SplitLineBaseCall_AttributesToSyntheticCtor()
    {
        // Split-line variant of C# 12 class primary ctor. The joined-header detector must still
        // pick it up so the base call is attributed to the synthetic function-kind container.
        // C# 12 class primary ctor の split-line 形式でも同じ扱いになることを固定。
        const string content = """
            namespace Demo;

            public class Parent(int value)
            {
            }

            public class ChildSplit
            (
                int value
            ) : Parent(
                value
            )
            {
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var parentRef = Assert.Single(references, r => r.SymbolName == "Parent");
        Assert.Equal("call", parentRef.ReferenceKind);
        Assert.Equal("function", parentRef.ContainerKind);
        Assert.Equal("ChildSplit", parentRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpGenericClassPrimaryCtor_BaseCall_AttributesToSyntheticCtor()
    {
        // Generic class with C# 12 primary ctor: `public class GenericChild<T>(T value) : Parent(42)`.
        // The generic arity in the type header must not break primary-ctor detection, and the base
        // call still resolves to the synthetic function-kind container named after the class.
        // generic class の C# 12 primary ctor でも同じ扱いになることを固定。
        const string content = """
            namespace Demo;

            public class Parent(int value)
            {
            }

            public class GenericChild<T>(T value) : Parent(42)
            {
                public T Item { get; } = value;
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var parentRef = Assert.Single(references, r => r.SymbolName == "Parent");
        Assert.Equal("call", parentRef.ReferenceKind);
        Assert.Equal("function", parentRef.ContainerKind);
        Assert.Equal("GenericChild", parentRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpClassPrimaryCtor_SameLineBracedBody_DoesNotStealBodyCalls()
    {
        // Same-line braced form of C# 12 class primary ctor: header, base call, and a body
        // expression all on one line. The synthetic function-kind container must cover only the
        // header portion (before `{`), so body calls stay attributed to their real container.
        // C# 12 class primary ctor の同一行 braced 形式でも、本体呼び出しが合成 ctor に奪われないことを固定。
        const string content = """
            namespace Demo;

            public static class Helper
            {
                public static int Compute(int x) => x + 1;
            }

            public class Parent(int value)
            {
            }

            public class ChildWithBody(int value) : Parent(value) { public int Doubled { get; } = Helper.Compute(value); }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var parentRef = Assert.Single(references, r => r.SymbolName == "Parent");
        Assert.Equal("function", parentRef.ContainerKind);
        Assert.Equal("ChildWithBody", parentRef.ContainerName);

        // Helper.Compute(value) sits past the header-terminating `{`, so the synthetic ctor
        // must NOT claim it. The exact inner container depends on SymbolExtractor behavior,
        // but the call must not be attributed to a `function` kind container named `ChildWithBody`.
        // 本体側 `Helper.Compute(value)` が合成 ctor に奪われていないことだけを固定する。
        var computeRef = Assert.Single(references, r => r.SymbolName == "Compute");
        Assert.False(
            computeRef.ContainerKind == "function" && computeRef.ContainerName == "ChildWithBody",
            $"Compute(...) was incorrectly attributed to the synthetic class primary ctor (container_kind={computeRef.ContainerKind}, container_name={computeRef.ContainerName}).");
    }

    [Fact]
    public void Extract_CsharpStructPrimaryCtor_BaseCall_AttributesToSyntheticCtor()
    {
        // C# 12 struct primary ctor with an interface-like base list carrying a call. structs
        // cannot inherit another class, but the synthetic-ctor code path is still reached when
        // the base list entry uses the primary-ctor call shape. This exercises the `struct` kind
        // branch added alongside class generalization.
        // C# 12 struct primary ctor でも合成 function コンテナに切り替わることを固定（struct 分岐カバー）。
        const string content = """
            namespace Demo;

            public class BaseImpl(int value)
            {
            }

            public struct BoxedValue(int value) : BaseImpl(value)
            {
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var baseRef = Assert.Single(references, r => r.SymbolName == "BaseImpl");
        Assert.Equal("call", baseRef.ReferenceKind);
        Assert.Equal("function", baseRef.ContainerKind);
        Assert.Equal("BoxedValue", baseRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpGenericBase_StripsGenericArgs()
    {
        const string content = """
            namespace Demo;

            public class Holder<T>
            {
                public Holder(T value) { }
            }

            public class IntHolder : Holder<int>
            {
                public IntHolder() : base(0) { }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var chainRef = Assert.Single(references, r => r.SymbolName == "Holder");
        Assert.Equal("call", chainRef.ReferenceKind);
        Assert.Equal("IntHolder", chainRef.ContainerName);
    }

    [Fact]
    public void Extract_CsharpBaseAfterInterfaces_UsesFirstBaseListEntry()
    {
        // The C# compiler requires the base class to come first in the base list,
        // so the first entry is the authoritative target for `base(...)`.
        // C# の base list では基底クラスが先頭に来る必要があり、そこが base(...) の呼び先となる。
        const string content = """
            namespace Demo;

            public interface IMarker {}

            public class Root
            {
                public Root(int x) { }
            }

            public class Leaf : Root, IMarker
            {
                public Leaf() : base(42) { }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var chainRef = Assert.Single(references, r => r.SymbolName == "Root");
        Assert.Equal("call", chainRef.ReferenceKind);
        Assert.Equal("Leaf", chainRef.ContainerName);
    }

    [Fact]
    public void Extract_JavaCtorChainSuper_RewritesToBaseClass()
    {
        const string content = """
            package demo;

            public class Root {
                public Root(int x) {}
            }

            class Leaf extends Root {
                public Leaf(int x) {
                    super(x);
                }
                public Leaf() {
                    this(0);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        var superRef = Assert.Single(references, r => r.SymbolName == "Root");
        Assert.Equal("call", superRef.ReferenceKind);
        Assert.Equal("Leaf", superRef.ContainerName);
        Assert.DoesNotContain(references, r => r.SymbolName == "super");

        var thisRef = Assert.Single(references, r =>
            r.SymbolName == "Leaf" && r.ContainerKind == "function" && r.ContainerName == "Leaf");
        Assert.Equal("call", thisRef.ReferenceKind);

        // The generic CallRegex loop runs after EmitJavaCtorChainReferences; `this` must be
        // in the Java ignore set so it does not leak as a phantom `call this` edge.
        // chain 書き換え後に generic CallRegex が `call this` を二重に出さないこと。
        Assert.DoesNotContain(references, r => r.SymbolName == "this");
    }

    [Fact]
    public void Extract_JavaCtorChainSuper_PackagePrivateCtor_RewritesToBaseClass()
    {
        // Package-private Java ctors like `Leaf(int x){...}` do not receive body ranges from
        // SymbolExtractor, so they are excluded from the innermost-container lookup. The
        // constructor-chain rewrite must still attribute the super(x) call to the ctor
        // through a name-based fallback against the enclosing class.
        // SymbolExtractor は package-private ctor に body 範囲を付けないため、innermost container
        // 判定からは外れる。chain 書き換えは外側クラス名との一致 fallback で救う。
        const string content = """
            package demo;

            public class Root {
                Root(int x) {}
            }

            class Leaf extends Root {
                Leaf(int x) {
                    super(x);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        var superRef = Assert.Single(references, r =>
            r.SymbolName == "Root" && r.ContainerKind == "function");
        Assert.Equal("call", superRef.ReferenceKind);
        Assert.Equal("Leaf", superRef.ContainerName);
        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void Extract_JavaCtorChain_SameLineBody_RewritesToBaseClass()
    {
        // Same-line ctor bodies like `Leaf(int x){super(x);}` do not match
        // SymbolExtractor's enum-member regex (line ends with `}`, not `{`/`,`/`;`),
        // so no function symbol is emitted. The chain rewrite must synthesize a
        // ctor container from the line text and attribute super(x)/this(0) correctly.
        // 同一行に本体を書くコンストラクタは SymbolExtractor で関数シンボルが作られないため、
        // chain 書き換えは行テキストから ctor コンテナを合成して super/this を拾う必要がある。
        const string content = """
            package demo;

            public class Root {
                public Root(int x) {}
                Root() {}
            }

            class PublicLeaf extends Root {
                public PublicLeaf(int x){super(x);}
                public PublicLeaf(){this(0);}
            }

            class PackageLeaf extends Root {
                PackageLeaf(int x){super(x);}
                PackageLeaf(){this(0);}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        var publicSuper = Assert.Single(references, r =>
            r.SymbolName == "Root" && r.ContainerName == "PublicLeaf" && r.ContainerKind == "function");
        Assert.Equal("call", publicSuper.ReferenceKind);

        var publicThis = Assert.Single(references, r =>
            r.SymbolName == "PublicLeaf" && r.ContainerName == "PublicLeaf" && r.ContainerKind == "function");
        Assert.Equal("call", publicThis.ReferenceKind);

        var packageSuper = Assert.Single(references, r =>
            r.SymbolName == "Root" && r.ContainerName == "PackageLeaf" && r.ContainerKind == "function");
        Assert.Equal("call", packageSuper.ReferenceKind);

        var packageThis = Assert.Single(references, r =>
            r.SymbolName == "PackageLeaf" && r.ContainerName == "PackageLeaf" && r.ContainerKind == "function");
        Assert.Equal("call", packageThis.ReferenceKind);

        Assert.DoesNotContain(references, r => r.SymbolName == "super");
        Assert.DoesNotContain(references, r => r.SymbolName == "this");
    }

    [Fact]
    public void Extract_JavaCtorChain_SameLineBody_WithLeadingAnnotation_RewritesToBaseClass()
    {
        // Same-line ctor bodies can be preceded by annotations (with or without argument
        // lists), e.g. `@Deprecated Leaf(int x){super(x);}` or
        // `@SuppressWarnings("unused") Leaf(int x){super(x);}`.
        // The synthesis regex must accept the leading annotation so the chain rewrite still
        // finds a ctor container.
        // 同一行 ctor 本体の直前にアノテーションが付く形（引数あり/なし）も、合成コンテナ生成で
        // 取りこぼしてはならない。
        const string content = """
            package demo;

            public class Root {
                public Root(int x) {}
            }

            class Leaf extends Root {
                @Deprecated Leaf(int x){super(x);}
                @SuppressWarnings("unused") Leaf(long x){super((int) x);}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        var rootRefs = references.Where(r =>
            r.SymbolName == "Root" && r.ContainerName == "Leaf" && r.ContainerKind == "function").ToList();
        Assert.Equal(2, rootRefs.Count);
        Assert.All(rootRefs, r => Assert.Equal("call", r.ReferenceKind));

        Assert.DoesNotContain(references, r => r.SymbolName == "super");
        Assert.DoesNotContain(references, r => r.SymbolName == "this");
    }

    [Fact]
    public void Extract_JavaCtorChain_SameLineBody_WithGenericCtor_RewritesToBaseClass()
    {
        // Generic constructors (`public <T> Leaf(T x){super(0);}`) insert type parameters
        // between the modifiers and the ctor name. The synthesis regex must accept the
        // optional `<...>` token before `<name>`.
        // 型パラメータ付き ctor (`public <T> Leaf(T x){super(0);}`) は修飾子と ctor 名の間に
        // `<...>` が入る。合成コンテナ生成は名前直前の generic 型パラメータを許容すべし。
        const string content = """
            package demo;

            public class Root {
                public Root(int x) {}
            }

            class Leaf extends Root {
                public <T> Leaf(T x){super(0);}
                <T extends Number> Leaf(T x, int y){super(y);}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        var rootRefs = references.Where(r =>
            r.SymbolName == "Root" && r.ContainerName == "Leaf" && r.ContainerKind == "function").ToList();
        Assert.Equal(2, rootRefs.Count);
        Assert.All(rootRefs, r => Assert.Equal("call", r.ReferenceKind));

        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void Extract_JavaSuperCall_OutsideConstructor_IsNotRewritten()
    {
        // super.method(...) is a regular method call, not a ctor chain.
        // Reference extractor must not confuse the two.
        // super.method(...) は通常のメソッド呼び出し。連鎖呼び出しと混同しない。
        const string content = """
            package demo;

            class Base {
                public void run() {}
            }

            class Child extends Base {
                public void run() {
                    super.run();
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.DoesNotContain(references, r => r.SymbolName == "Base");
    }

    [Fact]
    public void ParseCSharpBaseType_HandlesCommonShapes()
    {
        Assert.Equal("A", ReferenceExtractor.ParseCSharpBaseType("public class B : A"));
        Assert.Equal("A", ReferenceExtractor.ParseCSharpBaseType("class B : A, IFoo"));
        Assert.Equal("A", ReferenceExtractor.ParseCSharpBaseType("class B<T> : A<T> where T : new()"));
        Assert.Equal("Base", ReferenceExtractor.ParseCSharpBaseType("record Child(int x) : Base(x);"));
        Assert.Equal("Exception", ReferenceExtractor.ParseCSharpBaseType("class MyErr : global::System.Exception"));
        Assert.Null(ReferenceExtractor.ParseCSharpBaseType("public class Solo"));
    }

    [Fact]
    public void ParseCSharpBaseType_NestedTypeUnderGenericOuter_KeepsTerminalSegment()
    {
        // `Outer<int>.Base` must resolve to `Base`, not `Outer`. Naive slicing at the first `<`
        // drops the terminal segment and misattributes `callers` / `callees` / `impact`.
        // `Outer<int>.Base` のような generic な外側型の内部型は末尾セグメントを返す必要がある。
        Assert.Equal("Base", ReferenceExtractor.ParseCSharpBaseType("class Leaf : Outer<int>.Base"));
        Assert.Equal("Base", ReferenceExtractor.ParseCSharpBaseType("class Leaf : Outer<int>.Base, IFoo"));
        Assert.Equal("Inner", ReferenceExtractor.ParseCSharpBaseType("class Leaf : global::Ns.Outer<T>.Inner"));
        Assert.Equal("Inner", ReferenceExtractor.ParseCSharpBaseType("class Leaf : Outer<A, B<C>>.Inner"));
    }

    [Fact]
    public void ParseJavaBaseType_NestedTypeUnderGenericOuter_KeepsTerminalSegment()
    {
        // `Outer<Integer>.Base` must resolve to `Base`, mirroring the C# behavior.
        // Java でも `Outer<Integer>.Base` 形は末尾セグメント `Base` を返す。
        Assert.Equal("Base", ReferenceExtractor.ParseJavaBaseType("class Leaf extends Outer<Integer>.Base {"));
        Assert.Equal("Base", ReferenceExtractor.ParseJavaBaseType("class Leaf extends Outer<Integer>.Base implements Foo {"));
        Assert.Equal("Inner", ReferenceExtractor.ParseJavaBaseType("class Leaf extends a.b.Outer<A, B<C>>.Inner {"));
        Assert.Equal("A", ReferenceExtractor.ParseJavaBaseType("class B extends A implements IFoo"));
    }

    [Fact]
    public void Extract_CsharpBaseCall_NestedTypeUnderGenericOuter_AttributesToTerminalSegment()
    {
        // End-to-end C# regression for the `Outer<int>.Base` attribution bug.
        // `Outer<int>.Base` が `Base` へ張られることの E2E 回帰テスト。
        const string content = """
            namespace Demo;

            public class Outer<T>
            {
                public class Base
                {
                    public Base(int x) {}
                }
            }

            public class Leaf : Outer<int>.Base
            {
                public Leaf() : base(0) {}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Base" && r.ContainerKind == "function" && r.ContainerName == "Leaf");
        Assert.DoesNotContain(references, r => r.SymbolName == "base");
    }

    [Fact]
    public void Extract_JavaSuperCall_NestedTypeUnderGenericOuter_AttributesToTerminalSegment()
    {
        // End-to-end Java regression for the `Outer<Integer>.Base` attribution bug.
        // `Outer<Integer>.Base` が `Base` へ張られることの E2E 回帰テスト。
        const string content = """
            package demo;

            public class Outer<T> {
                public static class Base {
                    public Base(int x) {}
                }
            }

            class Leaf extends Outer<Integer>.Base {
                Leaf(){super(0);}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Base" && r.ContainerKind == "function" && r.ContainerName == "Leaf");
        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void Extract_JavaCtorChain_SameLineBody_WithQualifiedAnnotation_RewritesToBaseClass()
    {
        // Annotations on same-line ctors can be fully qualified (`@demo.Ann`) or carry
        // nested-paren argument lists. The synthesis scanner must strip both before locating
        // the ctor name.
        // 同一行 ctor 本体のアノテーションは `@demo.Ann` のような FQCN や、入れ子の括弧付き
        // 引数を持つこともある。合成コンテナ生成は両方を剥がして ctor 名へ辿り着く必要がある。
        const string content = """
            package demo;

            public class Root {
                public Root(int x) {}
            }

            @interface Ann {}

            class Leaf extends Root {
                @demo.Ann Leaf(int x){super(x);}
                @SuppressWarnings({"unused", "unchecked"}) Leaf(long x){super((int) x);}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        var rootRefs = references.Where(r =>
            r.SymbolName == "Root" && r.ContainerName == "Leaf" && r.ContainerKind == "function").ToList();
        Assert.Equal(2, rootRefs.Count);
        Assert.All(rootRefs, r => Assert.Equal("call", r.ReferenceKind));
    }

    [Fact]
    public void Extract_JavaCtorChain_SameLineBody_WithNestedGenericBound_RewritesToBaseClass()
    {
        // Generic type parameters can carry nested `<...>` bounds such as
        // `<T extends Comparable<Integer>>`. A flat regex cannot balance the nested `>`; the
        // synthesis scanner must handle it.
        // `<T extends Comparable<Integer>>` のような入れ子 `<...>` を伴う generic 境界も
        // 合成コンテナ生成で取りこぼしてはならない。
        const string content = """
            package demo;

            public class Root {
                public Root(int x) {}
            }

            class Leaf extends Root {
                public <T extends Comparable<Integer>> Leaf(T x){super(0);}
                <U extends java.util.List<? extends Number>> Leaf(U xs, int y){super(y);}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        var rootRefs = references.Where(r =>
            r.SymbolName == "Root" && r.ContainerName == "Leaf" && r.ContainerKind == "function").ToList();
        Assert.Equal(2, rootRefs.Count);
        Assert.All(rootRefs, r => Assert.Equal("call", r.ReferenceKind));
    }

    [Fact]
    public void ParseJavaBaseType_SealedWithPermits_StopsAtPermits()
    {
        // Java 17+ sealed types can add `permits A, B` after the base-list. The base-type scanner
        // must stop at the `permits` word-boundary just like `implements`, otherwise the returned
        // base type greedily absorbs `permits` and the super(...) edge misattributes.
        // Java 17+ sealed 型は base-list の後ろに `permits A, B` を付けられる。`implements` と同様
        // に `permits` の単語境界で停止しないと、基底型が `Base permits A` のように伸びてしまう。
        Assert.Equal("Base", ReferenceExtractor.ParseJavaBaseType("sealed class Leaf extends Base permits A, B {"));
        Assert.Equal("Base", ReferenceExtractor.ParseJavaBaseType("sealed class Leaf extends Base permits A"));
        Assert.Equal("Base", ReferenceExtractor.ParseJavaBaseType("non-sealed class Leaf extends Base permits A {"));
        Assert.Equal("Base", ReferenceExtractor.ParseJavaBaseType("sealed class Leaf extends Base implements Foo permits A {"));
        Assert.Equal("Base", ReferenceExtractor.ParseJavaBaseType("sealed class Leaf extends Outer<Integer>.Base permits A {"));
    }

    [Fact]
    public void Extract_JavaSuperCall_SealedWithPermits_AttributesToRealBase()
    {
        // End-to-end regression for Java 17+ sealed class with `permits`. Before the fix the base
        // resolver returned `"Base permits A"` and the super(...) edge was dropped.
        // Java 17+ sealed class with `permits` の E2E 回帰。修正前は基底型解決が
        // `"Base permits A"` となり super(...) エッジが落ちていた。
        const string content = """
            package demo;

            public sealed class Base permits Leaf, Other {
                public Base(int x) {}
            }

            final class Leaf extends Base {
                Leaf(){super(0);}
            }

            final class Other extends Base {
                Other(){super(1);}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Base" && r.ContainerKind == "function" && r.ContainerName == "Leaf");
        Assert.Contains(references, r =>
            r.SymbolName == "Base" && r.ContainerKind == "function" && r.ContainerName == "Other");
        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void Extract_CsharpBaseCall_MultiLineBaseList_AttributesToParent()
    {
        // C# multi-line base-list (e.g. `class Child\n    : Parent`) must still resolve the
        // `: base(...)` target. SymbolRecord.Signature only stores the first declaration line, so
        // the header must be reconstructed from structural lines to find `: Parent`.
        // C# の複数行 base-list (`class Child\n    : Parent` の形) でも `: base(...)` の
        // 解決先を見失わないこと。SymbolRecord.Signature は 1 行目しか持たないため、ヘッダを
        // structural lines から再構築する必要がある。
        const string content = """
            namespace Demo;

            public class Parent
            {
                public Parent(int x) {}
            }

            public class Child
                : Parent
            {
                public Child() : base(0) {}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Parent" && r.ContainerKind == "function" && r.ContainerName == "Child");
        Assert.DoesNotContain(references, r => r.SymbolName == "base");
    }

    [Fact]
    public void Extract_CsharpBaseCall_MultiLineBaseList_NestedGenericOuter_AttributesToTerminalSegment()
    {
        // Multi-line base-list combined with the `Outer<T>.Base` nested-generic form. Reconstructed
        // header feeds both multi-line handling and the depth-aware terminal-segment extractor.
        // 複数行 base-list と `Outer<T>.Base` ネスト generic 形の複合ケース。再構築したヘッダが
        // 両方の分岐（multi-line 連結と depth-aware な末尾セグメント抽出）を通る。
        const string content = """
            namespace Demo;

            public class Outer<T>
            {
                public class Base
                {
                    public Base(int x) {}
                }
            }

            public class Child<T>
                : Outer<T>.Base
            {
                public Child() : base(0) {}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Base" && r.ContainerKind == "function" && r.ContainerName == "Child");
        Assert.DoesNotContain(references, r => r.SymbolName == "base");
    }

    [Fact]
    public void Extract_JavaSuperCall_MultiLineExtends_AttributesToBaseClass()
    {
        // Java multi-line `class Foo\n    extends Base` must still resolve `super(...)`.
        // SymbolRecord.Signature only captures the first declaration line, so the header has to
        // be reconstructed from structural lines before ParseJavaBaseType runs.
        // Java の複数行 `class Foo\n    extends Base` でも `super(...)` の解決先を見失わないこと。
        // SymbolRecord.Signature は 1 行目しか持たないため、ヘッダを structural lines から再構築する。
        const string content = """
            package demo;

            public class Base {
                public Base(int x) {}
            }

            class Leaf
                extends Base {
                Leaf() {
                    super(0);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Base" && r.ContainerKind == "function" && r.ContainerName == "Leaf");
        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void Extract_JavaSuperCall_MultiLineExtends_NestedGenericOuter_AttributesToTerminalSegment()
    {
        // Multi-line `extends` combined with `Outer<Integer>.Base` nested-generic form.
        // Reconstructed header feeds both multi-line handling and the depth-aware terminal-segment
        // extractor.
        // 複数行 `extends` と `Outer<Integer>.Base` ネスト generic 形の複合ケース。再構築した
        // ヘッダが複数行連結と depth-aware な末尾セグメント抽出の両方を通る。
        const string content = """
            package demo;

            public class Outer<T> {
                public static class Base {
                    public Base(int x) {}
                }
            }

            class Leaf
                extends Outer<Integer>.Base {
                Leaf() {
                    super(0);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Base" && r.ContainerKind == "function" && r.ContainerName == "Leaf");
        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void Extract_JavaSuperCall_MultiLineExtends_WithPermitsContinuation_AttributesToBaseClass()
    {
        // Java 17+ sealed class with `permits` sitting on a continuation line. The reconstructed
        // multi-line header must still stop at `permits` via the base-type scanner.
        // Java 17+ sealed class で `permits` が継続行にある場合でも、再構築したヘッダから
        // base-type scanner が `permits` を終端として正しく停止すること。
        const string content = """
            package demo;

            public sealed class Base permits Leaf {
                public Base(int x) {}
            }

            sealed class Leaf
                extends Base
                permits None {
                Leaf() {
                    super(0);
                }
            }

            final class None extends Leaf {
                None() { super(); }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Base" && r.ContainerKind == "function" && r.ContainerName == "Leaf");
        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void Extract_CsharpBaseCall_MultiLineBaseList_WithWhereClause_AttributesToParent()
    {
        // Multi-line base-list continuation followed by a `where` constraint must still resolve
        // the base type correctly. The `where` clause regex is trimmed before extracting the
        // first base entry, and the multi-line header path must not regress that behavior.
        // 複数行 base-list の継続行の末尾に `where` 制約が続く形でも基底型が正しく解決されること。
        const string content = """
            namespace Demo;

            public class Parent<U>
            {
                public Parent(U x) {}
            }

            public class Child<T>
                : Parent<T>
                where T : class, new()
            {
                public Child() : base(null!) {}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Parent" && r.ContainerKind == "function" && r.ContainerName == "Child");
        Assert.DoesNotContain(references, r => r.SymbolName == "base");
    }

    [Fact]
    public void ParseJavaBaseType_TypeUseAnnotation_Stripped()
    {
        // Java type-use annotations (JLS 9.7.4) can precede the base type or sit between nested
        // segments: `extends @Ann Root`, `extends @pkg.Ann(value=1) Root`, `Outer<Integer>.@Ann Base`.
        // The base-type resolver must strip them or it returns a phantom name like `@Ann Root` that
        // misattributes `super(...)` edges to a non-existent symbol.
        // Java の type-use annotation (JLS 9.7.4) は基底型の直前やネスト型の区切り位置に現れる。
        // 剥がさないと `@Ann Root` のような幽霊シンボルへ `super(...)` が誤配線される。
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @Ann Root {"));
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @pkg.Ann Root {"));
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @Ann(value=1) Root {"));
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @Ann @Other Root {"));
        Assert.Equal("Base", ReferenceExtractor.ParseJavaBaseType("class Leaf extends Outer<Integer>.@Ann Base {"));
        Assert.Equal("Base", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @Ann Outer<Integer>.Base {"));
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @Ann Root implements Foo {"));
    }

    [Fact]
    public void ParseJavaBaseType_TypeUseAnnotationWithMultipleArgs_Stripped()
    {
        // Annotation arguments can contain commas (`@Ann(a = 1, b = 2)`) or brace-wrapped arrays
        // (`@Ann({A.class, B.class})`). The base-type scanner must track `(...)` depth so those
        // commas do not prematurely end the base-list walk — otherwise the annotation is not
        // fully consumed, `StripJavaTypeAnnotations` never sees the closing `)`, and the super(...)
        // edge resolves to a phantom type / annotation name instead of the real base.
        // annotation 引数は `@Ann(a = 1, b = 2)` や `@Ann({A.class, B.class})` のようにカンマや
        // 波括弧配列を含む。base-type scanner が `(...)` の深さを追わないと、内側の `,` で走査が
        // 切れて annotation を剥がし切れず、super(...) が幽霊型・annotation 名に張られる。
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @Ann(a = 1, b = 2) Root {"));
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @Ann(a=1, b=2) Root {"));
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @Ann({A.class, B.class}) Root {"));
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @pkg.Ann(value = {1, 2, 3}) Root {"));
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @Ann(a=1, b=2) Root implements Foo {"));
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType("class Leaf extends @Ann(a=1, b=2) Root permits A, B {"));
    }

    [Fact]
    public void Extract_JavaSuperCall_TypeUseAnnotationWithMultipleArgs_AttributesToRealBase()
    {
        // E2E regression for the multi-arg annotation case. Before the fix the scanner broke at
        // the `,` inside `@Ann(a = 1, b = 2)`, returned a truncated segment, and the super(...)
        // edge either pointed at a phantom annotated type or was dropped entirely.
        // 複数引数 annotation の E2E 回帰。修正前は `@Ann(a = 1, b = 2)` 内の `,` で走査が切れて
        // super(...) が幽霊型名に張られるか完全に落ちていた。
        const string content = """
            package demo;

            @interface Ann {
                int a();
                int b();
            }

            class Root {
                Root(int value) {}
            }

            class Leaf extends @Ann(a = 1, b = 2) Root {
                Leaf() {
                    super(0);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        // The chain edge must attribute to `Root` (the real base), not to a phantom annotated
        // type name. The class-header line still picks up `Ann(...)` as a regular call from the
        // annotation site — that is the existing CallRegex behavior for any parenthesized name
        // and is unrelated to the base-list scanner fix; only the chain-line `super(0)` edge is
        // what we care about here.
        // 連鎖エッジは本当の基底 `Root` に張られ、annotation 由来の幽霊型名にはならないこと。
        // ヘッダ行の `@Ann(...)` は汎用 CallRegex が `Ann(...)` として通常の call を拾う既存挙動で、
        // 本修正のスコープ外。ここでは chain 行 `super(0)` のエッジだけを検証する。
        Assert.Contains(references, r =>
            r.SymbolName == "Root" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Leaf" && r.Line == 14);
        Assert.DoesNotContain(references, r => r.SymbolName.StartsWith("@"));
        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void Extract_JavaSuperCall_TypeUseAnnotatedExtends_AttributesToRealBase()
    {
        // End-to-end regression for Java type-use annotated `extends`. Before the fix the base
        // resolver returned `"@Ann Root"` and `super(0)` edges pointed at a phantom symbol.
        // Java の type-use annotation 付き `extends` の E2E 回帰。修正前は基底型解決が
        // `"@Ann Root"` となり super(...) が幽霊シンボルに張られていた。
        const string content = """
            package demo;

            @interface Ann {}

            class Root {
                Root(int value) {}
            }

            class Leaf extends @Ann Root {
                Leaf() {
                    super(0);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Root" && r.ContainerKind == "function" && r.ContainerName == "Leaf");
        Assert.DoesNotContain(references, r => r.SymbolName == "@Ann Root");
        Assert.DoesNotContain(references, r => r.SymbolName.StartsWith("@"));
        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void Extract_JavaSuperCall_TypeUseAnnotatedNestedGeneric_AttributesToTerminalSegment()
    {
        // Type-use annotation sitting between nested segments (`Outer<Integer>.@Ann Base`). The
        // resolver must strip the annotation and still return the terminal segment `Base`.
        // ネスト型の区切り位置 (`Outer<Integer>.@Ann Base`) に type-use annotation が入る形。
        // annotation を剥がしたうえで末尾セグメント `Base` を返せることを確認する。
        const string content = """
            package demo;

            @interface Ann {}

            class Outer<T> {
                static class Base {
                    Base(int value) {}
                }
            }

            class Leaf extends Outer<Integer>.@Ann Base {
                Leaf() {
                    super(0);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Base" && r.ContainerKind == "function" && r.ContainerName == "Leaf");
        Assert.DoesNotContain(references, r => r.SymbolName.StartsWith("@"));
        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void Extract_JavaThisCall_EnumConstructorChain_AttributesToEnum()
    {
        // Java enums can declare constructors and chain via `this(...)`. Before the fix the
        // enclosing-type lookup filtered to class/struct/interface, so enum bodies were skipped
        // and the chain edge was silently dropped (compounded by `this` being in the ignore set).
        // Java の enum はコンストラクタを宣言でき `this(...)` 連鎖も使える。修正前は外側型の
        // 走査が class/struct/interface に限定され、enum 本体は無視されて連鎖エッジが
        // 黙って落ちていた（`this` が ignore set に含まれることも原因として重なっていた）。
        const string content = """
            package demo;

            enum Shade {
                Red,
                Blue(1);

                Shade() {
                    this(0);
                }

                Shade(int code) {
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Shade" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Shade" && r.Line == 8);
        Assert.DoesNotContain(references, r => r.SymbolName == "this");
    }

    [Fact]
    public void Extract_JavaSuperCall_SameLineCtorWithModifierThenAnnotation_AttributesToRealBase()
    {
        // Regression for same-line ctor bodies where an access modifier precedes an annotation,
        // e.g. `public @Deprecated Leaf(...)`. Before the fix the scanner consumed the modifier
        // first, hit `@` in ConsumeIdentifier, and returned null, dropping the super(...) edge.
        // 修正前は modifier の後に annotation が来ると ctor 名抽出が失敗し、super(...) が落ちた。
        const string content = """
            package demo;

            class Root {
                Root(int value) {}
            }

            class Leaf extends Root {
                public @Deprecated Leaf(int x){super(x);}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Root" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Leaf" && r.Line == 8);
        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void Extract_JavaSuperChain_BraceAnnotationArgExtends_AttributesSuperEdgeEndToEnd()
    {
        // End-to-end regression for Java type-use annotated `extends` whose annotation argument
        // itself contains `{...}`, e.g. `extends @Ann({A.class, B.class}) Root`. Before the fix
        // the SymbolExtractor brace range for `class Leaf` stopped at the annotation-arg `}`
        // because it did not skip `{` / `}` inside `()`, so `FindInnermostClassLike` could not
        // recover the enclosing type for the `super(0)` line, and the chain edge was dropped.
        // ParseJavaBaseType alone already resolves the correct base type (locked in
        // CollectCSharpRecordHeader_MultiLineExtendsWithBraceAnnotationArg_CollectsThroughRealBase),
        // so this test locks the extractor-level body range fix so that the full
        // `super(...)` → enclosing class → base type chain makes it into the reference stream.
        // annotation 引数内に `{...}` を含む Java `extends` の E2E 回帰。修正前は SymbolExtractor の
        // brace range が annotation 引数の `}` で閉じてしまい、`super(0)` の外側型が見つからず
        // 連鎖エッジが完全に落ちていた。本テストは body-range fix を E2E で固定する。
        const string content = """
            package demo;

            @interface Ann {
                Class<?>[] value() default {};
            }

            class A {}
            class B {}

            class Root {
                Root(int value) {}
            }

            class Leaf extends @Ann({A.class, B.class}) Root {
                Leaf() {
                    super(0);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        // SymbolExtractor regression: the `class Leaf` symbol must span through the real class
        // body, not collapse onto the declaration line because of the annotation-arg braces.
        // Without this, the downstream `FindInnermostClassLike` lookup at the super(0) line
        // returns null and the chain edge is silently dropped.
        // `class Leaf` の body 範囲が annotation 引数内の `{}` によって 1 行に潰れないことを固定。
        var leafClass = Assert.Single(symbols, s =>
            s.Kind == "class" && s.Name == "Leaf");
        Assert.NotNull(leafClass.BodyStartLine);
        Assert.NotNull(leafClass.BodyEndLine);
        Assert.True(leafClass.BodyEndLine!.Value >= 16,
            $"Leaf class body_end_line must cover the super(0) line, got {leafClass.BodyEndLine}.");

        // The super(0) edge must attribute to `Root` (the real base), with the function-kind
        // container pointing at the Leaf constructor — not a phantom annotated name, and not
        // dropped entirely. This is the edge codex round 8 flagged as silently lost.
        // `super(0)` の連鎖エッジが `Root` に対して Leaf コンストラクタから張られることを E2E で固定。
        Assert.Contains(references, r =>
            r.SymbolName == "Root" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Leaf" && r.Line == 16);
        Assert.DoesNotContain(references, r => r.SymbolName.StartsWith("@"));
        Assert.DoesNotContain(references, r => r.SymbolName == "super");
    }

    [Fact]
    public void CollectCSharpRecordHeader_MultiLineExtendsWithBraceAnnotationArg_CollectsThroughRealBase()
    {
        // Depth-aware header collector regression. Before the fix the collector terminated at
        // the first raw `{` without tracking `()` / `[]` / `<>` depth, so the `{` inside
        // `@Ann({A.class, B.class})` truncated the header before `Root`. ParseJavaBaseType and
        // the C# record primary-ctor container synthesis both feed off this helper, so any
        // multi-line Java `extends` or C# record base-list that carries a brace-containing
        // annotation argument must still reach the real base type. An end-to-end regression
        // covering the same brace-annotation shape now lives in
        // Extract_JavaSuperChain_BraceAnnotationArgExtends_AttributesSuperEdgeEndToEnd.
        // annotation 引数内の `{}` をヘッダ終端と誤認しない depth-aware 収集を直接検証する。
        // ParseJavaBaseType と record primary-ctor container synthesis の両方が依存する。
        var structuralLines = new[]
        {
            "class Leaf",
            "    extends @Ann({A.class, B.class}) Root {",
            "    Leaf() {",
            "        super(0);",
            "    }",
            "}",
        };

        var (endLine, endColumn, text) = ReferenceExtractor.CollectCSharpRecordHeader(structuralLines, startLine: 1);

        // Header must reach `Root` (the real base), which means the collector treated the inner
        // `{` as nested rather than the body opener. The terminator `{` is the one right after
        // `Root`, so endLine points at the second structural line, endColumn points at that `{`,
        // and text ends with `Root `.
        Assert.Equal(2, endLine);
        Assert.Contains("Root", text);
        Assert.EndsWith("Root ", text);
        // The terminator `{` sits at the very end of `    extends @Ann({A.class, B.class}) Root {`
        // (the outer `{` after `Root `). Use the structural line length minus 1 to stay agnostic
        // to exact column math while still asserting the column points at that `{`.
        Assert.Equal(structuralLines[1].Length - 1, endColumn);
        Assert.Equal('{', structuralLines[1][endColumn]);

        // ParseJavaBaseType on the collected header must also resolve to `Root`, not a phantom
        // annotated segment. This is the downstream path that super(...) attribution uses.
        Assert.Equal("Root", ReferenceExtractor.ParseJavaBaseType(text));
    }

    [Fact]
    public void CollectCSharpRecordHeader_AttributeArgsWithComparisonOperator_TerminatesAtRealBrace()
    {
        // Regression: previously the collector tracked `<` / `>` as generic depth, so a
        // comparison operator inside an attribute expression (e.g. `[Attr(Flag = 1 < 2)]`)
        // left angleDepth pinned at 1 with no matching `>`, which silently skipped the real
        // top-level `{` terminator and let the synthetic primary-ctor container stretch to
        // EOF. That polluted `callers` / `impact` with phantom record-ctor callers for every
        // call in the file. Now only `()` / `[]` are tracked for terminator detection.
        // 属性引数内の比較演算子 `<` で angleDepth が残り、ヘッダ終端の `{` を取り逃す回帰を固定。
        var structuralLines = new[]
        {
            "[Attr(Flag = 1 < 2)]",
            "public class Child(int x) : Parent(x)",
            "{",
            "    public int Doubled => x * 2;",
            "}",
        };

        var (endLine, endColumn, text) = ReferenceExtractor.CollectCSharpRecordHeader(structuralLines, startLine: 1);

        Assert.Equal(3, endLine);
        Assert.Equal(0, endColumn);
        Assert.Equal('{', structuralLines[2][endColumn]);
        Assert.Contains("Parent(x)", text);
        Assert.DoesNotContain("Doubled", text);
    }

    [Fact]
    public void Extract_CSharpPrimaryCtor_AttributeArgsWithComparison_DoesNotLeakContainerToFollowingCalls()
    {
        // End-to-end regression: with `[Attr(Flag = 1 < 2)]` on a C# 12 class primary
        // constructor, the synthetic record-ctor container must stop at the real body `{`.
        // Previously the unbalanced `<` kept the synthetic container active through the rest
        // of the file, so body-level calls (including calls in later unrelated methods) were
        // misattributed to the primary-ctor function container.
        // 属性引数内の比較演算子で合成 primary-ctor コンテナが EOF まで伸び、後続の call を
        // 誤って primary ctor 経由と見せる回帰を E2E で固定する。
        const string content = """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            class AttrAttribute : Attribute
            {
                public int Flag { get; set; }
            }

            public class Parent(int v)
            {
                public int Value { get; } = v;
            }

            public class Helper
            {
                public static int Compute(int x) => x + 1;
            }

            [Attr(Flag = 1 < 2)]
            public class Child(int x) : Parent(x)
            {
                public int Doubled() => Helper.Compute(x);
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        // The `Parent(x)` base call must attribute to the synthetic `Child` ctor container.
        Assert.Contains(references, r =>
            r.SymbolName == "Parent" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Child");

        // The `Helper.Compute(x)` body call must NOT leak into the synthetic Child ctor;
        // it must belong to the `Doubled` function container instead.
        Assert.Contains(references, r =>
            r.SymbolName == "Compute" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Doubled");
        Assert.DoesNotContain(references, r =>
            r.SymbolName == "Compute" && r.ContainerKind == "function" && r.ContainerName == "Child");
    }

    [Fact]
    public void Extract_Java_BraceRangeIgnoresCommentsAndStringsInClassHeader()
    {
        // Regression: `FindBraceRange` previously counted raw parens/brackets/braces, so a
        // declaration header with unbalanced punctuation inside a line comment, block comment,
        // or string literal (for example `class Leaf extends Root /* ( */ { ... }`) left
        // parenDepth / bracketDepth non-zero and silently collapsed the Leaf body range to a
        // single line. The super(...) call site then fell outside the class body, so the
        // constructor-chain edge was dropped.
        // 宣言ヘッダ付近のコメント・文字列内の不均衡な `(` / `[` で body 範囲が潰れないことを固定。
        const string content = """
            package demo;

            class Root {
                Root(int value) {}
            }

            // The stray `(` and `[` below live inside comments / strings and must not leak
            // into the brace-depth counters that FindBraceRange uses.
            class Leaf extends Root /* ( stray [ */ {
                // marker "(" and '[' stay in comment/string space
                String tag = "open ( bracket [";
                Leaf() {
                    super(0);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        var leaf = Assert.Single(symbols, s => s.Kind == "class" && s.Name == "Leaf");
        Assert.NotNull(leaf.BodyStartLine);
        Assert.NotNull(leaf.BodyEndLine);
        Assert.True(leaf.BodyEndLine!.Value - leaf.BodyStartLine!.Value >= 3,
            $"Leaf class body must span several lines, got {leaf.BodyStartLine}..{leaf.BodyEndLine}");

        // Super chain must now attribute to Root, proving the brace range extended far enough
        // to contain the Leaf ctor body through the annotation-/comment-polluted header.
        Assert.Contains(references, r =>
            r.SymbolName == "Root" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Leaf");
    }

    [Fact]
    public void Extract_Java_AnnotationStringArgWithCloseParen_ResolvesBaseToRoot()
    {
        // Regression: StripJavaTypeAnnotations and SkipBalancedParens previously counted raw
        // parens, so a closing `)` inside a string literal such as `@Ann(text=")")` closed the
        // annotation early. ExtractBareTypeName then received a mangled prefix like `") Root`
        // and the super(...) edge was silently dropped (or misattributed) for otherwise legal
        // annotations with quoted arguments.
        // annotation 引数内の `)` / `(` を含む文字列リテラルを正しくスキップし、基底型解決が
        // 壊れないことを固定する。
        const string content = """
            package demo;

            class Root {
                Root(int value) {}
            }

            @interface Ann {
                String text();
            }

            class Leaf extends @Ann(text=")") Root {
                Leaf() {
                    super(0);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Root" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Leaf");
    }

    [Fact]
    public void Extract_Java_SameLineCtorWithQuotedAnnotationArg_KeepsSuperEdge()
    {
        // Regression: TryExtractJavaCtorNameFromLine walks past annotations using
        // SkipBalancedParens, which previously counted raw `)` characters. A legal
        // `@Ann(text=")")` prefix on a same-line ctor truncated annotation scanning at the
        // string's closing `)` and the ctor name read then started mid-string, so
        // TrySynthesizeSameLineJavaCtor returned null and the synthesized `super(...)` edge
        // added for same-line ctors vanished.
        // 同一行 ctor の annotation 文字列引数内 `)` が ctor 名抽出を壊さないことを固定する。
        const string content = """
            package demo;

            class Root {
                Root(int value) {}
            }

            @interface Ann {
                String text();
            }

            class Leaf extends Root {
                public @Ann(text=")") Leaf(){super(0);}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Root" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Leaf");
    }

    [Fact]
    public void Extract_Php_SingleQuotedDefaultArgument_PreservesFunctionBodyRange()
    {
        // Regression: the shared `FindBraceRange` `'` heuristic previously assumed a char
        // literal (closing `'` within ~12 chars) and silently skipped only the opening `'`
        // if none was found nearby. In PHP `'...'` is a full string literal, so a long
        // default argument such as `$x = 'aaaaaaaaaaaaaaa['` leaked the `[` into the shared
        // paren / bracket depth counters — `parenDepth` stayed > 0 at the real body `{`, so
        // `demo` ended up with null body range and `callThing()` lost its container. Now
        // the scanner treats `'...'` as a full string literal for PHP specifically.
        // PHP の `'...'` を char literal として扱うと長い文字列内の `[` / `{` で body 範囲が崩れる。
        const string content = """
            <?php
            function demo($x = 'aaaaaaaaaaaaaaa[') { return callThing(); }
            """;

        var symbols = SymbolExtractor.Extract(1, "php", content);
        var references = ReferenceExtractor.Extract(1, "php", content, symbols);

        var demo = Assert.Single(symbols, s => s.Name == "demo" && s.Kind == "function");
        Assert.NotNull(demo.BodyStartLine);
        Assert.NotNull(demo.BodyEndLine);

        Assert.Contains(references, r =>
            r.SymbolName == "callThing" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "demo");
    }

    [Fact]
    public void Extract_CSharpPrimaryCtor_SameLineAttributeCallsStayOnClassContainer()
    {
        // Regression: the synthetic C# primary-ctor container previously applied to every
        // call on the declaration's start line, including attribute argument calls that
        // textually precede the `class` keyword. `[Attr(Helper.Get())] public class Child(int x) : Parent(x) {}`
        // then attributed `Attr` and `Get` to the synthetic `function:Child` container, so
        // `callers Attr` / `callers Helper.Get` looked like the child ctor called them.
        // The synthetic range now tracks a start column at the `class` keyword, so tokens
        // before it stay on the real `class:Child` container while `Parent(x)` remains on
        // the synthetic ctor.
        // 宣言行より前の属性呼び出しが合成 primary-ctor コンテナに奪われないことを固定する。
        const string content =
            "[Attr(Helper.Get())] public class Child(int x) : Parent(x) {}\n";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "Attr" && r.ContainerKind == "class" && r.ContainerName == "Child");
        Assert.Contains(references, r =>
            r.SymbolName == "Get" && r.ContainerKind == "class" && r.ContainerName == "Child");
        Assert.Contains(references, r =>
            r.SymbolName == "Parent" && r.ContainerKind == "function" && r.ContainerName == "Child");

        // No `Attr` / `Get` references should be attributed to the synthetic function ctor.
        Assert.DoesNotContain(references, r =>
            (r.SymbolName == "Attr" || r.SymbolName == "Get")
            && r.ContainerKind == "function" && r.ContainerName == "Child");
    }

    [Fact]
    public void TryExtractJavaCtorNameFromLine_QuotedAnnotationArg_ReturnsCtorName()
    {
        // Direct regression on the shared annotation scanner: an `)` inside a string literal
        // argument must not end the balanced-paren walk, otherwise ctor name extraction lands
        // on the `"` and returns null.
        // annotation 引数の文字列リテラル内の `)` で走査が切れないことを直接検証。
        Assert.Equal(
            "Leaf",
            ReferenceExtractor.TryExtractJavaCtorNameFromLine("public @Ann(text=\")\") Leaf(){super(0);}"));
        Assert.Equal(
            "Leaf",
            ReferenceExtractor.TryExtractJavaCtorNameFromLine("@Ann(text=\"(\") Leaf(){this(0);}"));
    }

    [Fact]
    public void Extract_Java_SameLineCtorBodyCall_AttributesToCtorContainer()
    {
        // Regression: non-chain body calls on a same-line Java ctor (for example the
        // `Helper.doWork()` statement after `super(0);` in `Leaf(T x){super(0); Helper.doWork();}`)
        // previously landed on `class:Leaf` because SymbolExtractor does not emit a function
        // symbol for the same-line ctor shape. The main loop now pre-computes a per-line synthetic
        // function-kind container covering the body `{ ... }` span so body calls attribute to
        // `function:Leaf` instead of leaking to the enclosing class.
        // 同一行 ctor 本体の通常 call が外側 class に吸われず、合成 function コンテナに帰属することを固定する。
        const string content = """
            package demo;

            class Helper {
                static void doWork() {}
            }

            class Root {
                Root(int v) {}
            }

            class Leaf extends Root {
                public <T> Leaf(T x){super(0); Helper.doWork();}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.Contains(references, r =>
            r.SymbolName == "doWork" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Leaf");
        Assert.DoesNotContain(references, r =>
            r.SymbolName == "doWork" && r.ContainerKind == "class");

        // Chain edge remains on the synthetic function ctor too.
        // 連鎖エッジも合成 function コンテナに帰属したままであることを確認する。
        Assert.Contains(references, r =>
            r.SymbolName == "Root" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Leaf");
    }

    [Fact]
    public void Extract_Java_SameLineCtorDeclarator_DoesNotEmitSelfCall()
    {
        // Regression: `CallRegex` matches `CtorName(` on the declarator of a same-line Java ctor
        // and, without suppression, emitted a phantom `Leaf|call|class|Leaf` edge attributing the
        // declarator to the enclosing class. The main loop now skips the `CtorName(` match at the
        // declarator's name column when the current line carries a synthesized same-line ctor.
        // 同一行 ctor の宣言子 `CtorName(` が自己 call として記録されないことを固定する。
        const string content = """
            package demo;

            class Root {
                Root(int v) {}
            }

            class Leaf extends Root {
                Leaf(){super(0);}
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        Assert.DoesNotContain(references, r =>
            r.SymbolName == "Leaf" && r.ReferenceKind == "call");
        // But the chain rewrite still emits the `Root` edge attributed to `function:Leaf`.
        // 連鎖書き換えによる `Root` エッジは残っていることを確認する。
        Assert.Contains(references, r =>
            r.SymbolName == "Root" && r.ReferenceKind == "call"
            && r.ContainerKind == "function" && r.ContainerName == "Leaf");
    }
}
