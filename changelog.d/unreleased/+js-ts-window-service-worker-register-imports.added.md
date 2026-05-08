---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript `window.navigator.serviceWorker.register()` references are now searchable** — `window.navigator.serviceWorker.register("./sw.js")` now adds `import` symbols for static script specifiers while broader global object variants remain ignored.

## 日本語

- **JavaScript/TypeScript の `window.navigator.serviceWorker.register()` reference を検索できるようになりました** — `window.navigator.serviceWorker.register("./sw.js")` が静的な script specifier の `import` シンボルを追加し、より広い global object variant は引き続き無視します。
