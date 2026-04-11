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
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Point");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
    }

    [Fact]
    public void Extract_CSharp_DetectsDelegateAndEvent()
    {
        // C# delegate and event / C# デリゲートとイベント
        var content = "public delegate void EventHandler(object sender, EventArgs e);\n\npublic class Button\n{\n    public event EventHandler Click;\n    public static event Action<string> OnLog;\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "EventHandler");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Button");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Click");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "OnLog");
    }

    [Fact]
    public void Extract_CSharp_DetectsProperties()
    {
        // C# property with get/set / C# プロパティ（get/set付き）
        var content = "public class User\n{\n    public string Name { get; set; }\n    public int Age { get; init; }\n    public virtual string? Email { get; set; }\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "User");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Name");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Age");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Email");
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
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Point");
        // Signature should contain the parameter list / シグネチャにパラメータリストが含まれるべき
        var service = symbols.First(s => s.Name == "Service");
        Assert.Contains("ILogger logger", service.Signature);
    }

    [Fact]
    public void Extract_CSharp_DetectsRecordVariants()
    {
        // record, record class, record struct with various modifiers
        var content = "public record UserDto(string Name, int Age);\npublic sealed record class Config(string Key, string Value);\ninternal readonly record struct Point(double X, double Y);";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "UserDto");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Point");
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

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "X" && s.ReturnType == "int");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Name" && s.ReturnType == "string");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Pi" && s.ReturnType == "double");
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

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Money");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "+");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "==");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "implicit");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "explicit");
    }

    [Fact]
    public void Extract_CSharp_DetectsRefStruct()
    {
        var content = "public readonly ref struct Span2D<T> { }\nref struct StackBuffer { }";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Span2D");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "StackBuffer");
    }

    [Fact]
    public void Extract_CSharp_DetectsEnumMembers()
    {
        var content = "public enum Color\n{\n    Red,\n    Green = 1,\n    Blue = 2,\n}";
        var symbols = SymbolExtractor.Extract(1, "csharp", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Color");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Red");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Green");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "Blue");
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
    public void Extract_Rust_DetectsFunctionsAndStructs()
    {
        var content = "pub fn handle_request() {}\npub struct Config {}\nimpl Config {";
        var symbols = SymbolExtractor.Extract(1, "rust", content);

        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "handle_request");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
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
    public void Extract_Kotlin_DetectsFunctionsAndClasses()
    {
        // Kotlin: class, fun / Kotlin: クラス、関数
        var content = "data class Config(val name: String)\nfun process(input: String): String {\n}";
        var symbols = SymbolExtractor.Extract(1, "kotlin", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "process");
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

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
        Assert.Contains(symbols, s => s.Kind == "function" && s.Name == "validate");
    }

    [Fact]
    public void Extract_C_DetectsFunctionsAndStructs()
    {
        // C: functions, struct / C: 関数、構造体
        var content = "typedef struct Config {\n    int value;\n};\nint main(int argc) {\n}";
        var symbols = SymbolExtractor.Extract(1, "c", content);

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Config");
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

        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "IUser");
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Status");
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
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Message");
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
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Status");
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
        Assert.Contains(symbols, s => s.Kind == "class" && s.Name == "Role");
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
