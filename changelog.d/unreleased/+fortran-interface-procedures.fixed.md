---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran interface procedures and `end module procedure` handling are now more complete** — `module procedure`, `procedure(normalized_iface) :: name`, and `procedure, pointer :: name` declarations inside interfaces are indexed as searchable `function` symbols, and `end module procedure` lines are explicitly ignored by the module body-range scan so they do not truncate later module members.

## 日本語

- **Fortran の interface 手続きと `end module procedure` の扱いをより完全にしました** — interface 内の `module procedure`、`procedure(normalized_iface) :: name`、`procedure, pointer :: name` を検索可能な `function` シンボルとして索引し、`end module procedure` 行は module の body-range スキャンで明示的に無視するため、その後ろの module メンバーが途中で切れなくなります。
