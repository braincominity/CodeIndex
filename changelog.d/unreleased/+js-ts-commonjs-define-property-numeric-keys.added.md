---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript CommonJS `Object.defineProperty` numeric export keys are now searchable** — `Object.defineProperty(exports, 404, ...)` and multiline `Object.defineProperty(module.exports, 500, ...)` calls now add exported `property` symbols for static numeric property keys while still ignoring `__esModule`.

## 日本語

- **JavaScript/TypeScript の CommonJS `Object.defineProperty` numeric export key を検索できるようになりました** — `Object.defineProperty(exports, 404, ...)` や複数行の `Object.defineProperty(module.exports, 500, ...)` が静的な numeric property key を exported `property` シンボルとして追加し、`__esModule` は引き続き無視します。
