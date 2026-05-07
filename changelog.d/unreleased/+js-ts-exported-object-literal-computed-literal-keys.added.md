---
category: added
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.JavaScriptTypeScriptSupport.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - USER_GUIDE.md
---

## English

- **JavaScript/TypeScript exported object literals now index computed literal keys** — static computed keys such as `module.exports = { ["computed-api"]: handler, [500]: notFound }` and `export default { ["dash-key"]: handler }` now appear as exported `property` symbols while dynamic computed keys stay skipped.

## 日本語

- **JavaScript/TypeScript の exported object literal で computed literal key を索引するようになりました** — `module.exports = { ["computed-api"]: handler, [500]: notFound }` や `export default { ["dash-key"]: handler }` のような静的 computed key が exported `property` シンボルとして出る一方、動的 computed key は引き続きスキップされます。
