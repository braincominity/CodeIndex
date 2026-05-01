---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# union cases and record fields are now searchable as symbols** — `SymbolExtractor` now emits searchable symbol rows for `type Color = Red | Green | Blue` cases and record fields inside `{ Name: string; Age: int }`, so the names inside common F# data declarations are easier to find.

## 日本語

- **F# の union case と record field を symbol として検索できるようになりました** — `SymbolExtractor` が `type Color = Red | Green | Blue` の case 名や `{ Name: string; Age: int }` 内の field 名を検索可能な symbol として出すため、F# の典型的なデータ定義の中身も見つけやすくなります。
