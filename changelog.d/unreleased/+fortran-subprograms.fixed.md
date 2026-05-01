---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Fortran modules, programs, subroutines, and functions are now indexed** — `module`, `program`, `subroutine`, and `function` declarations are now surfaced as searchable symbols, including common modifier and typed-function forms.

## 日本語

- **Fortran の module / program / subroutine / function を index するようになりました** — `module`、`program`、`subroutine`、`function` 宣言が検索可能なシンボルとして出力され、一般的な修飾子付き・型付き function 形式にも対応しました。
