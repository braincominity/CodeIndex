---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript CommonJS `Object.assign` exports are now searchable** — `Object.assign(exports, { foo })` and `Object.assign(module.exports, { "bar-baz": value })` now add exported `property` symbols for static object keys while leaving dynamic computed keys ignored.

## 日本語

- **JavaScript/TypeScript の CommonJS `Object.assign` export を検索できるようになりました** — `Object.assign(exports, { foo })` や `Object.assign(module.exports, { "bar-baz": value })` が静的な object key を exported `property` シンボルとして追加し、動的 computed key は引き続き無視します。
