---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran enumerators are now searchable** — `enumerator :: color_red = 1, color_blue = 2` declarations index each enumerator as a property symbol.

## 日本語

- **Fortran の enumerator が検索できるようになりました** — `enumerator :: color_red = 1, color_blue = 2` 宣言で、各 enumerator を property symbol として索引します。
