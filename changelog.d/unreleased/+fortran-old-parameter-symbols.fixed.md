---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Old-style Fortran `parameter (...)` constants are now searchable as symbols** — declarations such as `parameter (size = 4, limit = selected_int_kind(9))` index each constant.

## 日本語

- **旧形式の Fortran `parameter (...)` 定数が symbol として検索できるようになりました** — `parameter (size = 4, limit = selected_int_kind(9))` のような宣言で各定数を索引します。
