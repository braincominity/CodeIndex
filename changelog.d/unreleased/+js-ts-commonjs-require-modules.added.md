---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript CommonJS require calls now expose module imports** — `const fs = require("node:fs")` and multiline `require("./helper")` calls now add `import` symbols for their source modules while method calls such as `loader.require(...)` and `require.resolve(...)` stay skipped.

## 日本語

- **JavaScript/TypeScript の CommonJS require 呼び出しが module import として出るようになりました** — `const fs = require("node:fs")` や複数行の `require("./helper")` が source module の `import` シンボルを追加し、`loader.require(...)` や `require.resolve(...)` のような method 呼び出しは引き続きスキップされます。
