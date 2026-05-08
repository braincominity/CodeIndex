---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript `new URL(..., import.meta.url)` references are now searchable** — static string and no-substitution template specifiers such as `new URL("./worker.js", import.meta.url)` now add `import` symbols while dynamic templates and non-`import.meta.url` bases remain ignored.

## 日本語

- **JavaScript/TypeScript の `new URL(..., import.meta.url)` reference を検索できるようになりました** — `new URL("./worker.js", import.meta.url)` のような静的 string / no-substitution template specifier が `import` シンボルを追加し、動的 template と `import.meta.url` 以外の base は引き続き無視します。
