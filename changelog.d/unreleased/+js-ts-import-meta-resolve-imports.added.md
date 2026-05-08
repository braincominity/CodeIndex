---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript `import.meta.resolve` specifiers are now searchable** — static module specifiers passed to `import.meta.resolve("./feature.js")` are indexed as `import` symbols, including calls with a parent URL argument.

## 日本語

- **JavaScript/TypeScript の `import.meta.resolve` specifier を検索できるようになりました** — `import.meta.resolve("./feature.js")` に渡された静的な module specifier を、parent URL 引数付きの call を含めて `import` シンボルとして索引します。
