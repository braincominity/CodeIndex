---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran attribute-only variable declarations now appear in symbol search** — declarations such as `allocatable :: work` and `pointer :: node` are indexed as properties.

## 日本語

- **Fortran の属性のみの変数宣言が symbol search に出るようになりました** — `allocatable :: work` や `pointer :: node` のような宣言を property として索引します。
