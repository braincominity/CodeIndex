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
}
