---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# abstract member shorthand is now searchable** — declarations such as `abstract Reset : unit -> unit` are indexed as function symbols even when the `member` keyword is omitted.

## 日本語

- **F# の abstract member 省略形が検索できるようになりました** — `member` を省いた `abstract Reset : unit -> unit` のような宣言を function symbol として索引します。
