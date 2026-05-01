---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran `module procedure` declarations are now searchable** — `module procedure` lines are indexed as `function` symbols, so interface-bound procedure names show up in `symbols`, `definition`, and related lookups instead of being silently skipped.

## 日本語

- **Fortran の `module procedure` 宣言が検索対象になりました** — `module procedure` 行を `function` シンボルとして索引するため、interface に紐づく手続き名が `symbols`、`definition`、関連検索に現れ、無言で取りこぼされなくなります。
