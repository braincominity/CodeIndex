---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript direct Service Worker registrations are now searchable** — `navigator.serviceWorker.register("./sw.js")` and option-bearing direct registrations now add `import` symbols for static script specifiers while dynamic paths remain ignored.

## 日本語

- **JavaScript/TypeScript の direct Service Worker registration を検索できるようになりました** — `navigator.serviceWorker.register("./sw.js")` と options 付き direct registration が静的な script specifier の `import` シンボルを追加し、動的 path は引き続き無視します。
