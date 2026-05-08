---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ module partitions are searchable as namespace symbols** — `export module app.core:api;` now indexes the full partition-qualified module name.

## 日本語

- **C++ module partition を namespace symbol として検索できるようになりました** — `export module app.core:api;` のような宣言で、partition を含む module 名全体を index します。
