---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# `open type` imports now index the opened type** — `open type System.Math` is searchable as an import for `System.Math` instead of stopping at the contextual keyword.

## 日本語

- **F# の `open type` import が開いた型名で索引されるようになりました** — `open type System.Math` は文脈キーワードで止まらず、`System.Math` の import として検索できます。
