using CodeIndex.Cli;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for SourceCodeDetector (source code leak prevention).
/// SourceCodeDetectorのテスト（ソースコード漏洩防止）。
///
/// These tests verify that:
/// - Natural-language descriptions of gaps/errors are ALLOWED (return false)
/// - Pasted source code blocks are REJECTED (return true)
/// - Short inline code examples are ALLOWED
/// - Edge cases are handled correctly
/// </summary>
public class SourceCodeDetectorTests
{
    // ================================================================
    // ALLOWED inputs — these should NOT be flagged as source code.
    // 許容される入力 — ソースコードとしてフラグされるべきではない。
    // ================================================================

    [Theory]
    [InlineData("TypeScript の arrow function がシンボル抽出で拾えない")]
    [InlineData("Symbol extraction misses Kotlin data classes")]
    [InlineData("class keyword is incorrectly recognized as record")]
    [InlineData("cdidx search で NullReferenceException が発生した")]
    [InlineData("The search ranking puts test files above source files")]
    [InlineData("Reference extraction does not work for Go interfaces")]
    public void AllowsNaturalLanguageDescriptions(string text)
    {
        Assert.False(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void AllowsShortInlineCodeExample()
    {
        // A single backtick-wrapped example should be allowed.
        // バッククォートで囲まれた短い例示は許容されるべき。
        var text = "Symbol extraction misses arrow functions like `const foo = () => {}`";
        Assert.False(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void AllowsSingleLineCodeMention()
    {
        // Mentioning a single line of code in a sentence is fine.
        // 文中で1行のコードに言及するのは問題ない。
        var text = "When I write `public class MyRecord : IDisposable`, the symbol extractor misses it.";
        Assert.False(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void AllowsErrorMessageDescription()
    {
        // Describing an error message is not source code.
        // エラーメッセージの記述はソースコードではない。
        var text = "The tool crashed with: System.NullReferenceException: Object reference not set to an instance of an object.\n"
                 + "This happened when searching for symbols in a large TypeScript file.";
        Assert.False(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void AllowsShortBulletList()
    {
        // A bullet list of issues is not source code.
        // 課題の箇条書きはソースコードではない。
        var text = "Problems observed:\n"
                 + "- Arrow functions not detected\n"
                 + "- Class expressions ignored\n"
                 + "- Decorators cause parse errors\n"
                 + "- Default exports not indexed";
        Assert.False(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void AllowsEmptyOrWhitespace()
    {
        Assert.False(SourceCodeDetector.ContainsSourceCode(""));
        Assert.False(SourceCodeDetector.ContainsSourceCode("   "));
        Assert.False(SourceCodeDetector.ContainsSourceCode(null!));
    }

    [Fact]
    public void AllowsTwoLineCodeSnippet()
    {
        // Two lines of code-like text should not trigger (threshold is 3).
        // 2行のコード的テキストでは発動しない（しきい値は3）。
        var text = "Example pattern that fails:\n"
                 + "    const handler = (e) => {\n"
                 + "The above is not extracted as a symbol.";
        Assert.False(SourceCodeDetector.ContainsSourceCode(text));
    }

    // ================================================================
    // REJECTED inputs — these SHOULD be flagged as source code.
    // 拒否される入力 — ソースコードとしてフラグされるべき。
    // ================================================================

    [Fact]
    public void RejectsMultiLineCodeBlock()
    {
        // A typical C# method pasted verbatim.
        // C# のメソッドがそのままコピペされた典型例。
        var text = "public void ProcessFile(string path)\n"
                 + "{\n"
                 + "    var content = File.ReadAllText(path);\n"
                 + "    var lines = content.Split('\\n');\n"
                 + "    foreach (var line in lines)\n"
                 + "    {\n"
                 + "        Console.WriteLine(line);\n"
                 + "    }\n"
                 + "}";
        Assert.True(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void RejectsJavaScriptFunction()
    {
        var text = "function calculateTotal(items) {\n"
                 + "    let total = 0;\n"
                 + "    for (const item of items) {\n"
                 + "        total += item.price;\n"
                 + "    }\n"
                 + "    return total;\n"
                 + "}";
        Assert.True(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void RejectsPythonFunction()
    {
        var text = "def process_data(data):\n"
                 + "    result = []\n"
                 + "    for item in data:\n"
                 + "        if item.is_valid():\n"
                 + "            result.append(item)\n"
                 + "    return result";
        Assert.True(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void RejectsImportBlock()
    {
        // A block of import statements (top of a file).
        // import 文のブロック（ファイル先頭のコピペ）。
        var text = "import React from 'react';\n"
                 + "import { useState, useEffect } from 'react';\n"
                 + "import axios from 'axios';\n"
                 + "import { Button } from './components';";
        Assert.True(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void RejectsUsingBlock()
    {
        var text = "using System;\n"
                 + "using System.Collections.Generic;\n"
                 + "using System.Linq;\n"
                 + "using Microsoft.Data.Sqlite;";
        Assert.True(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void RejectsClassDefinition()
    {
        var text = "public class UserService {\n"
                 + "    private readonly ILogger _logger;\n"
                 + "    private readonly IUserRepository _repo;\n"
                 + "    public UserService(ILogger logger, IUserRepository repo) {\n"
                 + "        _logger = logger;\n"
                 + "        _repo = repo;\n"
                 + "    }\n"
                 + "}";
        Assert.True(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void RejectsStatementHeavyText()
    {
        // Text where most lines end with semicolons.
        // ほとんどの行がセミコロンで終わるテキスト。
        var text = "var x = 1;\n"
                 + "var y = 2;\n"
                 + "var z = x + y;\n"
                 + "Console.WriteLine(z);\n"
                 + "Console.WriteLine(x);\n"
                 + "Console.WriteLine(y);";
        Assert.True(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void RejectsRustFunction()
    {
        var text = "fn process(input: &str) -> Result<String, Error> {\n"
                 + "    let parsed = parse_input(input)?;\n"
                 + "    let result = transform(parsed);\n"
                 + "    Ok(result.to_string())\n"
                 + "}";
        Assert.True(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void RejectsGoFunction()
    {
        var text = "func handleRequest(w http.ResponseWriter, r *http.Request) {\n"
                 + "    body, err := io.ReadAll(r.Body)\n"
                 + "    if err != nil {\n"
                 + "        http.Error(w, err.Error(), 500)\n"
                 + "        return\n"
                 + "    }\n"
                 + "    w.Write(body)\n"
                 + "}";
        Assert.True(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void RejectsIncludeBlock()
    {
        var text = "#include <stdio.h>\n"
                 + "#include <stdlib.h>\n"
                 + "#include <string.h>";
        Assert.True(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void RejectsPythonImportBlock()
    {
        var text = "from pathlib import Path\n"
                 + "from typing import List, Optional\n"
                 + "from dataclasses import dataclass";
        Assert.True(SourceCodeDetector.ContainsSourceCode(text));
    }

    // ================================================================
    // Edge cases / エッジケース
    // ================================================================

    [Fact]
    public void AllowsDescriptionWithCodeKeywords()
    {
        // Using code keywords in natural language sentences should be fine.
        // 自然言語文中でのコードキーワード使用は問題ない。
        var text = "The 'return' keyword inside a lambda is not detected as a symbol.\n"
                 + "Also, 'if' expressions in Kotlin are treated as statements.\n"
                 + "This affects how 'var' declarations are parsed.";
        Assert.False(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void AllowsMarkdownFormatting()
    {
        // Markdown-style formatting in descriptions should be fine.
        // 説明内のMarkdown形式は問題ない。
        var text = "## Problem\n"
                 + "When indexing TypeScript files:\n"
                 + "- Arrow functions `=>` are not detected\n"
                 + "- Template literals `${}` cause issues\n"
                 + "\n"
                 + "## Expected\n"
                 + "Both should be handled correctly.";
        Assert.False(SourceCodeDetector.ContainsSourceCode(text));
    }

    [Fact]
    public void AllowsSingleImportMention()
    {
        // Mentioning one or two imports is fine (threshold is 3).
        // 1〜2行の import 言及は問題ない（しきい値は3）。
        var text = "The line `import React from 'react'` is not detected.\n"
                 + "Also `import { useState } from 'react'` is missed.";
        Assert.False(SourceCodeDetector.ContainsSourceCode(text));
    }
}
