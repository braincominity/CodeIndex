---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# keyword struct declarations are now indexed as structs** — `type Coordinates = struct ...` is searchable as a `struct` symbol instead of falling through generic type patterns.

## 日本語

- **F# の keyword struct declaration が struct として索引されるようになりました** — `type Coordinates = struct ...` を汎用 type pattern に落とさず、`struct` シンボルとして検索できます。
