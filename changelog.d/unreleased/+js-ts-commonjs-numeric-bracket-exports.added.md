---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript CommonJS numeric bracket exports are now searchable** — `exports[404] = value` and `module.exports[500] = function () {}` now surface exported symbols named `404` / `500`, while dynamic bracket expressions remain ignored.

## 日本語

- **JavaScript/TypeScript の CommonJS numeric bracket export を検索できるようになりました** — `exports[404] = value` や `module.exports[500] = function () {}` が `404` / `500` という exported シンボルとして表面化し、dynamic bracket expression は引き続き無視します。
