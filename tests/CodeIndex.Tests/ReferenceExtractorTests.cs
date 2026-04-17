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
    public void Extract_CsharpNameofTypeofDefault_CapturesArgumentAsTypeReference()
    {
        // issue #253: nameof/typeof/sizeof/default arguments are first-class compile-time
        // references; previously CallRegex dropped them because the argument has no `(`.
        // issue #253: nameof/typeof/sizeof/default の引数はコンパイル時参照。従来は
        // 末尾に `(` が無いため CallRegex で取り逃がしていた。
        const string content = """
            namespace Demo;

            public class Target
            {
                public int Alpha() => 1;
                public static int Beta() => 2;
            }

            public class Caller
            {
                public void Work()
                {
                    var n1 = nameof(Target.Alpha);
                    var n2 = nameof(Target);
                    var n3 = nameof(Beta);
                    var tp = typeof(Target);
                    Target? def = default(Target);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var targetRefs = references.Where(r => r.SymbolName == "Target" && r.ReferenceKind == "type_reference").ToList();
        Assert.Equal(4, targetRefs.Count); // nameof(Target.Alpha), nameof(Target), typeof(Target), default(Target)
        Assert.All(targetRefs, r => Assert.Equal("Work", r.ContainerName));

        Assert.Contains(references, r => r.SymbolName == "Alpha" && r.ReferenceKind == "type_reference");
        Assert.Contains(references, r => r.SymbolName == "Beta" && r.ReferenceKind == "type_reference");

        // Keyword itself must never be emitted as a reference.
        // キーワード自体は参照として出力されない。
        Assert.DoesNotContain(references, r => r.SymbolName == "nameof");
        Assert.DoesNotContain(references, r => r.SymbolName == "typeof");
        Assert.DoesNotContain(references, r => r.SymbolName == "default");
    }

    [Fact]
    public void Extract_CsharpTypeof_HandlesGenericAndArrayArguments()
    {
        // typeof(List<int>) should capture `List`; the lexer skips `<int>` because `int` is a
        // built-in C# alias that must never appear as a type_reference row.
        // typeof(List<int>) は `List` を拾い、`<int>` 内の C# built-in alias はスキップする。
        const string content = """
            using System.Collections.Generic;

            public class Caller
            {
                public void Work()
                {
                    var t1 = typeof(List<int>);
                    var t2 = typeof(System.Collections.Generic.Dictionary<string, int>);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "List" && r.ReferenceKind == "type_reference");
        // For dotted args, each segment gets its own row — preserves rename safety.
        // ドット付き引数は segment ごとに行を出す — rename 追跡のため。
        Assert.Contains(references, r => r.SymbolName == "System" && r.ReferenceKind == "type_reference");
        Assert.Contains(references, r => r.SymbolName == "Collections" && r.ReferenceKind == "type_reference");
        Assert.Contains(references, r => r.SymbolName == "Generic" && r.ReferenceKind == "type_reference");
        Assert.Contains(references, r => r.SymbolName == "Dictionary" && r.ReferenceKind == "type_reference");

        // C# built-in aliases inside generic arguments must not surface as type_reference rows.
        // C# の built-in alias は type_reference としてインデックスしない。
        Assert.DoesNotContain(references, r => r.ReferenceKind == "type_reference" && (r.SymbolName == "int" || r.SymbolName == "string"));
    }

    [Fact]
    public void Extract_CsharpNameof_CapturesTrailingMemberAfterGenericMidPath()
    {
        // issue #253 review: `nameof(List<int>.Count)` must emit both `List` AND `Count`;
        // regex-only extraction stopped at `<`, dropping the actual member name that the user
        // is navigating to. The lexer skips generic `<...>` and resumes scanning at `.Count`.
        // issue #253 レビュー: `nameof(List<int>.Count)` は `List` と `Count` を両方拾う必要あり。
        const string content = """
            using System.Collections.Generic;

            public class Caller
            {
                public void Work()
                {
                    var n = nameof(List<int>.Count);
                    var n2 = nameof(System.Collections.Generic.Dictionary<string, int>.KeyCollection);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "List" && r.ReferenceKind == "type_reference");
        Assert.Contains(references, r => r.SymbolName == "Count" && r.ReferenceKind == "type_reference");
        Assert.Contains(references, r => r.SymbolName == "Dictionary" && r.ReferenceKind == "type_reference");
        Assert.Contains(references, r => r.SymbolName == "KeyCollection" && r.ReferenceKind == "type_reference");
    }

    [Fact]
    public void Extract_CsharpNameof_SkipsGlobalAliasQualifier()
    {
        // issue #253 review: `nameof(global::System.String)` — `global` is a namespace alias
        // qualifier and should NOT be captured; `System` is a built-in alias and should be
        // skipped; only real user-indexed path segments remain (none in this example).
        // Before the fix, only the bogus `global` token was emitted.
        // issue #253 レビュー: `global::` のエイリアス修飾子は捕捉しない。
        const string content = """
            public class Caller
            {
                public void Work()
                {
                    var n = nameof(global::System.String);
                    var n2 = nameof(MyAlias::Some.Type);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, r => r.SymbolName == "global" && r.ReferenceKind == "type_reference");
        Assert.DoesNotContain(references, r => r.SymbolName == "MyAlias" && r.ReferenceKind == "type_reference");
        // BCL type names (`String`, capital) are real types and must surface; only lowercase
        // alias keywords (`string`) are filtered.
        // BCL 型名（`String`）は実型なので捕捉し、lowercase の alias だけを除外する。
        Assert.Contains(references, r => r.SymbolName == "System" && r.ReferenceKind == "type_reference");
        Assert.Contains(references, r => r.SymbolName == "String" && r.ReferenceKind == "type_reference");
        Assert.Contains(references, r => r.SymbolName == "Some" && r.ReferenceKind == "type_reference");
        Assert.Contains(references, r => r.SymbolName == "Type" && r.ReferenceKind == "type_reference");
    }

    [Fact]
    public void Extract_CsharpTypeof_SkipsBuiltInAliasArguments()
    {
        // issue #253 review: `typeof(int)`, `sizeof(int)`, `typeof(string)` etc. resolve to
        // BCL primitives and should NOT pollute `references` / `inspect`. The ignore set
        // mirrors the Java primitive skip for `T.class`.
        // issue #253 レビュー: C# built-in alias は type_reference として索引しない。
        const string content = """
            public class Caller
            {
                public void Work()
                {
                    var t1 = typeof(int);
                    var t2 = typeof(string);
                    var t3 = typeof(object);
                    var s = sizeof(int);
                    var v = typeof(void);
                    var d = typeof(dynamic);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, r => r.ReferenceKind == "type_reference");
    }

    [Fact]
    public void Extract_CsharpTypeKeyword_CapturesTupleElementsAcrossInnerParens()
    {
        // issue #253 review #2: `typeof((Foo, Bar))` / `default((Foo, Bar))` are tuple-typed
        // arguments where the indexed identifiers only exist inside inner parentheses. The old
        // lexer treated `(` as "skip balanced body", silently dropping Foo and Bar. The fixed
        // lexer tracks paren depth instead, so tuple element segments surface normally while
        // outer `)` still terminates the scan.
        // issue #253 レビュー #2: `typeof((Foo, Bar))` / `default((Foo, Bar))` のタプル型引数は、
        // 識別子が内側括弧の中にしか存在しない。旧 lexer は `(` を SkipBalanced で飛ばしていたため
        // Foo / Bar が silent に落ちていた。括弧深さ追跡に変えたことで tuple 要素も拾えるようになる。
        const string content = """
            public class Foo {}
            public class Bar {}
            public class Caller
            {
                public void Work()
                {
                    var t = typeof((Foo, Bar));
                    var d = default((Foo, Bar));
                    var n = typeof((Foo, Bar)[]);
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.Contains(references, r => r.SymbolName == "Foo" && r.ReferenceKind == "type_reference");
        Assert.Contains(references, r => r.SymbolName == "Bar" && r.ReferenceKind == "type_reference");
    }

    [Fact]
    public void Extract_CsharpDefaultWithoutParens_IsNotCapturedAsTypeReference()
    {
        // `default;` alone (no parens) is a value expression, not a type reference.
        // The regex requires `default\s*\(...\)`, so bare `default` produces nothing.
        // `default;` 単体は値式であり型参照ではない。正規表現は `(` を必須にしている。
        const string content = """
            public class Caller
            {
                public int Work()
                {
                    return default;
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        Assert.DoesNotContain(references, r => r.ReferenceKind == "type_reference");
    }

    [Fact]
    public void Extract_JavaDotClass_CapturesTypeChainAsTypeReference()
    {
        // issue #253: Java's `T.class` / `T[].class` are compile-time type literals that rename
        // tools already treat as references; primitive types are skipped.
        // issue #253: Java の `T.class` はコンパイル時型リテラル。プリミティブは除外する。
        const string content = """
            package demo;

            public class Target {
                public int alpha() { return 1; }
            }

            public class Caller {
                public void work() {
                    Class<?> t1 = Target.class;
                    Class<?> t2 = Target[].class;
                    Class<?> t3 = int.class;
                    Class<?> t4 = demo.Target.class;
                }
            }
            """;

        var symbols = SymbolExtractor.Extract(1, "java", content);
        var references = ReferenceExtractor.Extract(1, "java", content, symbols);

        // Plain `Target.class` + `Target[].class` + segment from `demo.Target.class`.
        // `demo.Target.class` の Target + 素の Target.class + Target[].class = 3 箇所。
        var targetRefs = references.Where(r => r.SymbolName == "Target" && r.ReferenceKind == "type_reference").ToList();
        Assert.Equal(3, targetRefs.Count);

        Assert.Contains(references, r => r.SymbolName == "demo" && r.ReferenceKind == "type_reference");
        // Java primitive type must be skipped / Java プリミティブ型は除外。
        Assert.DoesNotContain(references, r => r.SymbolName == "int" && r.ReferenceKind == "type_reference");
    }

    [Fact]
    public void Extract_TypeReferenceSegmentColumnMatchesOriginalLine()
    {
        // Column positions must point to the start of each dot-segment in the original line
        // so tooling can jump directly to the identifier.
        // 列位置は元行の各 segment 先頭を指す必要がある。
        const string content = "var s = nameof(Outer.Inner);";

        var symbols = SymbolExtractor.Extract(1, "csharp", content);
        var references = ReferenceExtractor.Extract(1, "csharp", content, symbols);

        var outer = references.Single(r => r.SymbolName == "Outer" && r.ReferenceKind == "type_reference");
        var inner = references.Single(r => r.SymbolName == "Inner" && r.ReferenceKind == "type_reference");

        // 1-based: 'O' of Outer at column 16, 'I' of Inner at column 22.
        // 1 始まり: Outer の 'O' は 16 列目、Inner の 'I' は 22 列目。
        Assert.Equal(16, outer.Column);
        Assert.Equal(22, inner.Column);
    }
}
