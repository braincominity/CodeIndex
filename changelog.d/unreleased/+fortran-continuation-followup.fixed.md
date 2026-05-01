---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran continuation-heavy declarations now stay searchable outside interface blocks** — the continuation merger now also recognizes leading modifiers and type-specifier prefixes, so multi-line declarations such as `integer(kind=4) &` / `function ...` and `pure &` / `recursive &` / `subroutine ...` still surface as `function` symbols instead of being skipped.

## 日本語

- **Fortran の継続行が多い宣言も interface ブロック外で検索対象として残るようになりました** — 継続行の結合処理が先頭の修飾子や型指定子プレフィックスも認識するため、`integer(kind=4) &` / `function ...` や `pure &` / `recursive &` / `subroutine ...` のような複数行宣言も `function` シンボルとして現れ、取りこぼしません。
