---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript exported object literals now index literal keys** — quoted and numeric keys such as `module.exports = { "x-api": handler, 404: notFound }` and `export default { "dash-key": value }` now appear as exported `property` symbols while computed keys stay skipped.

## 日本語

- **JavaScript/TypeScript の exported object literal で literal key を索引するようになりました** — `module.exports = { "x-api": handler, 404: notFound }` や `export default { "dash-key": value }` のような quoted / numeric key が exported `property` シンボルとして出る一方、computed key は引き続きスキップされます。
