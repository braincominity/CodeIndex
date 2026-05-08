---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript `require.resolve()` module references are now searchable** — static `require.resolve("./resolved")` calls now add `import` symbols for their module specifier while property-access calls such as `loader.require(...)` remain ignored.

## 日本語

- **JavaScript/TypeScript の `require.resolve()` module reference を検索できるようになりました** — 静的な `require.resolve("./resolved")` call が module specifier の `import` シンボルを追加し、`loader.require(...)` のような property-access call は引き続き無視します。
