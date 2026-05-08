---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript Worklet module loads are now searchable** — `audioWorklet.addModule("./processor.js")`, `CSS.paintWorklet.addModule("./paint.js")`, and related direct Worklet `addModule` calls now add `import` symbols for static module specifiers while dynamic paths and arbitrary `worklet.addModule(...)` calls remain ignored.

## 日本語

- **JavaScript/TypeScript の Worklet module load を検索できるようになりました** — `audioWorklet.addModule("./processor.js")`、`CSS.paintWorklet.addModule("./paint.js")`、関連する direct Worklet `addModule` call が静的な module specifier の `import` シンボルを追加し、動的 path と任意の `worklet.addModule(...)` call は引き続き無視します。
