using CodeIndex.Indexer;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for SymbolExtractor.
/// SymbolExtractorのテスト。
/// </summary>
public class SymbolExtractorTests
{
    [Fact]
    public void Extract_Python_DetectsFunctions()
    {
        // Should detect both sync and async functions
        // 同期・非同期関数を検出する
        var content = "def authenticate(user):\n    pass\nasync def fetch_data():\n    pass";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Equal(2, symbols.Count);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "authenticate");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetch_data");
    }

    [Fact]
    public void Extract_Python_DetectsClasses()
    {
        var content = "class UserService:\n    pass";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Single(symbols);
        Assert.Equal("class", symbols[0].Kind);
        Assert.Equal("UserService", symbols[0].Name);
    }

    [Fact]
    public void Extract_Python_DetectsDecoratedAndDunderMethods()
    {
        var content = "@dataclass\nclass User:\n    name: str\n    age: int\n\n    def __init__(self, name: str, age: int) -> None:\n        self.name = name\n\n    @property\n    def display_name(self) -> str:\n        return self.name\n\n    def __str__(self) -> str:\n        return self.name\n\n    @staticmethod\n    def create(name: str) -> 'User':\n        return User(name, 0)";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "__init__");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "display_name");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "__str__");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "create");
    }

    [Fact]
    public void Extract_JavaScript_DetectsFunctionsAndClasses()
    {
        // Should detect exported and non-exported functions and classes
        // export有無にかかわらず関数とクラスを検出する
        var content = "export function login() {}\nclass AuthService {}\nimport React from 'react'";
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "login");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "AuthService");
        Assert.Contains(symbols, s => s.Kind == "import");
    }

    [Fact]
    public void Extract_JavaScript_DoesNotTreatControlFlowBlocksAsFunctions()
    {
        var content = """
            class Parser {
                parse(value) {
                    if (value) {
                    }

                    for (const item of value.items) {
                    }

                    while (value.ready) {
                    }

                    switch (value.mode) {
                        case "fast":
                            break;
                    }
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Parser");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "parse");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "if");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "for");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "while");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "switch");
    }

    [Fact]
    public void Extract_JavaScript_AllowsKeywordNamedMethodsAtClassBodyDepthZero()
    {
        var content = """
            class KeywordMethods {
                if() {}
                catch() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "KeywordMethods");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "if");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "catch");
    }

    [Fact]
    public void Extract_JavaScript_IgnoresRegexLiteralBracesAndBlockCommentMethodShapes()
    {
        var content = """
            class Example {
                /*
                    fake() {
                    }
                */
                first() {
                    const open = /{/;
                    const close = /}/;
                }

                second() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "fake");
    }

    [Fact]
    public void Extract_JavaScript_KeepsSiblingMethodsAfterWrappedControlFlowRegexLiterals()
    {
        var content = """
            class Example {
                first(value) {
                    if (
                        ready
                    ) /{/.test(value);
                }

                second(value) {
                    if (first) {
                    }
                    else if (
                        secondReady
                    ) /{/.test(value);
                }

                third() {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "javascript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "third");
    }

    [Fact]
    public void Extract_TypeScript_DetectsAbstractClassAndNamespace()
    {
        var content = "export abstract class BaseService {\n    abstract getName(): string;\n}\ndeclare module 'express' {\n    interface Request { }\n}\nnamespace App.Models {\n    export type ID = string;\n}";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "BaseService");
        // Quoted ambient module declaration / 引用符付きアンビエントモジュール宣言
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "express");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "App.Models");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "ID");
    }

    [Fact]
    public void Extract_TypeScript_DoesNotTreatControlFlowBlocksAsFunctions()
    {
        var content = """
            export class Parser {
                override parse(value: Payload): Result {
                    if (value.ready) {
                    }

                    for (const item of value.items) {
                    }

                    while (value.more) {
                    }

                    switch (value.mode) {
                        case "fast":
                            return value.result;
                    }
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Parser");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "parse");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "if");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "for");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "while");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "switch");
    }

    [Fact]
    public void Extract_TypeScript_AllowsKeywordNamedMethodsAtClassBodyDepthZero()
    {
        var content = """
            export class KeywordMethods {
                if(): void {}
                catch(): string {
                    return "ok";
                }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "KeywordMethods");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "if");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "catch");
    }

    [Fact]
    public void Extract_TypeScript_IgnoresRegexLiteralBracesAndBlockCommentMethodShapes()
    {
        var content = """
            export class Example {
                /*
                    fake(): void {
                    }
                */
                first(): void {
                    const open = /{/;
                    const close = /}/;
                }

                second(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Example");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "fake");
    }

    [Fact]
    public void Extract_TypeScript_KeepsSiblingMethodsAfterWrappedControlFlowRegexLiterals()
    {
        var content = """
            export class Example {
                first(value: string): void {
                    if (
                        ready
                    ) /{/.test(value);
                }

                second(value: string): void {
                    if (first) {
                    }
                    else if (
                        secondReady
                    ) /{/.test(value);
                }

                third(): void {}
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "first");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "second");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "third");
    }

    [Fact]
    public void Extract_CSharp_DetectsClassesAndMethods()
    {
        var content = "public class UserService\n{\n    public async Task<User> GetUser(int id)\n    {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetUser");
    }

    [Fact]
    public void Extract_CSharp_DetectsFileScopedNamespaceAndRecordStruct()
    {
        // C# 10+: file-scoped namespace, global using, record struct
        var content = "global using System.Text;\nnamespace MyApp.Models;\n\npublic record struct Point(int X, int Y);\n\npublic record class User(string Name);";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name.Contains("System.Text"));
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "MyApp.Models");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Point");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
    }

    [Fact]
    public void Extract_CSharp_DetectsDelegateAndEvent()
    {
        // C# delegate and event / C# デリゲートとイベント
        var content = "public delegate void EventHandler(object sender, EventArgs e);\n\npublic class Button\n{\n    public event EventHandler Click;\n    public static event Action<string> OnLog;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "delegate" && s.Name == "EventHandler");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Button");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "Click");
        Assert.Contains(symbols, s => s.Kind == "event" && s.Name == "OnLog");
    }

    [Fact]
    public void Extract_CSharp_DetectsProperties()
    {
        // C# property with get/set / C# プロパティ（get/set付き）
        var content = "public class User\n{\n    public string Name { get; set; }\n    public int Age { get; init; }\n    public virtual string? Email { get; set; }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Name");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Age");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Email");
    }

    [Fact]
    public void Extract_CSharp_DetectsInlineAttributedProperty()
    {
        var content = """
            using System.Text.Json.Serialization;

            public class UserDto
            {
                [JsonPropertyName("full_name")] public string FullName { get; set; } = string.Empty;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserDto");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "FullName");
    }

    [Fact]
    public void Extract_CSharp_DetectsInlinePropertyTargetAttributeWithWhitespace()
    {
        var content = """
            using System.Text.Json.Serialization;

            public class UserDto
            {
                [property : JsonPropertyName("full_name")] public string FullName { get; set; } = string.Empty;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "FullName");
    }

    [Fact]
    public void Extract_CSharp_DetectsNoVisibilityMembers()
    {
        // Classes/methods without explicit visibility (internal by default)
        // 明示的な visibility のないクラス/メソッド（デフォルト internal）
        var content = "namespace MyApp;\n\nclass InternalClass\n{\n    static void Helper() { }\n}\n\nstatic class Utils\n{\n    static int Add(int a, int b) => a + b;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "InternalClass");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Helper");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Utils");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Add");
    }

    [Fact]
    public void Extract_CSharp_DetectsPrimaryConstructors()
    {
        // C# 12 primary constructors on class, struct, record
        // C# 12 のクラス・構造体・record の primary constructor
        var content = "public class Service(ILogger logger, IDb db)\n{\n}\n\npublic struct Point(double x, double y);";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Point");
        // Signature should contain the parameter list / シグネチャにパラメータリストが含まれるべき
        var service = symbols.First(s => s.Name == "Service");
        Assert.Contains("ILogger logger", service.Signature);
    }

    [Fact]
    public void Extract_CSharp_AttributeDoesNotBlockNextLine()
    {
        // [Attribute] on the line before class/method should not prevent extraction
        // [Attribute] がクラス/メソッドの前行にあっても抽出を妨げないこと
        var content = "[Serializable]\npublic class Config\n{\n    [Obsolete(\"Use V2\")]\n    public void OldMethod() { }\n\n    [HttpGet(\"/api\")]\n    public async Task<IActionResult> GetItems() { return null; }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "OldMethod");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetItems");
    }

    [Fact]
    public void Extract_CSharp_DetectsRecordVariants()
    {
        // record, record class, record struct with various modifiers
        var content = "public record UserDto(string Name, int Age);\npublic sealed record class Config(string Key, string Value);\ninternal readonly record struct Point(double X, double Y);";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserDto");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Point");
        // Signature should contain parameters / シグネチャにパラメータが含まれるべき
        var userDto = symbols.First(s => s.Name == "UserDto");
        Assert.Contains("string Name", userDto.Signature);
    }

    [Fact]
    public void Extract_CSharp_DetectsCompoundVisibility()
    {
        // protected internal and private protected / 複合アクセス修飾子
        var content = "public class Base\n{\n    protected internal void Foo() { }\n    private protected int Bar { get; set; }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var foo = symbols.FirstOrDefault(s => s.Name == "Foo");
        Assert.NotNull(foo);
        Assert.Equal("protected internal", foo.Visibility);

        var bar = symbols.FirstOrDefault(s => s.Name == "Bar");
        Assert.NotNull(bar);
        Assert.Equal("private protected", bar.Visibility);
    }

    [Fact]
    public void Extract_CSharp_DetectsUsingAlias()
    {
        var content = "using System;\nusing Json = System.Text.Json;\nusing static System.Math;\nglobal using Logging = Microsoft.Extensions.Logging;";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Json");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System.Math");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Logging");
    }

    [Fact]
    public void Extract_CSharp_DetectsConstAndStaticReadonly()
    {
        var content = "public class Config\n{\n    public const string Version = \"1.0\";\n    private const int MaxRetries = 3;\n    internal static readonly Dictionary<string, string> Map = new();\n    public string MutableField;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Version" && s.ReturnType == "string");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MaxRetries" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Map");
        // Regular mutable fields should NOT be extracted / 通常のフィールドは抽出されないこと
        Assert.DoesNotContain(symbols, s => s.Name == "MutableField");
    }

    [Fact]
    public void Extract_CSharp_DetectsExpressionBodiedMembers()
    {
        var content = "public class Calc\n{\n    public int X => 42;\n    public string Name => \"calc\";\n    public static double Pi => 3.14;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "X" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Name" && s.ReturnType == "string");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "Pi" && s.ReturnType == "double");
    }

    [Fact]
    public void Extract_CSharp_DetectsExplicitInterfaceImpl()
    {
        var content = "public class MyClass : IDisposable, IComparable<MyClass>\n{\n    void IDisposable.Dispose()\n    {\n    }\n    int IComparable<MyClass>.CompareTo(MyClass other) => 0;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Dispose" && s.ReturnType == "void");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "CompareTo" && s.ReturnType == "int");
    }

    [Fact]
    public void Extract_CSharp_DetectsIndexer()
    {
        var content = "public class Collection\n{\n    public string this[int index]\n    {\n        get => _items[index];\n        set => _items[index] = value;\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var indexer = symbols.FirstOrDefault(s => s.Name == "this");
        Assert.NotNull(indexer);
        Assert.Equal("function", indexer.Kind);
        Assert.Equal("string", indexer.ReturnType);
    }

    [Fact]
    public void Extract_CSharp_DetectsOperatorOverloads()
    {
        var content = "public struct Money\n{\n    public static Money operator +(Money a, Money b) => new();\n    public static bool operator ==(Money a, Money b) => true;\n    public static implicit operator decimal(Money m) => 0m;\n    public static explicit operator Money(decimal d) => new();\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Money");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "+");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "==");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "implicit");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit");
    }

    [Fact]
    public void Extract_CSharp_DetectsPartialMethods()
    {
        // C# 9 extended partial methods / C# 9 拡張 partial メソッド
        var content = "public partial class App\n{\n    partial void OnInit();\n    public partial string GetName();\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "App");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "OnInit");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetName");
    }

    [Fact]
    public void Extract_CSharp_DetectsGenericMethodOverloads()
    {
        // Issue #41: generic method overloads should both be extracted as definitions
        // Issue #41: ジェネリックメソッドのオーバーロードは両方とも定義として抽出されるべき
        var content = "public class App\n{\n    private static void TryRaise(Action? handler, string context) { }\n    private static void TryRaise<T>(Action<T>? handler, T argument, string context) { }\n    public Task<List<T>> GetItems<T>(int page) { return null; }\n    public void Process<TKey, TValue>(Dictionary<TKey, TValue> map) { }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "TryRaise" && s.Line == 3);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "TryRaise" && s.Line == 4);
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetItems");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Process");
    }

    [Fact]
    public void Extract_CSharp_DetectsFileScopedType()
    {
        // C# 11 file-scoped type / C# 11 のファイルスコープ型
        var content = "file class InternalHelper\n{\n    file static void DoWork() { }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "InternalHelper");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "DoWork");
    }

    [Fact]
    public void Extract_CSharp_DetectsRefStruct()
    {
        var content = "public readonly ref struct Span2D<T> { }\nref struct StackBuffer { }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Span2D");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "StackBuffer");
    }

    [Fact]
    public void Extract_CSharp_DetectsEnumMembers()
    {
        var content = "public enum Color\n{\n    Red,\n    Green = 1,\n    Blue = 2,\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Color");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Red");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Green");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Blue");
    }

    [Fact]
    public void Extract_CSharp_EnumMemberDoesNotMatchObjectInitializer()
    {
        // Object initializer lines should not be extracted as enum members
        // オブジェクト初期化子行は enum メンバーとして抽出されないこと
        var content = "var user = new User\n{\n    Name = \"Alice\",\n    Age = 30,\n    Email = GetEmail(),\n};";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.DoesNotContain(symbols, s => s.Name == "Name" && s.Kind == "function");
        Assert.DoesNotContain(symbols, s => s.Name == "Email" && s.Kind == "function");
    }

    [Fact]
    public void Extract_CSharp_DetectsRegionDirectives()
    {
        var content = "#region Private Methods\nvoid Helper() { }\n#endregion\n\n#region Properties\npublic int X { get; set; }\n#endregion";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "Private Methods");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "Properties");
    }

    [Fact]
    public void Extract_CSharp_DetectsStaticConstructorAndFinalizer()
    {
        var content = "public class Cache\n{\n    static Cache()\n    {\n        // init\n    }\n\n    ~Cache()\n    {\n        // cleanup\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Cache");
        // Static constructor — extracted by the static ctor pattern
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Cache" && s.Line == 3);
        // Finalizer
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Cache" && s.Line == 8);
    }

    [Fact]
    public void Extract_CSharp_DetectsNullableReturnTypeMethods()
    {
        var content = "public static class GitHelper\n{\n    public static string? ResolveGitCommonDir(string projectRoot)\n    {\n        return null;\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var method = Assert.Single(symbols.Where(s => s.Name == "ResolveGitCommonDir"));
        Assert.Equal("function", method.Kind);
        Assert.Equal("string?", method.ReturnType);
    }

    [Fact]
    public void Extract_AssignsContainerAndRanges_ForNestedMembers()
    {
        var content = "public class UserService\n{\n    public async Task<User> GetUser(int id)\n    {\n        return default!;\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        var classSymbol = Assert.Single(symbols.Where(s => s.Kind == "class"));
        var method = Assert.Single(symbols.Where(s => s.Kind == "function"));

        Assert.Equal(1, classSymbol.StartLine);
        Assert.Equal(7, classSymbol.EndLine);
        Assert.Equal(2, classSymbol.BodyStartLine);
        Assert.Equal(7, classSymbol.BodyEndLine);
        Assert.Equal("class", method.ContainerKind);
        Assert.Equal("UserService", method.ContainerName);
        Assert.Equal(3, method.StartLine);
        Assert.Equal(6, method.EndLine);
    }

    [Fact]
    public void Extract_Go_DetectsFunctions()
    {
        // Should detect both regular and method functions
        // 通常関数とメソッド関数の両方を検出する
        var content = "func NewHandler() *Handler {\n}\nfunc (h *Handler) ServeHTTP(w http.ResponseWriter, r *http.Request) {\n}";
        var symbols = SymbolExtractor.Extract(1, "go", content);

        Assert.Equal(2, symbols.Count);
        Assert.Contains(symbols, s => s.Name == "NewHandler");
        Assert.Contains(symbols, s => s.Name == "ServeHTTP");
    }

    [Fact]
    public void Extract_Shell_DetectsFunctions()
    {
        var content = "function setup() {\n  echo 'setup'\n}\n\ncleanup() {\n  echo 'cleanup'\n}";
        var symbols = SymbolExtractor.Extract(1, "shell", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "setup");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "cleanup");
    }

    [Fact]
    public void Extract_SQL_DetectsCreateStatements()
    {
        var content = "CREATE TABLE users (\n  id INT PRIMARY KEY\n);\n\nCREATE OR REPLACE FUNCTION get_user(id INT) RETURNS void;\n\nCREATE VIEW active_users AS SELECT * FROM users;\n\nALTER TABLE users ADD COLUMN email TEXT;";
        var symbols = SymbolExtractor.Extract(1, "sql", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "users");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "get_user");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "active_users");
    }

    [Fact]
    public void Extract_Terraform_DetectsResources()
    {
        var content = "resource \"aws_s3_bucket\" \"my_bucket\" {\n  bucket = \"my-bucket\"\n}\n\nvariable \"region\" {\n  default = \"us-east-1\"\n}\n\noutput \"bucket_arn\" {\n  value = aws_s3_bucket.my_bucket.arn\n}\n\nmodule \"vpc\" {\n  source = \"./modules/vpc\"\n}";
        var symbols = SymbolExtractor.Extract(1, "terraform", content);

        // resource captures logical name (second quoted token), not provider type
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "my_bucket");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "region");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "bucket_arn");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "vpc");
    }

    [Fact]
    public void Extract_PHP_DetectsExpandedFeatures()
    {
        var content = "<?php\nnamespace App\\Models;\n\nuse Illuminate\\Database\\Eloquent\\Model;\n\nreadonly class Config {\n    public const VERSION = '1.0';\n    public function getName(): string { return ''; }\n}\n\nenum Status: string {\n    case Active = 'active';\n}";
        var symbols = SymbolExtractor.Extract(1, "php", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name.Contains("App"));
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name.Contains("Model"));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "VERSION");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "getName");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
    }

    [Fact]
    public void Extract_Swift_DetectsActorAndTypealias()
    {
        var content = "public actor NetworkManager {\n    func fetch() { }\n}\n\npublic typealias Handler = (Data) -> Void\n\ndistributed actor RemoteWorker { }";
        var symbols = SymbolExtractor.Extract(1, "swift", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "NetworkManager");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetch");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Handler");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "RemoteWorker");
    }

    [Fact]
    public void Extract_Ruby_DetectsAttrAndRailsDSL()
    {
        var content = "class User < ActiveRecord::Base\n  attr_accessor :name\n  attr_reader :email\n  has_many :posts\n  belongs_to :company\n  scope :active\n\n  def initialize(name)\n    @name = name\n  end\nend";
        var symbols = SymbolExtractor.Extract(1, "ruby", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "name");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "email");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "posts");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "company");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "active");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "initialize");
    }

    [Fact]
    public void Extract_Rust_DetectsExpandedFeatures()
    {
        var content = "macro_rules! my_macro {\n    () => {};\n}\n\npub mod utils {\n}\n\nconst MAX_SIZE: usize = 1024;\nstatic COUNTER: AtomicU32 = AtomicU32::new(0);\npub const fn default_value() -> i32 { 42 }\npub unsafe fn raw_ptr() { }\ntype Result<T> = std::result::Result<T, Error>;\npub union MyUnion { f: f32 }";
        var symbols = SymbolExtractor.Extract(1, "rust", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "my_macro");
        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "utils");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MAX_SIZE");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "COUNTER");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "default_value");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "raw_ptr");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Result");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "MyUnion");
    }

    [Fact]
    public void Extract_Go_DetectsTypeAliasAndConst()
    {
        var content = "type Handler struct {\n}\ntype ID = string\ntype Logger interface {\n}\n\nconst (\n    MaxRetries = 3\n    DefaultTimeout = 30\n)\n\nvar GlobalConfig Config";
        var symbols = SymbolExtractor.Extract(1, "go", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Handler");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "ID");
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Logger");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MaxRetries");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "DefaultTimeout");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GlobalConfig");
    }

    [Fact]
    public void Extract_Rust_DetectsFunctionsAndStructs()
    {
        var content = "pub fn handle_request() {}\npub struct Config {}\nimpl Config {";
        var symbols = SymbolExtractor.Extract(1, "rust", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "handle_request");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Config");
    }

    [Fact]
    public void Extract_UnknownLang_ReturnsEmpty()
    {
        // Unsupported languages return no symbols
        // 未サポート言語は空を返す
        var symbols = SymbolExtractor.Extract(1, "markdown", "# Heading");
        Assert.Empty(symbols);
    }

    [Fact]
    public void Extract_NullLang_ReturnsEmpty()
    {
        var symbols = SymbolExtractor.Extract(1, null, "some content");
        Assert.Empty(symbols);
    }

    [Fact]
    public void Extract_LineNumbers_AreOneBased()
    {
        // Line numbers should be 1-based
        // 行番号は1始まりであること
        var content = "x = 1\ndef foo():\n    pass";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Single(symbols);
        Assert.Equal(2, symbols[0].Line);
    }

    [Fact]
    public void Extract_Java_DetectsClassesAndMethods()
    {
        // Java: class, interface, methods / Java: クラス、インターフェース、メソッド
        var content = "public class UserService {\n    public User getUser(int id) {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "getUser");
    }

    [Fact]
    public void Extract_Java_DetectsRecordAndSealedClass()
    {
        // Java 16+ record, Java 17+ sealed class / Java 16 の record、Java 17 の sealed class
        var content = "public record Point(int x, int y) { }\npublic sealed class Shape permits Circle, Rect { }";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Point");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Shape");
    }

    [Fact]
    public void Extract_Java_DetectsStaticFinalAndEnumMembers()
    {
        var content = "public class Config {\n    public static final String VERSION = \"1.0\";\n    private static final int MAX_RETRIES = 3;\n}\n\npublic enum Status {\n    ACTIVE,\n    INACTIVE,\n    PENDING;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "VERSION");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MAX_RETRIES");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ACTIVE");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "INACTIVE");
    }

    [Fact]
    public void Extract_Java_DetectsAnnotationType()
    {
        var content = "public @interface MyAnnotation {\n    String value();\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "MyAnnotation");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "value");
    }

    [Fact]
    public void Extract_Java_DetectsFlexibleConstantOrder()
    {
        // final static order (reversed) and generic types with spaces
        var content = "public class Config {\n    private final static int MAX = 100;\n    public static final Map<String, Integer> COUNTS = Map.of();\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "MAX");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "COUNTS");
    }

    [Fact]
    public void Extract_Java_DetectsEnumMembersWithTwoSpaceIndent()
    {
        // 2-space indent enum / 2スペースインデントの enum
        var content = "public enum Color {\n  RED,\n  GREEN,\n  BLUE;\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "RED");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GREEN");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "BLUE");
    }

    [Fact]
    public void Extract_Java_DetectsDefaultAndSynchronizedMethods()
    {
        var content = "public interface Service {\n    default void init() { }\n    static Service create() { return null; }\n}\npublic class Worker {\n    synchronized void process() { }\n}";
        var symbols = SymbolExtractor.Extract(1, "java", content);

        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "init");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "create");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "process");
    }

    [Fact]
    public void Extract_Kotlin_DetectsFunctionsAndClasses()
    {
        // Kotlin: class, fun / Kotlin: クラス、関数
        var content = "data class Config(val name: String)\nfun process(input: String): String {\n}";
        var symbols = SymbolExtractor.Extract(1, "kotlin", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "process");
    }

    [Fact]
    public void Extract_Kotlin_DetectsExpandedFeatures()
    {
        var content = "sealed interface Shape\nvalue class Email(val value: String)\ninner class Handler\n\ncompanion object {\n    const val MAX = 100\n}\n\nfun String.truncate(max: Int): String = take(max)\nsuspend fun fetchData(): List<Int> = emptyList()\ninline fun <reified T> parse(json: String): T = TODO()";
        var symbols = SymbolExtractor.Extract(1, "kotlin", content);

        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Shape");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Email");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Handler");
        // Companion object (unnamed) / コンパニオンオブジェクト（無名）
        Assert.Contains(symbols, s => s.Kind == "class" && s.Signature != null && s.Signature.Contains("companion object"));
        // Extension function / 拡張関数
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "truncate");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fetchData");
        // const val / 定数プロパティ
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "MAX");
    }

    [Fact]
    public void Extract_Ruby_DetectsDefAndClass()
    {
        // Ruby: def, class, module / Ruby: メソッド、クラス、モジュール
        var content = "class UserService\n  def find_user(id)\n  end\nend";
        var symbols = SymbolExtractor.Extract(1, "ruby", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "find_user");
    }

    [Fact]
    public void Extract_PHP_DetectsFunctionsAndClasses()
    {
        // PHP: function, class, interface / PHP: 関数、クラス、インターフェース
        var content = "class AuthService {\n    public function login($user) {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "php", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "AuthService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "login");
    }

    [Fact]
    public void Extract_Swift_DetectsFuncAndStruct()
    {
        // Swift: func, class, struct / Swift: 関数、クラス、構造体
        var content = "struct Config {\n    func validate() -> Bool {\n        return true\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "swift", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "validate");
    }

    [Fact]
    public void Extract_C_DetectsFunctionsAndStructs()
    {
        // C: functions, struct / C: 関数、構造体
        var content = "typedef struct Config {\n    int value;\n};\nint main(int argc) {\n}";
        var symbols = SymbolExtractor.Extract(1, "c", content);

        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "main");
    }

    [Fact]
    public void Extract_Cpp_DetectsClassAndNamespace()
    {
        // C++: class, namespace, functions / C++: クラス、名前空間、関数
        var content = "namespace MyApp {\nclass Handler {\n    void process(int data) {\n    }\n};\n}";
        var symbols = SymbolExtractor.Extract(1, "cpp", content);

        Assert.Contains(symbols, s => s.Kind == "namespace" && s.Name == "MyApp");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Handler" && s.ContainerName == "MyApp");
    }

    [Fact]
    public void Extract_TypeScript_DetectsInterfaceAndEnum()
    {
        // TypeScript: interface, type, enum / TypeScript: インターフェース、型、列挙型
        var content = "export interface IUser {\n    name: string;\n}\nexport enum Status {\n    Active,\n}";
        var symbols = SymbolExtractor.Extract(1, "typescript", content);

        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "IUser");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
    }

    [Fact]
    public void Extract_Haskell_DetectsTypeSignaturesAndDataTypes()
    {
        // Haskell: type signature, data, import / Haskell: 型シグネチャ、data型、import
        var content = "import Data.List\nimport qualified Data.Map as Map\n\ndata Tree a = Leaf | Node a (Tree a) (Tree a)\n\ninsert :: Ord a => a -> Tree a -> Tree a\ninsert x Leaf = Node x Leaf Leaf";
        var symbols = SymbolExtractor.Extract(1, "haskell", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Data.List");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "Data.Map");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Tree");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "insert");
    }

    [Fact]
    public void Extract_FSharp_DetectsLetTypeModuleOpen()
    {
        // F#: let, type, module, open / F#: let束縛、型、モジュール、open
        var content = "module MyApp.Domain\n\nopen System\n\ntype User = { Name: string; Age: int }\n\nlet validate user =\n    user.Age > 0\n\nlet rec factorial n =\n    if n <= 1 then 1 else n * factorial (n - 1)";
        var symbols = SymbolExtractor.Extract(1, "fsharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "MyApp.Domain");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "System");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "validate");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "factorial");
    }

    [Fact]
    public void Extract_FSharp_DoesNotMatchValueBindings()
    {
        // Value bindings should not be detected as functions / 値束縛は関数として検出されないこと
        var content = "let x = 5\nlet name = \"hello\"\nlet list = [1; 2; 3]";
        var symbols = SymbolExtractor.Extract(1, "fsharp", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "function");
    }

    [Fact]
    public void Extract_VB_DetectsCompoundVisibility()
    {
        // VB.NET compound visibility: Protected Friend / VB.NET 複合可視性
        var content = "Protected Friend Sub OnInit()\nEnd Sub\n\nPrivate Protected Function GetData() As String\n    Return \"\"\nEnd Function";
        var symbols = SymbolExtractor.Extract(1, "vb", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "OnInit");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetData");
    }

    [Fact]
    public void Extract_CSharp_DoesNotMatchFieldDeclarations()
    {
        // Fields should not be detected as properties / フィールドはプロパティとして検出されないこと
        var content = "public class Config\n{\n    public string Name;\n    private int _count;\n    public readonly string Id = \"x\";\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.DoesNotContain(symbols, s => s.Name == "Name" && s.Kind == "function");
        Assert.DoesNotContain(symbols, s => s.Name == "_count");
        Assert.DoesNotContain(symbols, s => s.Name == "Id" && s.Kind == "function");
    }

    [Fact]
    public void Extract_VB_DetectsSubFunctionClassModule()
    {
        // VB.NET: Sub, Function, Class, Module, Imports / VB.NET: サブ、関数、クラス、モジュール、Imports
        var content = "Imports System.IO\n\nPublic Class UserService\n    Public Sub Save(user As User)\n    End Sub\n\n    Private Function Validate(user As User) As Boolean\n        Return True\n    End Function\nEnd Class";
        var symbols = SymbolExtractor.Extract(1, "vb", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name.Contains("System.IO"));
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Save");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Validate");
    }

    [Fact]
    public void Extract_Haskell_DetectsIndentedAndLiterateSignatures()
    {
        // Indented where-clause signature and literate Haskell '>' prefix
        // インデントされたwhere節のシグネチャとliterate Haskellの'>'接頭辞
        var content = "  helper :: Int -> Int\n  helper x = x + 1\n> main :: IO ()\n> main = putStrLn \"hello\"";
        var symbols = SymbolExtractor.Extract(1, "haskell", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "helper");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "main");
    }

    [Fact]
    public void Extract_R_DoesNotMatchOrdinaryAssignment()
    {
        // Ordinary assignment should not be detected as a function
        // 通常の代入は関数として検出されないこと
        var content = "x <- 42\ny <- some_func(x)\nz <- list(1, 2, 3)";
        var symbols = SymbolExtractor.Extract(1, "r", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "function");
    }

    [Fact]
    public void Extract_R_DetectsFunctionAssignmentAndLibrary()
    {
        // R: function assignment, library / R: 関数代入、library
        var content = "library(ggplot2)\n\nmy_plot <- function(data, x, y) {\n  ggplot(data, aes(x, y))\n}";
        var symbols = SymbolExtractor.Extract(1, "r", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "ggplot2");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "my_plot");
    }

    [Fact]
    public void Extract_Lua_DetectsFunctionsAndRequire()
    {
        // Lua: function, local function, require / Lua: 関数、ローカル関数、require
        var content = "local http = require('socket.http')\n\nfunction greet(name)\n  print(name)\nend\n\nlocal function helper(x)\n  return x\nend";
        var symbols = SymbolExtractor.Extract(1, "lua", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "socket.http");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "greet");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "helper");
    }

    [Fact]
    public void Extract_Elixir_DetectsModuleAndFunctions()
    {
        // Elixir: defmodule, def, defp / Elixir: モジュール、関数、プライベート関数
        var content = "defmodule MyApp.Router do\n  def call(conn, _opts) do\n    conn\n  end\n\n  defp parse(data) do\n    data\n  end\nend";
        var symbols = SymbolExtractor.Extract(1, "elixir", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "MyApp.Router");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "call");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "parse");
    }

    [Fact]
    public void Extract_Scala_DetectsObjectTraitAndDef()
    {
        // Scala: object, trait, def, case class / Scala: オブジェクト、トレイト、def、ケースクラス
        var content = "object Main {\n  def run(): Unit = {\n  }\n}\nsealed trait Message\ncase class Ping(id: Int) extends Message";
        var symbols = SymbolExtractor.Extract(1, "scala", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Main");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "run");
        Assert.Contains(symbols, s => s.Kind == "interface" && s.Name == "Message");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Ping");
    }

    [Fact]
    public void Extract_Dart_DetectsClassFunctionAndMixin()
    {
        // Dart: class, mixin, function / Dart: クラス、mixin、関数
        var content = "abstract class Widget {\n  void build(BuildContext ctx) {\n  }\n}\nmixin Logging on Widget {\n}";
        var symbols = SymbolExtractor.Extract(1, "dart", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Widget");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "build");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Logging");
    }

    [Fact]
    public void Extract_Dart_DetectsImportAndEnum()
    {
        // Dart: import, enum / Dart: インポート、列挙型
        var content = "import 'package:flutter/material.dart';\n\nenum Status {\n  active,\n  inactive,\n}";
        var symbols = SymbolExtractor.Extract(1, "dart", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name.Contains("flutter"));
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Status");
    }

    [Fact]
    public void Extract_Dart_DoesNotMatchExpressionLines()
    {
        // Expressions that look like "type name(" but are not function definitions
        // 関数定義に見えるが実際は式の行を誤検出しないことを検証
        var content = "void main() {\n  return foo(bar);\n  await task(x);\n  const Widget(key: k);\n  throw Error('oops');\n}";
        var symbols = SymbolExtractor.Extract(1, "dart", content);

        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "foo");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "task");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Widget");
        Assert.DoesNotContain(symbols, s => s.Kind == "function" && s.Name == "Error");
        // main() should still be detected / main()は検出されるべき
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "main");
    }

    [Fact]
    public void Extract_GraphQL_DetectsSymbols()
    {
        var content = """
            type User {
              id: ID!
              name: String!
            }

            input CreateUserInput {
              name: String!
              email: String!
            }

            enum Role {
              ADMIN
              USER
            }

            query GetUser($id: ID!) {
              user(id: $id) { name }
            }

            mutation CreateUser($input: CreateUserInput!) {
              createUser(input: $input) { id }
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "graphql", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "CreateUserInput");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Role");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetUser");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "CreateUser");
    }

    [Fact]
    public void Extract_Gradle_DetectsSymbols()
    {
        // Both legacy apply plugin: and modern plugins { id '...' } forms
        // レガシーの apply plugin: と新しい plugins { id '...' } の両形式
        var content = "apply plugin: 'java'\n\nplugins {\n  id 'org.springframework.boot'\n}\n\ntask build {\n  doLast { println 'Building' }\n}\n\ndef customTask {\n  println 'custom'\n}\n";
        var symbols = SymbolExtractor.Extract(1, "gradle", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "java");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "org.springframework.boot");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "build");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "customTask");
    }

    [Fact]
    public void Extract_CSharp_DoesNotMatchCallSitesAsDefinitions()
    {
        // Issue #40: await/return/throw calls should not be extracted as method definitions
        // Issue #40: await/return/throw の呼び出しがメソッド定義として抽出されないこと
        var content = "public class Service\n{\n    public async Task InitAsync()\n    {\n        await TryNotifyAsync(true);\n        return GetResult();\n        throw CreateException(\"err\");\n        var x = ComputeValue(42);\n        yield return GenerateItem();\n    }\n\n    private async Task TryNotifyAsync(bool force)\n    {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // Real definitions should be extracted / 実際の定義は抽出されるべき
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Service");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "InitAsync");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "TryNotifyAsync");
        // Call sites should NOT be extracted as definitions / 呼び出し箇所は定義として抽出されないこと
        Assert.DoesNotContain(symbols, s => s.Name == "TryNotifyAsync" && s.Line == 5);
        Assert.DoesNotContain(symbols, s => s.Name == "GetResult");
        Assert.DoesNotContain(symbols, s => s.Name == "CreateException");
        Assert.DoesNotContain(symbols, s => s.Name == "ComputeValue");
        Assert.DoesNotContain(symbols, s => s.Name == "GenerateItem");
    }

    [Fact]
    public void Extract_CSharp_DoesNotMatchQualifiedCallSitesAsDefinitions()
    {
        // Qualified call sites (obj.Method()) should not match the explicit interface pattern
        // 修飾付き呼び出し (obj.Method()) は明示的インターフェース実装パターンにマッチしないこと
        var content = "public class Service\n{\n    public async Task Run()\n    {\n        return service.GetResult();\n        await client.SendAsync();\n        throw factory.CreateException(\"err\");\n    }\n\n    void IDisposable.Dispose()\n    {\n    }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        // Explicit interface impl should be extracted / 明示的インターフェース実装は抽出されるべき
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Dispose" && s.ReturnType == "void");
        // Qualified call sites should NOT be extracted / 修飾付き呼び出しは抽出されないこと
        Assert.DoesNotContain(symbols, s => s.Name == "GetResult");
        Assert.DoesNotContain(symbols, s => s.Name == "SendAsync");
        Assert.DoesNotContain(symbols, s => s.Name == "CreateException");
    }

    [Fact]
    public void Extract_CSharp_DoesNotMatchNamedArgumentFrameworkCallsAsDefinitions()
    {
        // Named-argument labels preceding qualified framework calls must not look like explicit interface impls.
        // 修飾付き framework call の前にある named-argument label を明示的インターフェース実装と誤認しないこと。
        var content = """
            public class PlatformState
            {
                public PlatformState(bool isWindows, bool isMacCatalyst)
                {
                }

                public static PlatformState Detect() =>
                    new(
                        isWindows: OperatingSystem.IsWindows(),
                        isMacCatalyst: OperatingSystem.IsMacCatalyst());
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "PlatformState");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Detect");
        Assert.DoesNotContain(symbols, s => s.Name == "IsWindows");
        Assert.DoesNotContain(symbols, s => s.Name == "IsMacCatalyst");
    }

    [Fact]
    public void Extract_CSharp_DetectsAliasQualifiedExplicitInterfaceImplementations()
    {
        // Alias-qualified return types should still match explicit interface implementations.
        // alias-qualified な戻り値型でも明示的インターフェース実装として抽出されること。
        var content = """
            public interface IFoo
            {
                string Name();
                object Create();
            }

            public class Impl : IFoo
            {
                global::System.String IFoo.Name() => "x";
                Alias::Type IFoo.Create() => default;
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Name" && s.ReturnType == "global::System.String");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Create" && s.ReturnType == "Alias::Type");
    }

    [Fact]
    public void Extract_CSharp_DetectsNewModifierMethods()
    {
        // C# `new` modifier for member hiding should still be extracted as definitions
        // C# のメンバー隠蔽用 `new` 修飾子は定義として抽出されるべき
        var content = "public class Derived : Base\n{\n    new void Reset() { }\n    new int Compare(object obj) { return 0; }\n    public new string ToString() { return \"\"; }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Reset");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Compare");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ToString");
    }

    [Fact]
    public void Extract_Python_DetectsPropertyDecorator()
    {
        var content = "class User:\n    @property\n    def name(self):\n        return self._name\n\n    def greet(self):\n        print(self.name)";
        var symbols = SymbolExtractor.Extract(1, "python", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "name");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "greet");
    }

    [Theory]
    [InlineData("csharp", "public interface IFoo { }", "interface")]
    [InlineData("csharp", "public enum Color { }", "enum")]
    [InlineData("csharp", "public struct Point { }", "struct")]
    [InlineData("csharp", "public delegate void Handler();", "delegate")]
    [InlineData("csharp", "public event EventHandler Click;", "event")]
    [InlineData("csharp", "public string Name { get; set; }", "property")]
    [InlineData("java", "interface Foo { }", "interface")]
    [InlineData("java", "enum Color { RED }", "enum")]
    [InlineData("kotlin", "interface Foo", "interface")]
    [InlineData("kotlin", "enum class Color { RED }", "enum")]
    [InlineData("kotlin", "val name: String = \"\"", "property")]
    [InlineData("typescript", "export interface IFoo { }", "interface")]
    [InlineData("typescript", "export enum Status { A }", "enum")]
    [InlineData("go", "type Foo struct { }", "struct")]
    [InlineData("go", "type Foo interface { }", "interface")]
    [InlineData("rust", "pub struct Config { }", "struct")]
    [InlineData("rust", "pub enum Color { }", "enum")]
    [InlineData("rust", "pub trait Foo { }", "interface")]
    [InlineData("swift", "struct Config { }", "struct")]
    [InlineData("swift", "enum Color { }", "enum")]
    [InlineData("swift", "protocol Foo { }", "interface")]
    [InlineData("c", "struct Config { };", "struct")]
    [InlineData("c", "enum Color { RED };", "enum")]
    [InlineData("cpp", "struct Config { };", "struct")]
    [InlineData("cpp", "enum Color { RED };", "enum")]
    [InlineData("php", "interface Foo { }", "interface")]
    [InlineData("php", "enum Color { }", "enum")]
    [InlineData("scala", "trait Foo", "interface")]
    [InlineData("scala", "enum Color", "enum")]
    [InlineData("dart", "enum Status { active }", "enum")]
    [InlineData("graphql", "interface Node { }", "interface")]
    [InlineData("graphql", "enum Role { ADMIN }", "enum")]
    [InlineData("haskell", "class Functor f where", "interface")]
    [InlineData("elixir", "defprotocol Enumerable do\nend", "interface")]
    [InlineData("ruby", "  attr_accessor :name", "property")]
    [InlineData("python", "@property\ndef name(self):", "property")]
    public void Extract_CrossLanguage_GranularKindsAreConsistent(string lang, string content, string expectedKind)
    {
        var symbols = SymbolExtractor.Extract(1, lang, content);
        Assert.Contains(symbols, s => s.Kind == expectedKind);
    }

    [Fact]
    public void EstimateComplexity_StraightLineFunction_ReturnsOne()
    {
        var body = "var x = 1;\nreturn x + 2;";
        Assert.Equal(1, SymbolExtractor.EstimateComplexity(body));
    }

    [Fact]
    public void EstimateComplexity_BranchingFunction_CountsBranches()
    {
        var body = "if (x > 0)\n    return x;\nelse if (x < 0)\n    return -x;\nfor (int i = 0; i < n; i++)\n    sum += i;\nwhile (retry)\n    attempt();\nvar result = a ?? b;\nvar flag = x && y || z;";
        // 1 (baseline) + if + else if + for + while + ?? + && + || = 8
        Assert.Equal(8, SymbolExtractor.EstimateComplexity(body));
    }

    [Fact]
    public void EstimateComplexity_EmptyBody_ReturnsOne()
    {
        Assert.Equal(1, SymbolExtractor.EstimateComplexity(""));
        Assert.Equal(1, SymbolExtractor.EstimateComplexity("   "));
    }

    [Fact]
    public void Extract_CSS_DetectsSymbols()
    {
        var content = "@import 'reset.css';\n\n$primary-color: #333;\n\n@mixin flex-center {\n  display: flex;\n  align-items: center;\n}\n\n@keyframes fade-in {\n  from { opacity: 0; }\n  to { opacity: 1; }\n}\n\n.container {\n  max-width: 1200px;\n}\n\n#header {\n  background: $primary-color;\n}";
        var symbols = SymbolExtractor.Extract(1, "css", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name.Contains("reset.css"));
        Assert.Contains(symbols, s => s.Kind == "property" && s.Name == "primary-color");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "flex-center");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "fade-in");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "container");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "header");
    }

    [Fact]
    public void Extract_PowerShell_DetectsSymbols()
    {
        var content = "Import-Module ActiveDirectory\nusing module PSDesiredStateConfiguration\n\nclass ServerConfig {\n    [string]$Name\n}\n\nenum Environment {\n    Dev\n    Staging\n    Prod\n}\n\nfunction Get-UserInfo {\n    param($UserId)\n    Get-ADUser -Identity $UserId\n}\n\nfilter Where-Active {\n    if ($_.Enabled) { $_ }\n}";
        var symbols = SymbolExtractor.Extract(1, "powershell", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "ActiveDirectory");
        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "PSDesiredStateConfiguration");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "ServerConfig");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Environment");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Get-UserInfo");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Where-Active");
    }

    [Fact]
    public void Extract_Zig_DetectsSymbols()
    {
        var content = "const std = @import(\"std\");\n\npub fn main() !void {\n    std.debug.print(\"hello\", .{});\n}\n\nfn helper(x: u32) u32 {\n    return x + 1;\n}\n\npub const Config = struct {\n    name: []const u8,\n};\n\nconst Direction = enum {\n    north,\n    south,\n};\n\ntest \"basic test\" {\n    try std.testing.expect(true);\n}";
        var symbols = SymbolExtractor.Extract(1, "zig", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "std");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "main" && s.Visibility == "pub");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "helper");
        Assert.Contains(symbols, s => s.Kind == "struct" && s.Name == "Config" && s.Visibility == "pub");
        Assert.Contains(symbols, s => s.Kind == "enum" && s.Name == "Direction");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "basic test");
    }

    [Fact]
    public void Extract_Makefile_DetectsTargets()
    {
        var content = "all: build test\n\nbuild:\n\tgcc -o main main.c\n\ntest:\n\t./run_tests\n\nclean:\n\trm -f main\n";
        var symbols = SymbolExtractor.Extract(1, "makefile", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "all");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "build");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "test");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "clean");
    }

    [Fact]
    public void Extract_Dockerfile_DetectsStages()
    {
        var content = "FROM node:18 AS builder\nWORKDIR /app\nCOPY . .\nRUN npm build\n\nFROM alpine:3.18\nCOPY --from=builder /app/dist /app\n";
        var symbols = SymbolExtractor.Extract(1, "dockerfile", content);

        // Named stages (AS builder) take priority over base image on the same line
        // 同一行では名前付きステージ(AS builder)がベースイメージより優先
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "builder");
        // Unnamed FROM lines produce base image class / 名前なしFROM行はベースイメージclassを生成
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "alpine:3.18");
        Assert.Equal(2, symbols.Count);
    }

    [Fact]
    public void Extract_Protobuf_DetectsSymbols()
    {
        var content = """
            syntax = "proto3";
            import "google/protobuf/timestamp.proto";

            message User {
              string name = 1;
              int32 age = 2;
            }

            enum Status {
              UNKNOWN = 0;
              ACTIVE = 1;
            }

            service UserService {
              rpc GetUser (GetUserRequest) returns (User);
              rpc ListUsers (ListUsersRequest) returns (ListUsersResponse);
            }
            """;
        var symbols = SymbolExtractor.Extract(1, "protobuf", content);

        Assert.Contains(symbols, s => s.Kind == "import" && s.Name == "google/protobuf/timestamp.proto");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Status");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserService");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "GetUser");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "ListUsers");
    }
}
