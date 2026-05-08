---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript `importScripts()` dependencies are now searchable** — global `importScripts("./worker-a.js", "/worker-b.js")` calls now add one `import` symbol per static string or no-substitution template specifier while property-access calls and dynamic templates remain ignored.

## 日本語

- **JavaScript/TypeScript の `importScripts()` dependency を検索できるようになりました** — global な `importScripts("./worker-a.js", "/worker-b.js")` call が静的 string / no-substitution template specifier ごとに `import` シンボルを追加し、property-access call と動的 template は引き続き無視します。
