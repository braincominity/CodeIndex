---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# mutually recursive functions are now searchable** — `and isOdd n = ...` is indexed as a function symbol alongside the leading `let rec` definition.

## 日本語

- **F# の相互再帰関数が検索できるようになりました** — `and isOdd n = ...` を先頭の `let rec` 定義と同じく function symbol として索引します。
