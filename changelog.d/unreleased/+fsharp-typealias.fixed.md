---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# type abbreviations are now indexed for search** — `SymbolExtractor` now records `type UserId = int`-style aliases as `typealias` symbols, so common F# type names show up in `symbols` and definition-oriented views instead of being skipped.

## 日本語

- **F# の type abbreviation を検索用に索引するようになりました** — `SymbolExtractor` が `type UserId = int` 形式の alias を `typealias` シンボルとして記録するため、よく使われる F# の型名が `symbols` や definition 系の表示から落ちずに見えるようになりました。
