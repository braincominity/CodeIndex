---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# `val` declarations are now searchable** — signature declarations such as `val Id : string` and `val mutable Count : int` are indexed as property symbols.

## 日本語

- **F# の `val` 宣言が検索できるようになりました** — `val Id : string` や `val mutable Count : int` のようなsignature宣言を property symbol として索引します。
