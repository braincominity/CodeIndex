---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHP backed enum symbols now record their backing type** — declarations such as `enum Status: string` now keep `string` in symbol metadata, improving enum search results.

## 日本語

- **PHP backed enum シンボルが backing type を記録するようになりました** — `enum Status: string` のような宣言で `string` をシンボル metadata に保持し、enum 検索結果を改善します。
