---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Fortran.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran `module procedure` implementations now keep body ranges** — submodule implementations such as `module procedure normalize_impl ... end procedure` are indexed as function symbols with bodies.

## 日本語

- **Fortran の `module procedure` 実装が本体範囲付きで検索できるようになりました** — `module procedure normalize_impl ... end procedure` のような submodule 実装を、本体範囲付きの function symbol として索引します。
