---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# `let!` bindings are now searchable** — computation expression bindings such as `let! loadedUser = ...` are indexed with the same lightweight symbol coverage as ordinary `let` bindings.

## 日本語

- **F# の `let!` binding が検索できるようになりました** — `let! loadedUser = ...` のような computation expression binding を通常の `let` binding と同じ軽量symbolとして索引します。
