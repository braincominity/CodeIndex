---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran `parameter` constants are now searchable** — typed declarations such as `integer, parameter :: max_rank = 8, default_rank = 2` index each parameter as a property symbol.

## 日本語

- **Fortran の `parameter` 定数が検索できるようになりました** — `integer, parameter :: max_rank = 8, default_rank = 2` のような型付き宣言で、各 parameter を property symbol として索引します。
