---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R backtick-escaped function names are now indexed** — function definitions such as `` `plot-model` <- function(...) `` are recognized as `function` symbols, so searches can find valid R identifiers that use punctuation and other non-syntactic characters.

## 日本語

- **R のバッククォート付き関数名を検索対象としてインデックスするようになりました** — `` `plot-model` <- function(...) `` のような定義を `function` シンボルとして認識するため、句読点などの非構文文字を含む有効な R 識別子も検索で見つけられるようになります。
