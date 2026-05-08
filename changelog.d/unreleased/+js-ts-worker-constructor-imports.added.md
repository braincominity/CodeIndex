---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript Worker constructor scripts are now searchable** — `new Worker("./worker.js")`, `new SharedWorker("./shared-worker.js", { type: "module" })`, and static template specifiers now add `import` symbols while dynamic templates, bare calls, and unrelated constructor names remain ignored.

## 日本語

- **JavaScript/TypeScript の Worker constructor script を検索できるようになりました** — `new Worker("./worker.js")`、`new SharedWorker("./shared-worker.js", { type: "module" })`、静的 template specifier が `import` シンボルを追加し、動的 template、bare call、無関係な constructor 名は引き続き無視します。
