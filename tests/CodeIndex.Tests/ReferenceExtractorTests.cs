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
}
