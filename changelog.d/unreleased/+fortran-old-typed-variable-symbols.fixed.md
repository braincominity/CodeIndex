---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Old-style Fortran typed variable declarations are now searchable as symbols** — declarations such as `integer count, total` index each declared name without requiring `::`.

## 日本語

- **旧形式の Fortran 型付き変数宣言が symbol として検索できるようになりました** — `integer count, total` のような宣言で、`::` がなくても各名前を索引します。
