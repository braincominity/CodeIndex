---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript CommonJS `Object.defineProperty` exports are now searchable** — `Object.defineProperty(exports, "foo", ...)` and `Object.defineProperty(module.exports, "bar", ...)` now add exported `property` symbols while the `__esModule` marker and non-export targets stay skipped.

## 日本語

- **JavaScript/TypeScript の CommonJS `Object.defineProperty` export を検索できるようになりました** — `Object.defineProperty(exports, "foo", ...)` や `Object.defineProperty(module.exports, "bar", ...)` が exported `property` シンボルを追加し、`__esModule` marker や export 以外の target は引き続きスキップされます。
