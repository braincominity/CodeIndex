---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript CommonJS `Object.defineProperties` exports are now searchable** — `Object.defineProperties(exports, { foo: ... })` and `Object.defineProperties(module.exports, { "bar-baz": ... })` now add exported `property` symbols for static descriptor keys while ignoring `__esModule` and dynamic computed keys.

## 日本語

- **JavaScript/TypeScript の CommonJS `Object.defineProperties` export を検索できるようになりました** — `Object.defineProperties(exports, { foo: ... })` や `Object.defineProperties(module.exports, { "bar-baz": ... })` が静的な descriptor key を exported `property` シンボルとして追加し、`__esModule` と動的 computed key は無視します。
