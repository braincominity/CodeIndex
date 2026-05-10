---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran typed variable and component declarations are now searchable as symbols** — declarations such as `integer :: x` and `real :: a, b` index each declared name as a property symbol.

## 日本語

- **Fortran の型付き変数・component 宣言が symbol として検索できるようになりました** — `integer :: x` や `real :: a, b` のような宣言で各名前を property symbol として索引します。
