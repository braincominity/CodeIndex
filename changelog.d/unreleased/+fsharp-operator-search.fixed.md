---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# operator definitions are now indexed for search** — `let (++)`-style operator bindings are normalized to searchable `function` symbols, so `symbols`, `definition`, and related lookups can find them instead of skipping the operator form.

## 日本語

- **F# の operator 定義が検索対象としてインデックスされるようになりました** — `let (++)` のような operator binding を検索可能な `function` シンボルに正規化することで、`symbols` / `definition` などの検索で operator 形が取りこぼされなくなりました。
