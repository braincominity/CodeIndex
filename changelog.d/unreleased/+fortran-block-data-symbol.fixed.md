---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Fortran.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Named Fortran `block data` units are now searchable** — `block data constants_block` declarations are indexed as symbols with their body ranges.

## 日本語

- **名前付き Fortran `block data` 単位が検索できるようになりました** — `block data constants_block` 宣言を本体範囲付きのシンボルとして索引します。
