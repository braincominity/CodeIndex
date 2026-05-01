---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran interface continuations and abstract interface prototypes are now searchable** — continuation-heavy `module procedure`, `procedure(...) :: name`, and `subroutine ...` declarations inside `interface` / `abstract interface` blocks are merged across `&` continuations before symbol matching, so they surface as `function` symbols instead of being skipped line by line.

## 日本語

- **Fortran の interface 継続行と abstract interface の prototype も検索対象になりました** — `interface` / `abstract interface` ブロック内の `module procedure`、`procedure(...) :: name`、`subroutine ...` 宣言を `&` 継続行ごとにまとめてからシンボル照合するため、1 行ずつでは取りこぼしていたものが `function` シンボルとして現れます。
