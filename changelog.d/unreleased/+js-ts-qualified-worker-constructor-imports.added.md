---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript qualified Worker constructors are now searchable** — `new window.Worker("./worker.js")` and `new globalThis.SharedWorker("./shared-worker.js")` now add `import` symbols for static script specifiers alongside unqualified Worker constructors.

## 日本語

- **JavaScript/TypeScript の qualified Worker constructor を検索できるようになりました** — `new window.Worker("./worker.js")` と `new globalThis.SharedWorker("./shared-worker.js")` が、unqualified Worker constructor と同様に静的な script specifier の `import` シンボルを追加します。
